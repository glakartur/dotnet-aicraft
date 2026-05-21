using System.CommandLine;
using DotnetAICraft.Commands.Impls;
using DotnetAICraft.Commands.Shared;
using DotnetAICraft.Output;

namespace DotnetAICraft.Commands;

public static class ImplsCommand
{
    public static Command Build(
        Option<FileInfo> solutionOption,
        Option<FileInfo> projectOption,
        Option<string?> idleTimeoutOption,
        Option<bool>? debugOption = null,
        Option<OutputFormat>? formatOption = null)
    {
        var symbolOpt = new Option<string>("--symbol")
        {
            Description = "Fully-qualified interface or abstract member name",
            Required = true
        };

        var cmd = new Command("impls",
            "Find all implementations of an interface or abstract member")
        {
            solutionOption, projectOption, symbolOpt, idleTimeoutOption
        };

        if (debugOption is not null)
            cmd.Add(debugOption);
        if (formatOption is not null)
            cmd.Add(formatOption);

        cmd.SetAction(async parseResult =>
        {
            var solution = parseResult.GetValue(solutionOption);
            var project = parseResult.GetValue(projectOption);
            var symbol = parseResult.GetRequiredValue(symbolOpt);
            var idleTimeout = parseResult.GetValue(idleTimeoutOption);
            var format = formatOption is null ? OutputFormat.Text : parseResult.GetValue(formatOption);

            var solutionPath = SolutionPathResolver.Resolve(solution, project, format);
            if (solutionPath is null) return;

            await Entry.ExecuteAsync(solutionPath, symbol, idleTimeout, format);
        });

        return cmd;
    }
}
