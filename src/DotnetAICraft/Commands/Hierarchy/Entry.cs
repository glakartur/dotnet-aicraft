using System.Text.Json;
using DotnetAICraft.Commands.Shared;
using DotnetAICraft.Models;
using DotnetAICraft.Output;

namespace DotnetAICraft.Commands.Hierarchy;

internal static class Entry
{
    private const string CommandName = "hierarchy";

    internal static async Task ExecuteAsync(
        string solutionPath,
        FileInfo? file,
        int? line,
        int? col,
        string? symbol,
        string direction,
        bool includeFramework,
        int? maxDepth,
        string? idleTimeout,
        OutputFormat format = OutputFormat.Text)
    {
        CliValidation.ValidateCliModeArgs(file, line, col, symbol);

        if (!CliValidation.TryParseDirection(direction, out var normalizedDirection, out var directionError))
        {
            CommandHelpers.WriteError(format, directionError!.Code, directionError.Message, directionError.Details);
            return;
        }

        if (!CliValidation.TryNormalizeMaxDepth(maxDepth, out var normalizedMaxDepth, out var maxDepthError))
        {
            CommandHelpers.WriteError(format, maxDepthError!.Code, maxDepthError.Message, maxDepthError.Details);
            return;
        }

        var @params = !string.IsNullOrWhiteSpace(symbol)
            ? (object)new { symbol = symbol.Trim(), direction = normalizedDirection, includeFramework, maxDepth = normalizedMaxDepth }
            : new { file = file!.FullName, line = line!.Value, col = col!.Value, direction = normalizedDirection, includeFramework, maxDepth = normalizedMaxDepth };

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
                var root = JsonOutput.Deserialize<HierarchyNode>((JsonElement)groups[i].Result);
                if (root is not null)
                    TextOutput.WriteHierarchy(root, normalizedDirection, solutionPath);
                if (i < groups.Count - 1)
                    Console.Out.WriteLine();
            }
        }
    }
}
