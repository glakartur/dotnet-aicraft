using System.CommandLine;
using System.CommandLine.Parsing;
using System.Text.Json;
using DotnetAICraft.Commands.Server;
using DotnetAICraft.Commands.Shared;
using DotnetAICraft.Diagnostics;
using DotnetAICraft.Models;
using DotnetAICraft.Output;
using Microsoft.Build.Locator;

namespace DotnetAICraft.Daemon;

public static class DaemonExecutable
{
    public static Task<int> RunAsync(string[] args)
        => RunAsync(args, Console.Out, Console.Error);

    internal static async Task<int> RunAsync(string[] args, TextWriter stdout, TextWriter stderr)
    {
        try
        {
            EnsureMsBuildRegistered();

            DebugLog.ConfigureFromEnvironment();
            DebugLog.ConfigureFromArgs(args);

            if (args.Length == 0)
            {
                await stdout.WriteLineAsync("This executable is for internal daemon/testing use. Use `dotnet aicraft`.");
                return 0;
            }

            return args[0] switch
            {
                "daemon" => await RunDaemonModeAsync(args[1..]),
                "cli" => await RunCliModeAsync(args[1..]),
                _ => await WriteUsageAndReturnAsync(stdout)
            };
        }
        catch (Exception ex)
        {
            if (DebugLog.IsEnabled)
                await stderr.WriteLineAsync(ex.ToString());

            var error = TopLevelExceptionFirewall.Map(ex);
            JsonOutput.WriteError(error.Code, error.Message, error.Details);
            return 1;
        }
    }

    private static void EnsureMsBuildRegistered()
    {
        if (MSBuildLocator.IsRegistered)
            return;

        try
        {
            var instances = MSBuildLocator.QueryVisualStudioInstances()
                .OrderByDescending(i => i.Version)
                .ToList();

            var instance = instances.FirstOrDefault()
                ?? throw new InvalidOperationException(
                    "Could not find .NET SDK. Make sure 'dotnet' is installed and available in PATH.");

            MSBuildLocator.RegisterInstance(instance);
        }
        catch (Exception ex)
        {
            throw new DaemonClientValidationException(new ErrorInfo(
                "MSBUILD_REGISTRATION_FAILED",
                ex.Message,
                new { type = ex.GetType().FullName }));
        }
    }

    private static async Task<int> RunDaemonModeAsync(string[] args)
    {
        var solutionOption = new Option<FileInfo>("--solution", "-s")
        {
            Description = "Path to the .sln/.slnx file (also accepts .csproj/.vbproj/.fsproj).",
            Required = false
        };

        var projectOption = new Option<FileInfo>("--project", "-p")
        {
            Description = "Path to the .csproj/.vbproj/.fsproj file (also accepts .sln/.slnx).",
            Required = false
        };

        var idleTimeoutOption = new Option<string?>("--idle-timeout")
        {
            Description = "Daemon idle timeout for this session: 'off' or a positive duration (m|h)"
        };

        var formatOption = new Option<OutputFormat>("--format")
        {
            Description = "Output format: text (default) | json",
            DefaultValueFactory = _ => OutputFormat.Text,
            CustomParser = ParseOutputFormat
        };

        var command = new RootCommand("aicraft-daemon daemon mode");
        command.Add(solutionOption);
        command.Add(projectOption);
        command.Add(idleTimeoutOption);
        command.Add(formatOption);

        command.SetAction(async parseResult =>
        {
            var solution = parseResult.GetValue(solutionOption);
            var project = parseResult.GetValue(projectOption);
            var idleTimeout = parseResult.GetValue(idleTimeoutOption);
            var format = parseResult.GetValue(formatOption);

            var solutionPath = SolutionPathResolver.Resolve(solution, project, format);
            if (solutionPath is null)
                return;

            if (!DotnetAICraft.Commands.Server.Validation.TryParseIdleTimeout(idleTimeout, out var timeout, out var error))
            {
                DotnetAICraft.Commands.Server.OutputMapping.WriteError(error, format);
                return;
            }

            var decision = await DaemonStartupCoordinator.PrepareServerStartAsync(solutionPath);
            if (decision.Type == DaemonServerStartDecisionType.AttachedExisting)
                return;

            if (decision.Type == DaemonServerStartDecisionType.Failed)
            {
                DotnetAICraft.Commands.Server.OutputMapping.WriteError(
                    decision.Error ?? new ErrorInfo("DAEMON_STARTUP_FAILED", "Daemon startup failed."),
                    format);
                return;
            }

            await using var server = new DaemonServer(solutionPath, timeout, decision.StartupLock);
            await server.RunAsync();
        });

        return await command.Parse(args).InvokeAsync();
    }

