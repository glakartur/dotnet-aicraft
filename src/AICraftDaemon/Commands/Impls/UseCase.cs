using DotnetAICraft.Models;
using DotnetAICraft.Roslyn;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;

namespace DotnetAICraft.Commands.Impls;

internal static class UseCase
{
    internal static async Task<IReadOnlyList<SymbolMatchGroup>> ResolveAsync(
        Solution solution,
        string symbol,
        CancellationToken ct = default)
    {
        Validation.ValidateDaemonArgs(symbol);

        var targets = await SymbolResolver.FromFullNameAllAsync(solution, symbol, ct);
        var solutionDir = Path.GetDirectoryName(solution.FilePath) ?? string.Empty;
        var groups = new List<SymbolMatchGroup>();

        foreach (var sym in targets)
        {
            var impls = sym is INamedTypeSymbol namedType
                ? await SymbolFinder.FindImplementationsAsync(namedType, solution, transitive: false, projects: null, ct)
                : await SymbolFinder.FindImplementationsAsync(sym, solution, projects: null, ct);

            var items = impls.Select(impl => OutputMapping.Map(impl, solutionDir)).ToList();
            groups.Add(new SymbolMatchGroup(sym.ToDisplayString(), sym.GetKindName(), items));
        }

        return groups;
    }
}
