using System.CommandLine;
using DotnetAICraft.Commands.Shared;
using DotnetAICraft.Commands.Unused;
using DotnetAICraft.Output;

namespace DotnetAICraft.Commands;

public static class UnusedCommand
{
    public static Command Build(
        Option<FileInfo> solutionOption,
        Option<FileInfo> projectOption,
        Option<string?> idleTimeoutOption,
        Option<bool>? debugOption = null,
        Option<OutputFormat>? formatOption = null)
    {
        var kindOpt = new Option<string>("--kind")
        {
            Description = $"Symbol kind filter: {AnalysisCommandMetadata.UnusedKindAcceptedValues}",
            DefaultValueFactory = _ => "all"
        };

        var projectNameOpt = new Option<string?>("--project-name")
        {
            Description = "Optional project name filter"
        };

        var publicOnlyOpt = new Option<bool>("--public-only")
        {
            Description = "Analyze only public symbols"
        };

        var includeGeneratedOpt = new Option<bool>("--include-generated")
        {
            Description = "Include generated-code symbols in analysis (default: false)"
        };

        var cmd = new Command("unused", "Find likely unused symbols with confidence and reason")
        {
            solutionOption,
            projectOption,
            kindOpt,
            projectNameOpt,
            publicOnlyOpt,
            includeGeneratedOpt,
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
            var kind = parseResult.GetRequiredValue(kindOpt);
            var projectName = parseResult.GetValue(projectNameOpt);
            var publicOnly = parseResult.GetValue(publicOnlyOpt);
            var includeGenerated = parseResult.GetValue(includeGeneratedOpt);
            var idleTimeout = parseResult.GetValue(idleTimeoutOption);
            var format = formatOption is null ? OutputFormat.Text : parseResult.GetValue(formatOption);

            var solutionPath = SolutionPathResolver.Resolve(solution, project, format);
            if (solutionPath is null) return;

            await Entry.ExecuteAsync(solutionPath, kind, projectName, publicOnly, includeGenerated, idleTimeout, format);
        });

        return cmd;
    }
}
