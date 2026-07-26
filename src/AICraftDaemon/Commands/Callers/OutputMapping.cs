using DotnetAICraft.Daemon;
using Microsoft.CodeAnalysis;

namespace DotnetAICraft.Commands.Callers;

internal static class OutputMapping
{
    internal static async Task<object> MapAsync(
        Solution solution,
        ISymbol symbol,
        string normalizedDirection,
        int normalizedDepth,
        CancellationToken ct)
        => await DaemonServer.CollectCallGraphAsync(solution, symbol, normalizedDirection, normalizedDepth, ct);
}
