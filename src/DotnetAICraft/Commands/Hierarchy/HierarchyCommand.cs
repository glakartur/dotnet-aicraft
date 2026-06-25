using System.CommandLine;
using DotnetAICraft.Commands.Hierarchy;
using DotnetAICraft.Commands.Shared;
using DotnetAICraft.Output;

namespace DotnetAICraft.Commands;

public static class HierarchyCommand
{
    public static Command Build(
        Option<FileInfo> solutionOption,
        Option<FileInfo> projectOption,
        Option<string?> idleTimeoutOption,
        Option<bool>? debugOption = null,
        Option<OutputFormat>? formatOption = null)
    {
        var fileOpt = new Option<FileInfo?>("--file") { Description = "Source file containing the symbol" };
        var lineOpt = new Option<int?>("--line") { Description = "1-based line number" };
        var colOpt = new Option<int?>("--col") { Description = "1-based column number" };
        var symbolOpt = new Option<string?>("--symbol") { Description = "Fully-qualified type name" };

        var directionOpt = new Option<string>("--direction")
        {
            Description = $"Inheritance direction (required): {Validation.DirectionAcceptedValues}",
            Required = true
        };

        var includeFrameworkOpt = new Option<bool>("--include-framework")
        {
            Description = "Include BCL/framework base types in 'up' direction (up to object)"
        };

        var maxDepthOpt = new Option<int?>("--max-depth")
        {
            Description = "Maximum traversal depth (min: 1, default: no cap)"
        };

        var cmd = new Command("hierarchy",
            "Find type inheritance lineage — base types (up) or derived types (down)")
        {
            solutionOption,
            projectOption,
            fileOpt,
            lineOpt,
            colOpt,
            symbolOpt,
            directionOpt,
            includeFrameworkOpt,
            maxDepthOpt,
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
            var direction = parseResult.GetRequiredValue(directionOpt);
            var includeFramework = parseResult.GetValue(includeFrameworkOpt);
            var maxDepth = parseResult.GetValue(maxDepthOpt);
            var idleTimeout = parseResult.GetValue(idleTimeoutOption);
            var format = formatOption is null ? OutputFormat.Text : parseResult.GetValue(formatOption);

            var solutionPath = SolutionPathResolver.Resolve(solution, project, format);
            if (solutionPath is null) return;

            await Entry.ExecuteAsync(
                solutionPath,
                file,
                line,
                col,
                symbol,
                direction,
                includeFramework,
                maxDepth,
                idleTimeout,
                format);
        });

        return cmd;
    }
}
