using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace DotnetAICraft.Roslyn;

public static class SymbolResolver
{
    /// <summary>
    /// Resolves a symbol by source file location (file + 1-based line + 1-based col).
    /// This is the most reliable way for an agent to identify a symbol after reading source.
    /// </summary>
    public static async Task<ISymbol> FromLocationAsync(
        Solution solution,
        string filePath,
        int line,
        int col,
        CancellationToken ct = default)
    {
        var normalizedPath = Path.GetFullPath(filePath);

        var docId = solution.GetDocumentIdsWithFilePath(filePath).FirstOrDefault()
            ?? solution.GetDocumentIdsWithFilePath(normalizedPath).FirstOrDefault()
            ?? throw new ArgumentException(
                $"File not found in solution: {filePath}\n" +
                $"Tip: make sure the path is absolute or relative to the solution directory.",
                nameof(filePath));

        var document = solution.GetDocument(docId)!;
        var sourceText = await document.GetTextAsync(ct);

        // Convert 1-based to 0-based
        var lineIndex = line - 1;
        var colIndex  = col  - 1;

        if (lineIndex < 0 || lineIndex >= sourceText.Lines.Count)
            throw new ArgumentOutOfRangeException(nameof(line),
                $"Line {line} is out of range (file has {sourceText.Lines.Count} lines).");

        if (colIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(col),
                $"Column {col} is out of range (must be >= 1).");

        var textLine   = sourceText.Lines[lineIndex];
        var position   = textLine.Start + Math.Min(colIndex, textLine.End - textLine.Start);

        var semanticModel = await document.GetSemanticModelAsync(ct)
            ?? throw new InvalidOperationException("Could not get semantic model for document.");

        var root = await document.GetSyntaxRootAsync(ct)!;
        var node = root!.FindToken(position).Parent;

        // Walk up to find the nearest named syntax node
        while (node is not null)
        {
            var symbol = semanticModel.GetSymbolInfo(node, ct).Symbol
                      ?? semanticModel.GetDeclaredSymbol(node, ct);

            if (symbol is not null)
                return symbol;

            node = node.Parent;
        }

        throw new ArgumentException(
            $"No symbol found at {filePath}:{line}:{col}.\n" +
            $"Tip: point to the symbol identifier, not whitespace or punctuation.");
    }

    /// <summary>
    /// Resolves a single symbol by its fully-qualified name (e.g. "MyApp.Services.OrderService.Process").
    /// Throws when the name is not found, or when it is ambiguous (matches more than one symbol, e.g.
    /// overloaded methods/constructors addressed without a parameter signature). For commands that can
    /// operate on every match, use <see cref="FromFullNameAllAsync"/> instead.
    /// </summary>
    public static async Task<ISymbol> FromFullNameAsync(
        Solution solution,
        string fullName,
        CancellationToken ct = default)
    {
        var matches = await FromFullNameAllAsync(solution, fullName, ct);
        if (matches.Count == 1)
            return matches[0];

        var candidates = string.Join("\n", matches.Select(s => "  " + s.ToDisplayString()));
        throw new ArgumentException(
            $"Symbol '{fullName}' is ambiguous — {matches.Count} matches:\n{candidates}\n" +
            "Tip: disambiguate with the full parameter signature, e.g. " +
            "'MyApp.Services.OrderService.ProcessOrder(MyApp.OrderDto)', or use --file/--line/--col.",
            nameof(fullName));
    }

    /// <summary>
    /// Resolves all symbols matching a fully-qualified name. Accepts both the parameterless form
    /// ("Ns.Type.Member") and the parameterized form emitted by <c>symbols</c>
    /// ("Ns.Type.Member(System.String)"). Constructors are addressable by the repeated type name
    /// ("Ns.Type.Type"), with parameters ("Ns.Type.Type(System.String)"), or via "#ctor" — including
    /// implicit/compiler-generated default constructors that have no source position.
    /// Slower than location-based — searches all projects.
    /// </summary>
    public static async Task<IReadOnlyList<ISymbol>> FromFullNameAllAsync(
        Solution solution,
        string fullName,
        CancellationToken ct = default)
    {
        var target = fullName.Trim();
        var nameNoParens = StripParameters(target);
        var (lastSegment, secondLastSegment) = LastTwoSegments(nameNoParens);

        var matches = new List<ISymbol>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var project in solution.Projects)
        {
            var compilation = await project.GetCompilationAsync(ct);
            if (compilation is null) continue;

            var raw = compilation.GetSymbolsWithName(
                name => name == lastSegment ||
                        (secondLastSegment is not null && name == secondLastSegment),
                SymbolFilter.All, ct);

            foreach (var candidate in raw.SelectMany(ExpandWithConstructors))
            {
                if (!AcceptableFullNames(candidate).Contains(target))
                    continue;

                // Default display string includes the parameter signature, so overloads stay
                // distinct while the same symbol seen across projects is merged.
                if (seen.Add(candidate.ToDisplayString()))
                    matches.Add(candidate);
            }
        }

