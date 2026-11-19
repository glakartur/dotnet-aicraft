using DotnetAICraft.Daemon;
using DotnetAICraft.Tests.Support;
using System.Runtime.Versioning;
using Xunit;

namespace DotnetAICraft.Tests.Daemon;

/// <summary>
/// Daemon-level tests for the one-time retry of unreadable files and for the
/// delayed retry after a failed watcher recreate.
/// </summary>
[Collection("Console output")]
[Trait(LiveSampleSolution.TraitName, LiveSampleSolution.TraitValue)]
public sealed class DaemonRetryAndWatcherRecoveryTests
{
    private const string RetryLog = "File update queued for retry:";
    private const string CannotReadLog = "File update error: cannot read";

    [LinuxOnlyFact]
    [UnsupportedOSPlatform("windows")]
    public async Task UnreadableFile_IsQueuedOnceAndDroppedOnSecondFailure()
    {
        using var capture = ConsoleErrorCapture.Start();
        await using var live = await LiveSampleSolution.StartAsync(
            serverFactory: path => new DaemonServer(path) { FileSetDebounce = TimeSpan.FromMilliseconds(100) });
        var locked = MoveInUnreadableFile(live, "Locked.cs");
        if (locked is null)
            return; // running as root: permissions do not block reads

        await LiveSampleSolution.EventuallyAsync(() => Count(capture, CannotReadLog) == 1);
        await Task.Delay(1500); // a third attempt would have happened by now

        Assert.Equal(1, Count(capture, RetryLog));
        Assert.Equal(1, Count(capture, CannotReadLog));
        Assert.Empty(live.DocumentsAt(locked));
    }

    [LinuxOnlyFact]
    [UnsupportedOSPlatform("windows")]
    public async Task UnreadableFile_ThatBecomesReadableBeforeRetry_IsApplied()
    {
        using var capture = ConsoleErrorCapture.Start();
        await using var live = await LiveSampleSolution.StartAsync(
            serverFactory: path => new DaemonServer(path) { FileSetDebounce = TimeSpan.FromMilliseconds(800) });
        var locked = MoveInUnreadableFile(live, "Recovers.cs");
        if (locked is null)
            return; // running as root: permissions do not block reads

        await LiveSampleSolution.EventuallyAsync(() => Count(capture, RetryLog) == 1);
        File.SetUnixFileMode(locked, UnixFileMode.UserRead | UnixFileMode.UserWrite);

        await LiveSampleSolution.EventuallyAsync(() => live.DocumentsAt(locked).Count == 1);
        Assert.Equal(0, Count(capture, CannotReadLog));
    }

    [Fact]
    public async Task FailedWatcherRecreate_IsRetriedAfterDelayAndReloads()
    {
        await using var live = await StartWithWatcherTimingAsync(retryDelay: TimeSpan.FromMilliseconds(50));
        var away = BreakWatchedDirectory(live);

        live.Server.HandleWatcherError(WatcherKind.Source, new IOException("watcher died"));

        Assert.False(live.Server.GetWatcher(WatcherKind.Source)!.EnableRaisingEvents);
        Directory.Move(away, live.Root);

        await LiveSampleSolution.EventuallyAsync(
            () => live.Server.GetWatcher(WatcherKind.Source)!.EnableRaisingEvents);
        await LiveSampleSolution.EventuallyAsync(() => live.Server.LoadAttemptCount >= 2, TimeSpan.FromSeconds(60));
        await live.WaitLoadedAsync(expectedAttempts: 2);

        live.WriteFile("src/Sample.Domain/AfterRetry.cs", "namespace Sample.Domain; public sealed class AfterRetry { }");
        await LiveSampleSolution.EventuallyAsync(async () => (await live.SymbolsAsync("AfterRetry")).Count > 0);
    }

    [Fact]
    public async Task FailedWatcherRecreate_DisposeBeforeDelay_CreatesNoWatcher()
    {
        var live = await StartWithWatcherTimingAsync(retryDelay: TimeSpan.FromMilliseconds(400));
        var away = BreakWatchedDirectory(live);
        try
        {
            live.Server.HandleWatcherError(WatcherKind.Source, new IOException("watcher died"));
            Assert.Equal(1, live.Server.PendingWatcherRetryCountForTesting);

            await live.Server.DisposeAsync();
            Directory.Move(away, live.Root);
            await Task.Delay(1000);

            Assert.Null(live.Server.GetWatcher(WatcherKind.Source));
        }
        finally
        {
            if (Directory.Exists(away))
                Directory.Move(away, live.Root);
            try { Directory.Delete(live.Root, recursive: true); } catch { /* best-effort cleanup */ }
        }
    }

    [Fact]
    public async Task FailedWatcherRecreate_TwoFailures_KeepOnlyOnePendingRetry()
    {
        await using var live = await StartWithWatcherTimingAsync(retryDelay: TimeSpan.FromSeconds(5));
        var away = BreakWatchedDirectory(live);

        live.Server.HandleWatcherError(WatcherKind.Source, new IOException("first"));
        live.Server.HandleWatcherError(WatcherKind.Source, new IOException("second"));
        Assert.Equal(1, live.Server.PendingWatcherRetryCountForTesting);

        // A different watcher kind has its own retry.
        live.Server.GetWatcher(WatcherKind.Tree)!.EnableRaisingEvents = false;
        live.Server.HandleWatcherError(WatcherKind.Tree, new IOException("tree"));
        Assert.Equal(2, live.Server.PendingWatcherRetryCountForTesting);

        Directory.Move(away, live.Root);
    }

    private static Task<LiveSampleSolution> StartWithWatcherTimingAsync(TimeSpan retryDelay)
        => LiveSampleSolution.StartAsync(serverFactory: path => new DaemonServer(path)
        {
            WatcherRecreateRetryDelay = retryDelay,
            FullReloadDebounce = TimeSpan.FromSeconds(1),
        });

    /// <summary>Stops the source watcher and moves the watched directory away, so a recreate must fail.</summary>
    private static string BreakWatchedDirectory(LiveSampleSolution live)
    {
        live.Server.GetWatcher(WatcherKind.Source)!.EnableRaisingEvents = false;
        var away = live.Root + ".away";
        Directory.Move(live.Root, away);
        return away;
    }

    /// <summary>Moves a new, unreadable file into the tree. Null when the file is readable anyway (root).</summary>
    [UnsupportedOSPlatform("windows")]
    private static string? MoveInUnreadableFile(LiveSampleSolution live, string name)
    {
        var temp = Path.Combine(Path.GetTempPath(), "aicraft-" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(temp, "namespace Sample.Domain; public sealed class " + Path.GetFileNameWithoutExtension(name) + " { }");
        File.SetUnixFileMode(temp, UnixFileMode.None);
        try
        {
            using var probe = File.OpenRead(temp);
            File.Delete(temp);
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            var target = live.PathOf("src/Sample.Domain/" + name);
            File.Move(temp, target);
            return target;
        }
    }

    private static int Count(ConsoleErrorCapture capture, string text)
    {
        var output = capture.GetOutput();
        var count = 0;
        for (var i = output.IndexOf(text, StringComparison.Ordinal); i >= 0; i = output.IndexOf(text, i + text.Length, StringComparison.Ordinal))
            count++;
        return count;
    }
}
