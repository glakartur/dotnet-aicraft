using System.Text.Json;
using DotnetAICraft.Commands.Shared;
using DotnetAICraft.Models;
using DotnetAICraft.Output;

namespace DotnetAICraft.Commands.Outline;

internal static class Entry
{
    private const string CommandName = "outline";

    internal static async Task ExecuteAsync(
        string solutionPath,
        FileInfo? file,
        string? symbol,
        bool publicOnly,
        bool includeInherited,
        string? idleTimeout,
        OutputFormat format = OutputFormat.Text)
    {
        CliValidation.ValidateCliArgs(file, line: null, col: null, symbol);

        var @params = !string.IsNullOrWhiteSpace(symbol)
            ? (object)new { symbol = symbol.Trim(), publicOnly, includeInherited }
            : new { file = file!.FullName, publicOnly, includeInherited };

        var res = await CommandHelpers.SendWithRetryOrWriteErrorAsync(
            solutionPath, CommandName, @params, idleTimeout, format: format);
        if (res is null)
            return;

        if (CommandHelpers.TryHandleError(res, format))
            return;

        var solutionDir = Path.GetDirectoryName(solutionPath) ?? string.Empty;
        if (format == OutputFormat.Json)
        {
            JsonOutput.WriteWithSolutionRoot(solutionDir, CommandHelpers.GetDataOrNull(res));
        }
        else
        {
            TextOutput.WriteSolutionRootHeader(solutionDir);
            var groups = JsonOutput.Deserialize<IReadOnlyList<SymbolMatchGroup>>((JsonElement)res.Result!) ?? Array.Empty<SymbolMatchGroup>();
            if (groups.Count == 0)
            {
                TextOutput.WriteOutlineEmpty();
                return;
            }
            for (var i = 0; i < groups.Count; i++)
            {
                TextOutput.WriteMatchHeader(groups[i].Symbol, groups[i].Kind);
                var result = JsonOutput.Deserialize<OutlineResult>((JsonElement)groups[i].Result);
                if (result is not null)
                    TextOutput.WriteOutline(result, solutionPath);
                if (i < groups.Count - 1)
                    Console.Out.WriteLine();
            }
        }
    }
}
