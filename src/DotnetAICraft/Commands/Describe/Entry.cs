using System.Text.Json;
using DotnetAICraft.Commands.Shared;
using DotnetAICraft.Models;
using DotnetAICraft.Output;

namespace DotnetAICraft.Commands.Describe;

internal static class Entry
{
    private const string CommandName = "describe";

    internal static async Task ExecuteAsync(
        string solutionPath,
        FileInfo? file,
        int? line,
        int? col,
        string? symbol,
        string? idleTimeout,
        OutputFormat format = OutputFormat.Text)
    {
        CliValidation.ValidateCliArgs(file, line, col, symbol);

        var @params = !string.IsNullOrWhiteSpace(symbol)
            ? (object)new { symbol = symbol.Trim() }
            : new { file = file!.FullName, line = line!.Value, col = col!.Value };

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
            for (var i = 0; i < groups.Count; i++)
            {
                TextOutput.WriteMatchHeader(groups[i].Symbol, groups[i].Kind);
                var card = JsonOutput.Deserialize<DescribeCard>((JsonElement)groups[i].Result);
                if (card is not null)
                    TextOutput.WriteDescribe(card, solutionPath);
                if (i < groups.Count - 1)
                    Console.Out.WriteLine();
            }
        }
    }
}
