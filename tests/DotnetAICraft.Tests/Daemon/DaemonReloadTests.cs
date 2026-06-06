using System.Text.Json;
using DotnetAICraft.Daemon;
using DotnetAICraft.Models;
using DotnetAICraft.Output;
using Xunit;

namespace DotnetAICraft.Tests.Daemon;

public class DaemonReloadTests
{
    private static DaemonServer NewServer()
        => new DaemonServer(Path.Combine(Path.GetTempPath(), "no-such.sln"));

    [Fact]
    public async Task DispatchAsync_Reload_AcknowledgesImmediatelyWithLoadingState_DoesNotBlockOnLoad()
    {
        // Regression guard: reload must hand the actual solution load off to a
        // background task and acknowledge at once with loadState "loading".
        // Previously it awaited the full reload inside the request, so a load
        // slower than the client's fixed response timeout tripped
        // DAEMON_RESPONSE_TIMEOUT. A non-existent solution path makes a blocking
        // reload resolve synchronously to "unloaded"; a non-blocking reload
        // reports "loading" because Loading is set before the ack is built.
        await using var server = NewServer();
        var req = new DaemonRequest(
            Id: "reload-1",
            Command: "reload",
            Params: null);

        var response = await server.DispatchAsync(req, CancellationToken.None);

        Assert.Equal(DaemonResponseStatus.Ok, response.Status);

        using var doc = JsonDocument.Parse(JsonOutput.Serialize(response.Result));
        Assert.Equal("loading", doc.RootElement.GetProperty("loadState").GetString());
        Assert.True(doc.RootElement.GetProperty("reloaded").GetBoolean());
    }
}
