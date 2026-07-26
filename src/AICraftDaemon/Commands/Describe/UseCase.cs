using DotnetAICraft.Models;
using DotnetAICraft.Roslyn;
using Microsoft.CodeAnalysis;

namespace DotnetAICraft.Commands.Describe;

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

        // D8: a namespace is not describable. When a type and same-named namespace both match, the
        // type wins (drop namespaces); a namespace-only result redirects to `symbols`.
        var describable = targets.Where(t => t is not INamespaceSymbol).ToList();
        // Everything was filtered out, so any matches that existed were namespaces.
        if (describable.Count == 0 && targets.Count > 0)
        {
            var name = targets[0].ToDisplayString();
            throw new ArgumentException(
                $"'{name}' is a namespace, not a type or member. " +
                $"Use 'symbols --pattern {name}.*' to list the types it contains.");
        }

        var solutionDir = Path.GetDirectoryName(solution.FilePath) ?? string.Empty;
        return describable
            .Select(s => new SymbolMatchGroup(
                s.ToDisplayString(),
                s.GetKindName(),
                OutputMapping.Map(s, solutionDir, ct)))
            .ToList();
    }
}
