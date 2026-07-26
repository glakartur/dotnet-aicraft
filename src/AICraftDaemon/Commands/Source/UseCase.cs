using DotnetAICraft.Models;
using DotnetAICraft.Roslyn;
using Microsoft.CodeAnalysis;

namespace DotnetAICraft.Commands.Source;

internal static class UseCase
{
    internal static async Task<IReadOnlyList<SymbolMatchGroup>> ResolveAsync(
        Solution solution,
        string? symbol,
        string? file,
        int? line,
        int? col,
        CancellationToken ct = default)
    {
        Validation.ValidateDaemonArgs(symbol, file, line, col);

        var targets = await SymbolResolver.ResolveTargetsAsync(solution, symbol, file, line, col, ct);
        var solutionDir = Path.GetDirectoryName(solution.FilePath) ?? string.Empty;

        return targets
            .Select(s => new SymbolMatchGroup(
                s.ToDisplayString(),
                s.GetKindName(),
                OutputMapping.Map(s, solution, solutionDir, ct)))
            .ToList();
    }
}
