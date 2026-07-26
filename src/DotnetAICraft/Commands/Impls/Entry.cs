using DotnetAICraft.Commands.Shared;
using DotnetAICraft.Models;
using DotnetAICraft.Output;

namespace DotnetAICraft.Commands.Impls;

internal static class Entry
{
    private const string CommandName = "impls";

    internal static async Task ExecuteAsync(
        string solutionPath,
        string symbol,
        string? idleTimeout,
        OutputFormat format = OutputFormat.Text)
    {
        var res = await CommandHelpers.SendWithRetryOrWriteErrorAsync<IReadOnlyList<SymbolMatchGroup<IReadOnlyList<SymbolResult>>>>(
            solutionPath, CommandName, new { symbol }, idleTimeout, format: format);
        if (res is null)
            return;

        if (CommandHelpers.TryHandleError(res, format))
            return;

        var solutionDir = Path.GetDirectoryName(solutionPath) ?? string.Empty;
        if (format == OutputFormat.Json)
        {
            JsonOutput.WriteWithSolutionRoot(solutionDir, res.Result);
        }
        else
        {
            TextOutput.WriteSolutionRootHeader(solutionDir);
            var groups = res.Result ?? Array.Empty<SymbolMatchGroup<IReadOnlyList<SymbolResult>>>();
            for (var i = 0; i < groups.Count; i++)
            {
                TextOutput.WriteMatchHeader(groups[i].Symbol, groups[i].Kind);
                TextOutput.WriteImpls(groups[i].Result, symbol, solutionPath);
                if (i < groups.Count - 1)
                    Console.Out.WriteLine();
            }
        }
    }
}
