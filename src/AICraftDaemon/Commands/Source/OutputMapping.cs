using DotnetAICraft.Models;
using DotnetAICraft.Roslyn;
using Microsoft.CodeAnalysis;

namespace DotnetAICraft.Commands.Source;

internal static class OutputMapping
{
    internal static SourceResult Map(ISymbol symbol, Solution solution, string solutionDir, CancellationToken ct = default)
    {
        var blocks = new List<SourceBlock>();

        foreach (var syntaxReference in symbol.DeclaringSyntaxReferences)
        {
            ct.ThrowIfCancellationRequested();

            var syntax = syntaxReference.GetSyntax(ct);
            // Span from the start of the leading trivia (XML-doc + attributes captured by ToFullString)
            // to the end of the node itself — excluding trailing trivia, which would push EndLine past
            // the closing brace onto the next line.
            var span = Microsoft.CodeAnalysis.Text.TextSpan.FromBounds(syntax.FullSpan.Start, syntax.Span.End);
            var lineSpan = syntax.SyntaxTree.GetLineSpan(span, ct);
            if (!lineSpan.IsValid)
                continue;

            // ToFullString captures leading trivia — XML-doc + attributes + signature + body verbatim.
            var path = PathFormatter.ToRelative(lineSpan.Path, solutionDir) ?? lineSpan.Path;
            blocks.Add(new SourceBlock(
                File: path,
                StartLine: lineSpan.StartLinePosition.Line + 1,
                EndLine: lineSpan.EndLinePosition.Line + 1,
                Text: syntax.ToFullString()));
        }

        var fullName = symbol.ToDisplayString();
        var kind = symbol.GetKindName();

        if (blocks.Count > 0)
            return new SourceResult(fullName, kind, HasSource: true, blocks, Assembly: null, Note: null);

        // No in-source declaring syntax. Distinguish a compiler-generated member of a source type
        // (e.g. a record's synthesized members or an implicit constructor) from a metadata-only symbol.
        var assembly = symbol.ContainingAssembly?.Name;
        var containerLocation = symbol.ContainingType?.Locations.FirstOrDefault(l => l.IsInSource);
        if (symbol.IsImplicitlyDeclared && containerLocation is not null)
        {
            var (file, line, _) = containerLocation.GetFileLineColRelative(solutionDir);
            var note = $"compiler-generated; declared by {symbol.ContainingType!.ToDisplayString()} at {file}:{line}";
            return new SourceResult(fullName, kind, HasSource: false, [], assembly, note);
        }

        return new SourceResult(
            fullName, kind, HasSource: false, [], assembly,
            Note: $"no source available — declared in metadata{(assembly is null ? "" : $" ({assembly})")}");
    }
}
