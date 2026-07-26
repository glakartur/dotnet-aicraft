using DotnetAICraft.Commands.Shared;
using DotnetAICraft.Models;
using DotnetAICraft.Output;

namespace DotnetAICraft.Commands.Refs;

internal static class Entry
{
    private const string CommandName = "refs";

    internal static async Task ExecuteAsync(
        string solutionPath,
        FileInfo? file,
        int? line,
        int? col,
        string? symbol,
        string? idleTimeout,
        OutputFormat format = OutputFormat.Text)
    {
        Validation.ValidateCliArgs(file, line, col, symbol);

        var @params = symbol is not null
            ? (object)new { symbol }
            : new { file = file!.FullName, line = line!.Value, col = col!.Value };

        var res = await CommandHelpers.SendWithRetryOrWriteErrorAsync<IReadOnlyList<SymbolMatchGroup<IReadOnlyList<ReferenceResult>>>>(
            solutionPath, CommandName, @params, idleTimeout, format: format);
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
            var target = symbol ?? $"{file!.FullName}:{line}:{col}";
            var groups = res.Result ?? Array.Empty<SymbolMatchGroup<IReadOnlyList<ReferenceResult>>>();
            for (var i = 0; i < groups.Count; i++)
            {
                TextOutput.WriteMatchHeader(groups[i].Symbol, groups[i].Kind);
                TextOutput.WriteRefs(groups[i].Result, target, solutionPath);
                if (i < groups.Count - 1)
                    Console.Out.WriteLine();
            }
        }
    }
}
