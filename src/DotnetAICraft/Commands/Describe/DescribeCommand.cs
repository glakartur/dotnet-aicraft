using System.CommandLine;
using DotnetAICraft.Commands.Describe;
using DotnetAICraft.Commands.Shared;
using DotnetAICraft.Output;

namespace DotnetAICraft.Commands;

public static class DescribeCommand
{
    public static Command Build(
        Option<FileInfo> solutionOption,
        Option<FileInfo> projectOption,
        Option<string?> idleTimeoutOption,
        Option<bool>? debugOption = null,
        Option<OutputFormat>? formatOption = null)
    {
        var fileOpt = new Option<FileInfo?>("--file")
        {
            Description = "Source file containing the symbol usage/declaration"
        };

        var lineOpt = new Option<int?>("--line")
        {
            Description = "1-based line number"
        };

        var colOpt = new Option<int?>("--col")
        {
            Description = "1-based column number"
        };

        var symbolOpt = new Option<string?>("--symbol")
        {
            Description = "Fully-qualified symbol name (alternative to --file/--line/--col)"
        };

        var cmd = new Command("describe",
            "Show a semantic card for a symbol: signature, types, modifiers, attributes, doc, and sibling overloads")
        {
            solutionOption,
            projectOption,
            fileOpt,
            lineOpt,
            colOpt,
            symbolOpt,
            idleTimeoutOption
        };

        if (debugOption is not null)
            cmd.Add(debugOption);
        if (formatOption is not null)
            cmd.Add(formatOption);

        cmd.SetAction(async parseResult =>
        {
            var solution = parseResult.GetValue(solutionOption);
            var project = parseResult.GetValue(projectOption);
            var file = parseResult.GetValue(fileOpt);
            var line = parseResult.GetValue(lineOpt);
            var col = parseResult.GetValue(colOpt);
            var symbol = parseResult.GetValue(symbolOpt);
            var idleTimeout = parseResult.GetValue(idleTimeoutOption);
            var format = formatOption is null ? OutputFormat.Text : parseResult.GetValue(formatOption);

            var solutionPath = SolutionPathResolver.Resolve(solution, project, format);
            if (solutionPath is null) return;

            await Entry.ExecuteAsync(solutionPath, file, line, col, symbol, idleTimeout, format);
        });

        return cmd;
    }
}