    private static async Task<int> RunCliModeAsync(string[] args)
    {
        var solutionOption = new Option<FileInfo>("--solution", "-s")
        {
            Description = "Path to the .sln/.slnx file (also accepts .csproj/.vbproj/.fsproj).",
            Required = false
        };

        var projectOption = new Option<FileInfo>("--project", "-p")
        {
            Description = "Path to the .csproj/.vbproj/.fsproj file (also accepts .sln/.slnx).",
            Required = false
        };

        var idleTimeoutOption = new Option<int?>("--idle-timeout-minutes")
        {
            Description = "Optional idle-timeout override sent with the request."
        };

        var commandArgument = new Argument<string>("command");
        var paramsArgument = new Argument<string?>("params")
        {
            Arity = ArgumentArity.ZeroOrOne,
            Description = "Optional JSON object passed as daemon request params."
        };

        var root = new RootCommand("aicraft-daemon cli mode");
        root.Add(solutionOption);
        root.Add(projectOption);
        root.Add(idleTimeoutOption);
        root.Add(commandArgument);
        root.Add(paramsArgument);

        root.SetAction(async parseResult =>
        {
            var solution = parseResult.GetValue(solutionOption);
            var project = parseResult.GetValue(projectOption);
            var solutionPath = SolutionPathResolver.Resolve(solution, project, OutputFormat.Json);
            if (solutionPath is null)
                return;

            var daemonCommand = parseResult.GetValue(commandArgument)
                ?? throw new InvalidOperationException("Missing daemon command.");
            var rawParams = parseResult.GetValue(paramsArgument);
            var idleTimeoutMinutes = parseResult.GetValue(idleTimeoutOption);
            var requestParams = ParseRequestParams(rawParams);

            var client = await DaemonClient.TryConnectAsync(solutionPath);
            if (client is null)
            {
                JsonOutput.WriteError(
                    "DAEMON_NOT_RUNNING",
                    "No daemon running for this solution.",
                    new { solutionPath });
                return;
            }

            await using (client)
            {
                var response = await client.SendAsync<object?>(daemonCommand, requestParams, idleTimeoutMinutes: idleTimeoutMinutes);
                JsonOutput.Write(response);
            }
        });

        return await root.Parse(args).InvokeAsync();
    }

    private static object? ParseRequestParams(string? rawParams)
    {
        if (string.IsNullOrWhiteSpace(rawParams))
            return null;

        using var doc = JsonDocument.Parse(rawParams);
        return doc.RootElement.Clone();
    }

    private static OutputFormat ParseOutputFormat(ArgumentResult result)
    {
        if (result.Tokens.Count == 0)
            return OutputFormat.Text;

        var raw = result.Tokens[0].Value;
        return raw.ToLowerInvariant() switch
        {
            "text" => OutputFormat.Text,
            "json" => OutputFormat.Json,
            _ => InvalidFormat(result, raw)
        };
    }

    private static OutputFormat InvalidFormat(ArgumentResult result, string raw)
    {
        result.AddError($"Invalid --format value '{raw}'. Accepted values: text, json.");
        return OutputFormat.Text;
    }

    private static async Task<int> WriteUsageAndReturnAsync(TextWriter stdout)
    {
        await stdout.WriteLineAsync("This executable is for internal daemon/testing use. Use `dotnet aicraft`.");
        return 0;
    }
}