        if (matches.Count == 0)
            throw new ArgumentException(
                $"Symbol '{fullName}' not found in any project in the solution.\n" +
                "Tip: use the fully qualified name, e.g. 'MyApp.Services.OrderService.ProcessOrder' " +
                "(constructors: 'MyApp.Services.OrderService.OrderService').",
                nameof(fullName));

        matches.Sort((a, b) => string.CompareOrdinal(a.ToDisplayString(), b.ToDisplayString()));
        return matches;
    }

    /// <summary>
    /// Resolves the target symbols for a command: every match of a fully-qualified name, or the
    /// single symbol at a source location. Read-only commands map each target to its own result group.
    /// </summary>
    public static async Task<IReadOnlyList<ISymbol>> ResolveTargetsAsync(
        Solution solution,
        string? symbol,
        string? file,
        int? line,
        int? col,
        CancellationToken ct = default)
        => symbol is not null
            ? await FromFullNameAllAsync(solution, symbol, ct)
            : [await FromLocationAsync(solution, file!, line!.Value, col!.Value, ct)];

    /// <summary>
    /// What a <c>--symbol</c> fully-qualified name addresses for container-oriented commands
    /// (<c>outline</c>/<c>describe</c>): one or more named types, a member (method/property/field/event),
    /// or a namespace. See plan decision D8.
    /// </summary>
    public enum ContainerTargetKind { Types, Member, Namespace }

    /// <summary>Result of <see cref="ResolveContainerTargetAsync"/>.</summary>
    public sealed record ContainerTarget(
        ContainerTargetKind Kind,
        IReadOnlyList<INamedTypeSymbol> Types,
        IReadOnlyList<ISymbol> Members);

    /// <summary>
    /// Returns the top-level type/enum/delegate declarations in a source file as
    /// <see cref="INamedTypeSymbol"/>s in source order — the containers <c>outline --file</c> enumerates
    /// (see D7). A file with no declarations (only <c>using</c>s) returns an empty list, not an error.
    /// A top-level-statements file resolves the synthesized entry-point type.
    /// </summary>
    public static async Task<IReadOnlyList<INamedTypeSymbol>> ContainersInFileAsync(
        Solution solution,
        string filePath,
        CancellationToken ct = default)
    {
        var normalizedPath = Path.GetFullPath(filePath);
        var docId = solution.GetDocumentIdsWithFilePath(filePath).FirstOrDefault()
            ?? solution.GetDocumentIdsWithFilePath(normalizedPath).FirstOrDefault()
            ?? throw new ArgumentException(
                $"File not found in solution: {filePath}\n" +
                "Tip: make sure the path is absolute or relative to the solution directory.",
                nameof(filePath));

        var document = solution.GetDocument(docId)!;
        var root = await document.GetSyntaxRootAsync(ct);
        var model = await document.GetSemanticModelAsync(ct);
        if (root is null || model is null)
            return [];

        var results = new List<INamedTypeSymbol>();
        var seen = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);

        foreach (var node in root.DescendantNodes())
        {
            // Top-level only — nested types are surfaced by the caller's recursive member walk.
            if (node is not (BaseTypeDeclarationSyntax or DelegateDeclarationSyntax))
                continue;
            if (node.Parent is not (BaseNamespaceDeclarationSyntax or CompilationUnitSyntax))
                continue;

            if (model.GetDeclaredSymbol(node, ct) is INamedTypeSymbol type && seen.Add(type))
                results.Add(type);
        }

        // Top-level statements declare no type syntax; resolve the synthesized entry-point container.
        if (root is CompilationUnitSyntax cu && cu.Members.OfType<GlobalStatementSyntax>().Any())
        {
            var entry = model.Compilation.GetEntryPoint(ct);
            if (entry?.ContainingType is { } program && seen.Add(program))
                results.Add(program);
        }

        return results;
    }

    /// <summary>
    /// Classifies what a <c>--symbol</c> fully-qualified name addresses. When a name matches both a type
    /// and a same-named namespace, the type is preferred (D8). Throws <see cref="ArgumentException"/>
    /// when nothing matches.
    /// </summary>
    public static async Task<ContainerTarget> ResolveContainerTargetAsync(
        Solution solution,
        string fullName,
        CancellationToken ct = default)
    {
        IReadOnlyList<ISymbol> matches;
        try
        {
            matches = await FromFullNameAllAsync(solution, fullName, ct);
        }
        catch (ArgumentException)
        {
            matches = [];
        }

        var types = matches.OfType<INamedTypeSymbol>().ToList();
        if (types.Count > 0)
            return new ContainerTarget(ContainerTargetKind.Types, types, []);

        var members = matches.Where(IsAddressableMember).ToList();
        if (members.Count > 0)
            return new ContainerTarget(ContainerTargetKind.Member, [], members);

        var ns = matches.OfType<INamespaceSymbol>().FirstOrDefault()
                 ?? await FindNamespaceAsync(solution, fullName, ct);
        if (ns is not null)
            return new ContainerTarget(ContainerTargetKind.Namespace, [], [ns]);

        throw new ArgumentException(
            $"Symbol '{fullName}' not found in any project in the solution.\n" +
            "Tip: use the fully qualified type name, e.g. 'MyApp.Services.OrderService'.",
            nameof(fullName));
    }

    /// <summary>Walks the global namespace by dotted segments to find a namespace by full name.</summary>
    public static async Task<INamespaceSymbol?> FindNamespaceAsync(
        Solution solution,
        string fullName,
        CancellationToken ct = default)
    {
        var segments = fullName.Trim().Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
            return null;

        foreach (var project in solution.Projects)
        {
            var compilation = await project.GetCompilationAsync(ct);
            if (compilation is null) continue;

            INamespaceSymbol? current = compilation.GlobalNamespace;
            foreach (var segment in segments)
            {
                current = current?.GetNamespaceMembers().FirstOrDefault(n => n.Name == segment);
                if (current is null) break;
            }

            if (current is not null)
                return current;
        }

        return null;
    }

    private static bool IsAddressableMember(ISymbol symbol)
        => symbol.Kind is SymbolKind.Method or SymbolKind.Property or SymbolKind.Field or SymbolKind.Event;

    private static IEnumerable<ISymbol> ExpandWithConstructors(ISymbol symbol)
    {
        yield return symbol;

        if (symbol is not INamedTypeSymbol type)
            yield break;

        // Keep the genuine compiler-generated default constructor (so a class with no explicit ctor
        // is still addressable by name), but drop implicitly-declared constructors when the type also
        // has an explicit one — those are spurious and would surface an uncallable phantom group.
        var hasExplicitConstructor = type.InstanceConstructors.Any(c => !c.IsImplicitlyDeclared);
        foreach (var constructor in type.Constructors)
            if (!constructor.IsImplicitlyDeclared || !hasExplicitConstructor)
                yield return constructor;
    }

    // The forms a caller may legitimately type for a symbol. Includes the canonical display string
    // (identical to what `symbols` emits), the parameterless containing-type path, and constructor aliases.
    private static IEnumerable<string> AcceptableFullNames(ISymbol s)
    {
        // Canonical form — round-trips the fullName printed by the `symbols` command.
        yield return s.ToDisplayString();

        if (s is IMethodSymbol method &&
            method.MethodKind is MethodKind.Constructor or MethodKind.StaticConstructor)
        {
            var typeFqn = TypeFqn(s.ContainingType);
            yield return typeFqn + "." + s.ContainingType.Name; // Ns.Type.Type
            yield return typeFqn + ".#ctor";
            yield return typeFqn + "..ctor";
        }
        else if (s.ContainingType is not null &&
                 s.Kind is SymbolKind.Method or SymbolKind.Property or SymbolKind.Field or SymbolKind.Event)
        {
            yield return TypeFqn(s.ContainingType) + "." + s.Name;
        }
        else
        {
            yield return s.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", "");
        }
    }

    private static string TypeFqn(INamedTypeSymbol type)
        => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", "");

    private static string StripParameters(string fullName)
    {
        var paren = fullName.IndexOf('(');
        return paren < 0 ? fullName : fullName[..paren].Trim();
    }

    private static (string Last, string? SecondLast) LastTwoSegments(string dottedName)
    {
        var lastDot = dottedName.LastIndexOf('.');
        if (lastDot < 0)
            return (dottedName, null);

        var last = dottedName[(lastDot + 1)..];
        var rest = dottedName[..lastDot];
        var prevDot = rest.LastIndexOf('.');
        var secondLast = prevDot < 0 ? rest : rest[(prevDot + 1)..];
        return (last, secondLast);
    }

    /// <summary>
    /// Search symbols by pattern (supports * and ? wildcards).
    /// </summary>
    public static async IAsyncEnumerable<ISymbol> SearchAsync(
        Solution solution,
        string pattern,
        SymbolFilter filter = SymbolFilter.All,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var seen = new HashSet<string>();

        foreach (var project in solution.Projects)
        {
            var compilation = await project.GetCompilationAsync(ct);
            if (compilation is null) continue;

            var symbols = compilation.GetSymbolsWithName(
                name => MatchesPattern(name, pattern), filter, ct);

            foreach (var symbol in symbols)
            {
                var key = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                if (seen.Add(key))
                    yield return symbol;
            }
        }
    }

    private static bool MatchesPattern(string name, string pattern)
    {
        if (!pattern.Contains('*') && !pattern.Contains('?'))
            return name.Contains(pattern, StringComparison.OrdinalIgnoreCase);

        // Simple glob matching
        return GlobMatch(pattern.ToLowerInvariant(), name.ToLowerInvariant());
    }

    private static bool GlobMatch(string pattern, string input)
    {
        int pi = 0, si = 0, starPi = -1, starSi = 0;
        while (si < input.Length)
        {
            if (pi < pattern.Length && (pattern[pi] == '?' || pattern[pi] == input[si]))
            { pi++; si++; }
            else if (pi < pattern.Length && pattern[pi] == '*')
            { starPi = pi++; starSi = si; }
            else if (starPi >= 0)
            { pi = starPi + 1; si = ++starSi; }
            else return false;
        }
        while (pi < pattern.Length && pattern[pi] == '*') pi++;
        return pi == pattern.Length;
    }
}
