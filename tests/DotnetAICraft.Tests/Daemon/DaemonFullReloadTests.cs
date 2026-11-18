using DotnetAICraft.Daemon;
using DotnetAICraft.Models;
using Xunit;

namespace DotnetAICraft.Tests.Daemon;

/// <summary>
/// Full reload triggers (metadata change, reconciler fallback, watcher error)
/// all go through ScheduleFullReload → StartBackgroundLoad and are never lost.
/// The solution path does not exist, so each load fails fast; the tests count
/// load attempts.
/// </summary>
public sealed class DaemonFullReloadTests
{
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(100);

    private static DaemonServer NewServer(string? solutionPath = null)
        => new(solutionPath ?? Path.Combine(Path.GetTempPath(), "no-such.sln")) { FullReloadDebounce = Debounce };

    [Fact]
    public async Task ScheduleFullReload_TwiceWithinDebounce_LoadsOnce()
    {
        await using var server = NewServer();

        server.ScheduleFullReload("first");
        server.ScheduleFullReload("second");

        await WaitUntilAsync(() => server.LoadAttemptCount >= 1);
        await Task.Delay(Debounce * 3);
        await server.GetBackgroundLoadTaskForTesting();
        Assert.Equal(1, server.LoadAttemptCount);
    }

    [Fact]
    public async Task ScheduleFullReload_DuringRunningLoad_RunsSecondLoadAfterIt()
    {
        await using var server = NewServer();
        var gate = new TaskCompletionSource();
        server.LoadGateForTesting = gate.Task;

        await server.DispatchAsync(new DaemonRequest("reload-1", "reload", null), CancellationToken.None);
        await WaitUntilAsync(() => server.LoadAttemptCount == 1); // first load is blocked on the gate

        server.ScheduleFullReload("metadata changed during load");
        await Task.Delay(Debounce * 3);
        Assert.Equal(1, server.LoadAttemptCount);

        gate.SetResult();
        await WaitUntilAsync(() => server.LoadAttemptCount == 2);
        await server.GetBackgroundLoadTaskForTesting();
        Assert.Equal(2, server.LoadAttemptCount);
    }

