using DotnetAICraft.Daemon;
using Xunit;

namespace DotnetAICraft.Tests.Daemon;

/// <summary>
/// Automatic full reload against a solution that really loads (copy of the sample fixture).
/// </summary>
[Trait(LiveSampleSolution.TraitName, LiveSampleSolution.TraitValue)]
public sealed class DaemonFullReloadLiveTests
{
    private static DaemonServer NewServer(string solutionPath)
        => new(solutionPath)
        {
            FullReloadDebounce = TimeSpan.FromMilliseconds(20),
            FullReloadRetryBaseDelay = TimeSpan.FromMilliseconds(20),
        };

    [Fact]
    public async Task ScheduleFullReload_SuccessfulLoad_DoesNotRetry()
    {
        await using var live = await LiveSampleSolution.StartAsync(serverFactory: NewServer);
        var before = live.Server.LoadAttemptCount;

        live.Server.ScheduleFullReload("trigger");
        await LiveSampleSolution.EventuallyAsync(() => live.Server.LoadAttemptCount == before + 1);
        await live.WaitLoadedAsync(before + 1);
        await Task.Delay(500);

        Assert.Equal(before + 1, live.Server.LoadAttemptCount);
        Assert.Equal("loaded", (await live.StatusAsync()).LoadState);
        Assert.Equal(0, live.Server.FullReloadRetryCountForTesting);
    }

    [Fact]
    public async Task ScheduleFullReload_SuccessAfterRetries_ResetsRetryBudget()
    {
        await using var live = await LiveSampleSolution.StartAsync(serverFactory: NewServer);
        var solution = live.PathOf("SampleSolution.sln");
        var hidden = solution + ".hidden";
        File.Move(solution, hidden);

        live.Server.ScheduleFullReload("broken");
        await LiveSampleSolution.EventuallyAsync(() => live.Server.FullReloadRetryCountForTesting >= 1);

        File.Move(hidden, solution);
        await LiveSampleSolution.EventuallyAsync(
            async () => live.Server.FullReloadRetryCountForTesting == 0 && (await live.StatusAsync()).LoadState == "loaded",
            TimeSpan.FromSeconds(120));

        // Break the solution again: a new trigger gets a full fresh budget.
        await live.Server.GetBackgroundLoadTaskForTesting();
        File.Move(solution, hidden);
        var start = live.Server.LoadAttemptCount;
        live.Server.ScheduleFullReload("broken again");
        var expected = start + 1 + DaemonServer.MaxFullReloadRetries;
        await LiveSampleSolution.EventuallyAsync(() => live.Server.LoadAttemptCount >= expected);
        await Task.Delay(500);

        Assert.Equal(expected, live.Server.LoadAttemptCount);
    }
}
