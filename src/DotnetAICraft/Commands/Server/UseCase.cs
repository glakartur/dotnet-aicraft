using DotnetAICraft.Daemon;
using DotnetAICraft.Models;

namespace DotnetAICraft.Commands.Server;

internal static class UseCase
{
    internal static async Task<ErrorInfo?> EnsureRunningAsync(string solutionPath, DaemonIdleTimeoutSetting? timeout)
    {
        var outcome = await DaemonStartupCoordinator.ConnectOrStartAsync(solutionPath, timeout);

        if (outcome.Type == DaemonStartupOutcomeType.Failed)
        {
            return outcome.Error ?? new ErrorInfo("DAEMON_STARTUP_FAILED", "Daemon startup failed.");
        }

        var client = outcome.Client
            ?? throw new InvalidOperationException("Daemon startup coordinator returned no client.");

        await using (client)
        {
            // On attach to an existing daemon with no --idle-timeout, send a no-op
            // `status` so the daemon's per-request BeginRequest/EndRequest cycle
            // resets the idle deadline at the currently effective session value.
            // When --idle-timeout is provided, ConnectOrStartAsync has already
            // routed through ApplyIdleTimeoutAsync (setIdleTimeout) which itself
            // resets the deadline.
            if (outcome.Type == DaemonStartupOutcomeType.AttachedExisting && timeout is null)
            {
                try
                {
                    await client.SendAsync("status");
                }
                catch (DaemonClientValidationException ex)
                {
                    return ex.Error;
                }
            }
        }

        return null;
    }

    internal static async Task<ErrorInfo?> DaemonAsync(string solutionPath, DaemonIdleTimeoutSetting? timeout)
    {
        var decision = await DaemonStartupCoordinator.PrepareServerStartAsync(solutionPath);
        if (decision.Type == DaemonServerStartDecisionType.AttachedExisting)
        {
            return null;
        }

        if (decision.Type == DaemonServerStartDecisionType.Failed)
        {
            return decision.Error ?? new ErrorInfo("DAEMON_STARTUP_FAILED", "Daemon startup failed.");
        }

        await using var server = new DaemonServer(solutionPath, timeout, decision.StartupLock);
        await server.RunAsync();
        return null;
    }

    internal static async Task<(object? result, ErrorInfo? error)> StopAsync(string solutionPath)
    {
        var client = await DaemonClient.TryConnectAsync(solutionPath);
        if (client is null)
        {
            return (null, new ErrorInfo("DAEMON_NOT_RUNNING", "No daemon running for this solution."));
        }

        await using (client)
        {
            DaemonResponse res;
            try
            {
                res = await client.SendAsync("shutdown");
            }
            catch (DaemonClientValidationException ex)
            {
                return (null, ex.Error);
            }

            return res.Status == DaemonResponseStatus.Ok
                ? (res.Result, null)
                : (null, res.Error);
        }
    }

    internal static async Task<object> StatusAsync(string solutionPath)
    {
        var client = await DaemonClient.TryConnectAsync(solutionPath);
        if (client is null)
            return new { running = false, solutionPath };

        await using (client)
        {
            DaemonResponse res;
            try
            {
                res = await client.SendAsync("status");
            }
            catch (DaemonClientValidationException ex)
            {
                return new { error = ex.Error };
            }

            return res.Status == DaemonResponseStatus.Ok
                ? res.Result!
                : new { error = res.Error };
        }
    }

    internal static async Task<(object? result, ErrorInfo? error)> ReloadAsync(string solutionPath, string? idleTimeout)
    {
        var client = await DotnetAICraft.Commands.Shared.CommandHelpers.ConnectOrWriteValidationErrorAsync(solutionPath, idleTimeout);
        if (client is null)
            return (null, null);

        if (!DotnetAICraft.Commands.Shared.CommandHelpers.TryParseIdleTimeoutMinutes(idleTimeout, out var idleTimeoutMinutes, out var parseError))
        {
            await client.DisposeAsync();
            return (null, parseError);
        }

        // The daemon now flips to Loading and acknowledges the reload immediately
        // rather than holding this request open for the whole MSBuild reload — a
        // load slower than the client's fixed response timeout previously tripped
        // DAEMON_RESPONSE_TIMEOUT.
        try
        {
            await using (client)
            {
                var ack = await client.SendAsync("reload", idleTimeoutMinutes: idleTimeoutMinutes);
                if (ack.Status != DaemonResponseStatus.Ok)
                    return (null, ack.Error);
            }
        }
        catch (DaemonClientValidationException ex)
        {
            return (null, ex.Error);
        }

        // Wait for the background reload to finish by polling `status` (with the
        // same first-run progress notices), then report the final status. Each
        // poll is a short-lived request, so a long reload no longer blocks on a
        // single response.
        try
        {
            await using var ready = await DaemonClient.ConnectOrStartAsync(solutionPath);
            var status = await ready.SendAsync("status");
            return status.Status == DaemonResponseStatus.Ok
                ? (status.Result, null)
                : (null, status.Error);
        }
        catch (DaemonClientValidationException ex)
        {
            return (null, ex.Error);
        }
    }
}
