using DotnetAICraft.Models;
using DotnetAICraft.Roslyn;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;

namespace DotnetAICraft.Commands.Refs;

internal static class UseCase
{
    internal static async Task<IReadOnlyList<SymbolMatchGroup<IReadOnlyList<ReferenceResult>>>> ResolveAsync(
        Solution solution,
        string? symbol,
        string? file,
        int? line,
        int? col,
        CancellationToken ct = default)
    {
        var targets = await SymbolResolver.ResolveTargetsAsync(solution, symbol, file, line, col, ct);
        var solutionDir = Path.GetDirectoryName(solution.FilePath) ?? string.Empty;
        var groups = new List<SymbolMatchGroup<IReadOnlyList<ReferenceResult>>>();

        foreach (var sym in targets)
        {
            var refs = await SymbolFinder.FindReferencesAsync(sym, solution, ct);
            var items = refs
                .SelectMany(reference => reference.Locations)
                .Select(loc => OutputMapping.Map(loc, solutionDir))
                .ToList();
            groups.Add(new SymbolMatchGroup<IReadOnlyList<ReferenceResult>>(sym.ToDisplayString(), sym.GetKindName(), items));
        }

        return groups;
    }
}
