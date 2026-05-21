using System.CommandLine;
using DotnetAICraft.Daemon;
using DotnetAICraft.Commands.Server;
using DotnetAICraft.Commands.Shared;
using DotnetAICraft.Output;

namespace DotnetAICraft.Commands;

public static class ServerCommand
{
    public static Command Build(
        Option<FileInfo> solutionOption,
        Option<FileInfo> projectOption,
        Option<string?> idleTimeoutOption,
        Option<bool>? debugOption = null,
        Option<OutputFormat>? formatOption = null)
    {
        var cmd = new Command("server", "Manage the analysis daemon");

        cmd.Add(BuildStart(solutionOption, projectOption, idleTimeoutOption, debugOption, formatOption));
        cmd.Add(BuildDaemon(solutionOption, projectOption, idleTimeoutOption, debugOption, formatOption));
        cmd.Add(BuildStop(solutionOption, projectOption, debugOption, formatOption));
        cmd.Add(BuildStatus(solutionOption, projectOption, debugOption, formatOption));
        cmd.Add(BuildReload(solutionOption, projectOption, idleTimeoutOption, debugOption, formatOption));

        return cmd;
    }

    private static Command BuildDaemon(
        Option<FileInfo> solutionOption,
        Option<FileInfo> projectOption,
        Option<string?> idleTimeoutOption,
        Option<bool>? debugOption,
        Option<OutputFormat>? formatOption)
    {
        var cmd = new Command("daemon", "Run the analysis daemon in the foreground (internal use only)")
        {
            Hidden = true,
        };
        cmd.Add(solutionOption);
        cmd.Add(projectOption);
        cmd.Add(idleTimeoutOption);
        if (debugOption is not null)
            cmd.Add(debugOption);
        if (formatOption is not null)
            cmd.Add(formatOption);

        cmd.SetAction(async parseResult =>
        {
            var solution = parseResult.GetValue(solutionOption);
            var project = parseResult.GetValue(projectOption);
            var idleTimeout = parseResult.GetValue(idleTimeoutOption);
            var format = formatOption is null ? OutputFormat.Text : parseResult.GetValue(formatOption);

            var solutionPath = SolutionPathResolver.Resolve(solution, project, format);
            if (solutionPath is null) return;

            await Entry.DaemonAsync(solutionPath, idleTimeout, format);
        });

        return cmd;
    }

    private static Command BuildStart(
        Option<FileInfo> solutionOption,
        Option<FileInfo> projectOption,
        Option<string?> idleTimeoutOption,
        Option<bool>? debugOption,
        Option<OutputFormat>? formatOption)
    {
        var cmd = new Command("start", "Start the daemon (usually called automatically)");
        cmd.Add(solutionOption);
        cmd.Add(projectOption);
        cmd.Add(idleTimeoutOption);
        if (debugOption is not null)
            cmd.Add(debugOption);
        if (formatOption is not null)
            cmd.Add(formatOption);

        cmd.SetAction(async parseResult =>
        {
            var solution = parseResult.GetValue(solutionOption);
            var project = parseResult.GetValue(projectOption);
            var idleTimeout = parseResult.GetValue(idleTimeoutOption);
            var format = formatOption is null ? OutputFormat.Text : parseResult.GetValue(formatOption);

            var solutionPath = SolutionPathResolver.Resolve(solution, project, format);
            if (solutionPath is null) return;

            await Entry.StartAsync(solutionPath, idleTimeout, format);
        });

        return cmd;
    }

    private static Command BuildStop(
        Option<FileInfo> solutionOption,
        Option<FileInfo> projectOption,
        Option<bool>? debugOption,
        Option<OutputFormat>? formatOption)
    {
        var cmd = new Command("stop", "Stop the running daemon for this solution");
        cmd.Add(solutionOption);
        cmd.Add(projectOption);
        if (debugOption is not null)
            cmd.Add(debugOption);
        if (formatOption is not null)
            cmd.Add(formatOption);

        cmd.SetAction(async parseResult =>
        {
            var solution = parseResult.GetValue(solutionOption);
            var project = parseResult.GetValue(projectOption);
            var format = formatOption is null ? OutputFormat.Text : parseResult.GetValue(formatOption);

            var solutionPath = SolutionPathResolver.Resolve(solution, project, format);
            if (solutionPath is null) return;

            await Entry.StopAsync(solutionPath, format);
        });

        return cmd;
    }

    private static Command BuildStatus(
        Option<FileInfo> solutionOption,
        Option<FileInfo> projectOption,
        Option<bool>? debugOption,
        Option<OutputFormat>? formatOption)
    {
        var cmd = new Command("status", "Show daemon status");
        cmd.Add(solutionOption);
        cmd.Add(projectOption);
        if (debugOption is not null)
            cmd.Add(debugOption);
        if (formatOption is not null)
            cmd.Add(formatOption);

        cmd.SetAction(async parseResult =>
        {
            var solution = parseResult.GetValue(solutionOption);
            var project = parseResult.GetValue(projectOption);
            var format = formatOption is null ? OutputFormat.Text : parseResult.GetValue(formatOption);

            var solutionPath = SolutionPathResolver.Resolve(solution, project, format);
            if (solutionPath is null) return;

            await Entry.StatusAsync(solutionPath, format);
        });

        return cmd;
    }

    private static Command BuildReload(
        Option<FileInfo> solutionOption,
        Option<FileInfo> projectOption,
        Option<string?> idleTimeoutOption,
        Option<bool>? debugOption,
        Option<OutputFormat>? formatOption)
    {
        var cmd = new Command("reload", "Reload the solution (e.g. after adding/removing projects)");
        cmd.Add(solutionOption);
        cmd.Add(projectOption);
        cmd.Add(idleTimeoutOption);
        if (debugOption is not null)
            cmd.Add(debugOption);
        if (formatOption is not null)
            cmd.Add(formatOption);

        cmd.SetAction(async parseResult =>
        {
            var solution = parseResult.GetValue(solutionOption);
            var project = parseResult.GetValue(projectOption);
            var idleTimeout = parseResult.GetValue(idleTimeoutOption);
            var format = formatOption is null ? OutputFormat.Text : parseResult.GetValue(formatOption);

            var solutionPath = SolutionPathResolver.Resolve(solution, project, format);
            if (solutionPath is null) return;

            await Entry.ReloadAsync(solutionPath, idleTimeout, format);
        });

        return cmd;
    }
}
