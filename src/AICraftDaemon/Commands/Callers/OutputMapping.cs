using DotnetAICraft.Models;
using Microsoft.CodeAnalysis;

namespace DotnetAICraft.Commands.Callers;

internal static class OutputMapping
{
    internal static Task<CallGraphResult> MapAsync(
        Solution solution,
        ISymbol symbol,
        string normalizedDirection,
        int normalizedDepth,
        CancellationToken ct)
        => Daemon.DaemonServer.CollectCallGraphAsync(solution, symbol, normalizedDirection, normalizedDepth, ct);
}
