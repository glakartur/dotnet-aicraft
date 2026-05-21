using System.CommandLine;
using DotnetAICraft.Commands.Diagnostics;
using DotnetAICraft.Commands.Shared;
using DotnetAICraft.Output;

namespace DotnetAICraft.Commands;

public static class DiagnosticsCommand
{
    private const string AcceptedSeverities = "all | error | warning | info | hidden";

    public static Command Build(
        Option<FileInfo> solutionOption,
        Option<FileInfo> projectOption,
        Option<string?> idleTimeoutOption,
        Option<bool>? debugOption = null,
        Option<OutputFormat>? formatOption = null)
    {
        var severityOpt = new Option<string>("--severity")
        {
            Description = $"Diagnostic severity filter: {AcceptedSeverities}",
            DefaultValueFactory = _ => "all"
        };

        var projectNameOpt = new Option<string?>("--project-name")
        {
            Description = "Optional project name filter"
        };

        var fileOpt = new Option<FileInfo?>("--file")
        {
            Description = "Optional file path filter"
        };

        var cmd = new Command("diagnostics", "List Roslyn compiler diagnostics across the solution")
        {
            solutionOption, projectOption, severityOpt, projectNameOpt, fileOpt, idleTimeoutOption
        };

        if (debugOption is not null)
            cmd.Add(debugOption);
        if (formatOption is not null)
            cmd.Add(formatOption);

        cmd.SetAction(async parseResult =>
        {
            var solution = parseResult.GetValue(solutionOption);
            var project = parseResult.GetValue(projectOption);
            var severity = parseResult.GetRequiredValue(severityOpt);
            var projectName = parseResult.GetValue(projectNameOpt);
            var file = parseResult.GetValue(fileOpt);
            var idleTimeout = parseResult.GetValue(idleTimeoutOption);
            var format = formatOption is null ? OutputFormat.Text : parseResult.GetValue(formatOption);

            var solutionPath = SolutionPathResolver.Resolve(solution, project, format);
            if (solutionPath is null) return;

            await Entry.ExecuteAsync(solutionPath, severity, projectName, file, idleTimeout, AcceptedSeverities, format);
        });

        return cmd;
    }
}
