using DotnetAICraft.Daemon;
using DotnetAICraft.Models;
using DotnetAICraft.Roslyn;
using Microsoft.CodeAnalysis;

namespace DotnetAICraft.Commands.Hierarchy;

internal static class UseCase
{
    internal static async Task<IReadOnlyList<SymbolMatchGroup<HierarchyNode>>> ResolveAsync(
        Solution solution,
        string? symbol,
        string? file,
        int? line,
        int? col,
        string? direction,
        bool includeFramework,
        int? maxDepth,
        CancellationToken ct = default)
    {
        Validation.ValidateDaemonModeArgs(symbol, file, line, col);

        if (!CliValidation.TryParseDirection(direction, out var normalizedDirection, out var directionError))
            throw new DaemonValidationException(directionError!);

        if (!CliValidation.TryNormalizeMaxDepth(maxDepth, out var normalizedMaxDepth, out var maxDepthError))
            throw new DaemonValidationException(maxDepthError!);

        var symbolArg = string.IsNullOrWhiteSpace(symbol) ? null : symbol.Trim();
        var targets = await SymbolResolver.ResolveTargetsAsync(solution, symbolArg, file, line, col, ct);
        var solutionDir = Path.GetDirectoryName(solution.FilePath) ?? string.Empty;

        var groups = new List<SymbolMatchGroup<HierarchyNode>>();
        foreach (var sym in targets)
        {
            var named = HierarchyTargetValidation.EnsureTargetKind(sym);
            var root = await OutputMapping.BuildNodeAsync(
                solution, named, normalizedDirection, includeFramework, normalizedMaxDepth, depth: 0, solutionDir, ct);
            groups.Add(new SymbolMatchGroup<HierarchyNode>(named.ToDisplayString(), named.GetKindName(), root));
        }

        return groups;
    }
}
