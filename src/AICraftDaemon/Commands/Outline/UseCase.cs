using DotnetAICraft.Models;
using DotnetAICraft.Roslyn;
using Microsoft.CodeAnalysis;

namespace DotnetAICraft.Commands.Outline;

internal static class UseCase
{
    internal static async Task<IReadOnlyList<SymbolMatchGroup<OutlineResult>>> ResolveAsync(
        Solution solution,
        string? symbol,
        string? file,
        bool publicOnly,
        bool includeInherited,
        CancellationToken ct = default)
    {
        Validation.ValidateDaemonArgs(symbol, file, line: null, col: null);

        IReadOnlyList<INamedTypeSymbol> containers;

        if (!string.IsNullOrWhiteSpace(symbol))
        {
            var target = await SymbolResolver.ResolveContainerTargetAsync(solution, symbol.Trim(), ct);
            containers = target.Kind switch
            {
                SymbolResolver.ContainerTargetKind.Types => target.Types,
                // R10/AE2: a member is not a container — redirect to describe.
                SymbolResolver.ContainerTargetKind.Member => throw new ArgumentException(
                    $"'{symbol.Trim()}' is a member, not a type. Use 'describe --symbol {symbol.Trim()}' for its signature."),
                // D8: a namespace is not a container — redirect to symbols.
                _ => throw new ArgumentException(
                    $"'{symbol.Trim()}' is a namespace, not a type. " +
                    $"Use 'symbols --pattern {symbol.Trim()}.*' to list the types it contains.")
            };
        }
        else
        {
            containers = await SymbolResolver.ContainersInFileAsync(solution, file!, ct);
        }

        var solutionDir = Path.GetDirectoryName(solution.FilePath) ?? string.Empty;
        return containers
            .Select(c => new SymbolMatchGroup<OutlineResult>(
                c.ToDisplayString(),
                c.GetKindName(),
                OutputMapping.Map(c, solutionDir, publicOnly, includeInherited)))
            .ToList();
    }
}