    [Fact]
    public async Task HandleWatcherError_SchedulesReload_StatusBecomesLoading()
    {
        var root = CreateTempDirectory();
        try
        {
            await using var server = NewServer(Path.Combine(root, "App.sln"));
            var gate = new TaskCompletionSource();
            server.LoadGateForTesting = gate.Task; // keep the load running
            try
            {
                server.HandleWatcherError(DaemonServer.WatcherKind.Source, new InternalBufferOverflowException());

                await WaitUntilAsync(() => server.LoadAttemptCount == 1);
                Assert.Equal("loading", await GetLoadStateAsync(server));
            }
            finally
            {
                gate.SetResult();
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task HandleWatcherError_StoppedWatcher_RecreatesIt()
    {
        var root = CreateTempDirectory();
        try
        {
            await using var server = NewServer(Path.Combine(root, "App.sln"));
            server.StartWorkspaceServices();
            var original = server.GetWatcher(DaemonServer.WatcherKind.Source)!;
            original.EnableRaisingEvents = false;

            server.HandleWatcherError(DaemonServer.WatcherKind.Source, new IOException("watcher died"));

            var recreated = server.GetWatcher(DaemonServer.WatcherKind.Source);
            Assert.NotNull(recreated);
            Assert.NotSame(original, recreated);
            Assert.True(recreated.EnableRaisingEvents);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task HandleWatcherError_RecreateFails_LogsAndStillSchedulesReload()
    {
        var missingRoot = Path.Combine(Path.GetTempPath(), "aicraft-missing-" + Guid.NewGuid().ToString("N"));
        await using var server = NewServer(Path.Combine(missingRoot, "App.sln"));

        var error = Record.Exception(() =>
            server.HandleWatcherError(DaemonServer.WatcherKind.Tree, new IOException("directory removed")));

        Assert.Null(error);
        Assert.Null(server.GetWatcher(DaemonServer.WatcherKind.Tree));
        await WaitUntilAsync(() => server.LoadAttemptCount == 1);
    }

    [Fact]
    public async Task HandleWatcherError_AfterDispose_DoesNotRecreateWatcherOrReload()
    {
        var root = CreateTempDirectory();
        try
        {
            var server = NewServer(Path.Combine(root, "App.sln"));
            await server.DisposeAsync();

            server.HandleWatcherError(DaemonServer.WatcherKind.Source, new IOException("late error"));

            Assert.Null(server.GetWatcher(DaemonServer.WatcherKind.Source));
            await Task.Delay(Debounce * 3);
            Assert.Equal(0, server.LoadAttemptCount);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task HandleWatcherError_FromWatcherThatIsNotRegistered_DoesNotRecreate()
    {
        var root = CreateTempDirectory();
        try
        {
            await using var server = NewServer(Path.Combine(root, "App.sln"));
            server.StartWorkspaceServices();
            var registered = server.GetWatcher(DaemonServer.WatcherKind.Source);
            using var starting = new FileSystemWatcher(root);

            server.HandleWatcherError(DaemonServer.WatcherKind.Source, starting, new IOException("error while starting"));

            Assert.Same(registered, server.GetWatcher(DaemonServer.WatcherKind.Source));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task HandleWatcherError_FromWatcherThatIsNotRegistered_OnlyLogs()
    {
        var root = CreateTempDirectory();
        try
        {
            await using var server = NewServer(Path.Combine(root, "App.sln"));
            server.StartWorkspaceServices();
            await WaitUntilAsync(() => server.LoadAttemptCount >= 1);
            await server.GetBackgroundLoadTaskForTesting();
            var before = server.LoadAttemptCount;
            using var starting = new FileSystemWatcher(root);

            server.HandleWatcherError(DaemonServer.WatcherKind.Source, starting, new IOException("error while starting"));

            await Task.Delay(Debounce * 3);
            Assert.Equal(before, server.LoadAttemptCount);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ScheduleFullReload_FailingLoad_RetriesAtMostThreeTimes()
    {
        await using var server = NewRetryServer();

        server.ScheduleFullReload("trigger");

        await WaitUntilAsync(() => server.LoadAttemptCount == 1 + DaemonServer.MaxFullReloadRetries);
        await Task.Delay(Debounce * 8);
        await server.GetBackgroundLoadTaskForTesting();
        Assert.Equal(1 + DaemonServer.MaxFullReloadRetries, server.LoadAttemptCount);
    }

    [Fact]
    public async Task ScheduleFullReload_RetriesExhausted_NewTriggerStartsFreshRetryBudget()
    {
        await using var server = NewRetryServer();

        server.ScheduleFullReload("first");
        await WaitUntilAsync(() => server.LoadAttemptCount == 1 + DaemonServer.MaxFullReloadRetries);
        await Task.Delay(Debounce * 8);

        server.ScheduleFullReload("second");
        await WaitUntilAsync(() => server.LoadAttemptCount == 2 * (1 + DaemonServer.MaxFullReloadRetries));
        await Task.Delay(Debounce * 8);
        Assert.Equal(2 * (1 + DaemonServer.MaxFullReloadRetries), server.LoadAttemptCount);
    }

    private static DaemonServer NewRetryServer(string? solutionPath = null)
        => new(solutionPath ?? Path.Combine(Path.GetTempPath(), "no-such.sln"))
        {
            FullReloadDebounce = TimeSpan.FromMilliseconds(20),
            FullReloadRetryBaseDelay = TimeSpan.FromMilliseconds(20),
        };

    [Fact]
    public async Task StartWorkspaceServices_UnreadableSubdirectory_DoesNotRecurseOrHang()
    {
        if (OperatingSystem.IsWindows())
            return;

        var root = CreateTempDirectory();
        var locked = Path.Combine(root, "locked");
        Directory.CreateDirectory(locked);
        File.SetUnixFileMode(locked, UnixFileMode.None);
        try
        {
            await using var server = NewServer(Path.Combine(root, "App.sln"));

            var start = Task.Run(server.StartWorkspaceServices);
            var finished = await Task.WhenAny(start, Task.Delay(TimeSpan.FromSeconds(10)));

            Assert.Same(start, finished);
            await start;
            await WaitUntilAsync(() => server.LoadAttemptCount >= 1);
            await Task.Delay(Debounce * 3);
            Assert.Equal(1, server.LoadAttemptCount);
        }
        finally
        {
            File.SetUnixFileMode(locked, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ScheduleFullReload_ContinuousTriggers_ReloadNoLaterThanMaxDelay()
    {
        await using var server = new DaemonServer(Path.Combine(Path.GetTempPath(), "no-such.sln"))
        {
            FullReloadDebounce = TimeSpan.FromMilliseconds(200),
            FullReloadMaxDelay = TimeSpan.FromMilliseconds(500),
        };

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        while (server.LoadAttemptCount == 0 && stopwatch.Elapsed < TimeSpan.FromSeconds(3))
        {
            server.ScheduleFullReload("steady stream");
            await Task.Delay(50);
        }

        Assert.True(server.LoadAttemptCount >= 1, "the reload was postponed for the whole stream");
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(2), $"first reload after {stopwatch.Elapsed}");
    }

    private static async Task<string?> GetLoadStateAsync(DaemonServer server)
    {
        var response = await server.DispatchAsync(new DaemonRequest("status-1", "status", null), CancellationToken.None);
        return Assert.IsType<DaemonStatus>(response.Result).LoadState;
    }

    private static string CreateTempDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "aicraft-reload-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    internal static async Task WaitUntilAsync(Func<bool> condition, int timeoutSeconds = 5)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException($"Condition not met in {timeoutSeconds} s.");
            await Task.Delay(20);
        }
    }
}
