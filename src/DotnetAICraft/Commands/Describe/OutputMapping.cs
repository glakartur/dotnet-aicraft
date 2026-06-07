using DotnetAICraft.Models;
using DotnetAICraft.Roslyn;
using Microsoft.CodeAnalysis;

namespace DotnetAICraft.Commands.Describe;

internal static class OutputMapping
{
    internal static DescribeCard Map(ISymbol symbol, string solutionDir, CancellationToken ct = default)
    {
        var sourceLocation = symbol.Locations.FirstOrDefault(location => location.IsInSource);

        string? file = null;
        int? line = null;
        int? col = null;
        string? assembly = null;

        if (sourceLocation is not null)
        {
            var (relFile, relLine, relCol) = sourceLocation.GetFileLineColRelative(solutionDir);
            file = relFile;
            line = relLine;
            col = relCol;
        }
        else
        {
            // Metadata symbol (D6): null coordinates, name the declaring assembly.
            assembly = symbol.ContainingAssembly?.Name;
        }

        return new DescribeCard(
            FullName: symbol.ToDisplayString(),
            Kind: symbol.GetKindName(),
            File: file,
            Line: line,
            Col: col,
            ContainingType: symbol.ContainingType?.ToDisplayString(),
            ContainingNamespace: NamespaceOrNull(symbol),
            Signature: Signature(symbol),
            ReturnType: ReturnType(symbol),
            Parameters: Parameters(symbol),
            Modifiers: Modifiers(symbol),
            Attributes: Attributes(symbol),
            ConstantValue: ConstantValue(symbol),
            Documentation: XmlDocCleaner.Clean(symbol.GetDocumentationCommentXml(cancellationToken: ct)),
            Siblings: SiblingOverloads(symbol),
            Assembly: assembly);
    }

    private static string? NamespaceOrNull(ISymbol symbol)
    {
        var ns = symbol.ContainingNamespace;
        return ns is null || ns.IsGlobalNamespace ? null : ns.ToDisplayString();
    }

    private static string Signature(ISymbol symbol) => symbol switch
    {
        INamedTypeSymbol { TypeKind: TypeKind.Delegate } d => SymbolDisplayFormats.FormatDelegateSignature(d),
        INamedTypeSymbol type => SymbolDisplayFormats.FormatTypeHeader(type),
        _ => SymbolDisplayFormats.FormatMemberSignature(symbol)
    };

    private static string? ReturnType(ISymbol symbol) => symbol switch
    {
        IMethodSymbol { MethodKind: MethodKind.Constructor or MethodKind.StaticConstructor } => null,
        IMethodSymbol method => method.ReturnType.ToDisplayString(),
        IPropertySymbol property => property.Type.ToDisplayString(),
        IFieldSymbol field => field.Type.ToDisplayString(),
        _ => null
    };

    private static IReadOnlyList<DescribeParameter>? Parameters(ISymbol symbol)
    {
        var parameters = symbol switch
        {
            IMethodSymbol method => method.Parameters,
            IPropertySymbol { IsIndexer: true } indexer => indexer.Parameters,
            INamedTypeSymbol { TypeKind: TypeKind.Delegate, DelegateInvokeMethod: { } invoke } => invoke.Parameters,
            _ => default
        };

        if (parameters.IsDefaultOrEmpty)
            return null;

        return parameters
            .Select(p => new DescribeParameter(
                p.Name,
                p.Type.ToDisplayString(),
                p.HasExplicitDefaultValue ? FormatConstant(p.ExplicitDefaultValue) : null))
            .ToList();
    }

    private static IReadOnlyList<string>? Modifiers(ISymbol symbol)
    {
        var modifiers = new List<string>();

        if (symbol.IsStatic && symbol is not INamedTypeSymbol)
            modifiers.Add("static");
        if (symbol.IsAbstract && symbol is not INamedTypeSymbol)
            modifiers.Add("abstract");
        if (symbol.IsVirtual)
            modifiers.Add("virtual");
        if (symbol.IsOverride)
            modifiers.Add("override");
        if (symbol.IsSealed && symbol is not INamedTypeSymbol)
            modifiers.Add("sealed");
        if (symbol is IMethodSymbol { IsAsync: true })
            modifiers.Add("async");
        if (symbol is IMethodSymbol { IsExtern: true })
            modifiers.Add("extern");
        if (symbol is IFieldSymbol { IsConst: true })
            modifiers.Add("const");
        if (symbol is IFieldSymbol { IsReadOnly: true })
            modifiers.Add("readonly");
        if (symbol is IFieldSymbol { IsVolatile: true })
            modifiers.Add("volatile");

        return modifiers.Count == 0 ? null : modifiers;
    }

    private static IReadOnlyList<string>? Attributes(ISymbol symbol)
    {
        var names = symbol.GetAttributes()
            .Select(a => a.AttributeClass?.Name)
            .Where(n => !string.IsNullOrEmpty(n))
            .Select(n => n!.EndsWith("Attribute", StringComparison.Ordinal) ? n[..^"Attribute".Length] : n)
            .ToList();

        return names.Count == 0 ? null : names;
    }

    private static string? ConstantValue(ISymbol symbol) => symbol switch
    {
        IFieldSymbol { HasConstantValue: true } field => FormatConstant(field.ConstantValue),
        _ => null
    };

    private static string FormatConstant(object? value) => value switch
    {
        null => "null",
        string s => $"\"{s}\"",
        bool b => b ? "true" : "false",
        char c => $"'{c}'",
        _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? value.ToString() ?? string.Empty
    };

    // D3: list the other overloads' signatures, never the target itself.
    private static IReadOnlyList<string>? SiblingOverloads(ISymbol symbol)
    {
        if (symbol is not IMethodSymbol method ||
            method.MethodKind is not (MethodKind.Ordinary or MethodKind.Constructor))
            return null;

        var siblings = method.ContainingType?
            .GetMembers(method.Name)
            .OfType<IMethodSymbol>()
            .Where(m => m.MethodKind == method.MethodKind
                        && !m.IsImplicitlyDeclared
                        && !SymbolEqualityComparer.Default.Equals(m, method))
            .Select(m => SymbolDisplayFormats.FormatMemberSignature(m))
            .ToList();

        return siblings is null || siblings.Count == 0 ? null : siblings;
    }
}
