using DotnetAICraft.Daemon.FileSetSync;
using DotnetAICraft.Tests.Support;
using Xunit;

namespace DotnetAICraft.Tests.Daemon.FileSetSync;

public sealed class FileSetChangeQueueTests
{
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(400);
    private static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(2);

    [Fact]
    public void Enqueue_ManyEventsInDebounceWindow_FlushesOnceWithDistinctPaths()
    {
        var time = new ManualTimeProvider();
        var batches = new List<FileSetBatch>();
        using var queue = NewQueue(time, batches);

        queue.Enqueue("/s/A.cs");
        queue.Enqueue("/s/A.cs");
        queue.Enqueue("/s/B.cs");
        time.Advance(TimeSpan.FromMilliseconds(100));
        queue.Enqueue("/s/C.cs");
        time.Advance(TimeSpan.FromMilliseconds(399));
        Assert.Empty(batches);

        time.Advance(TimeSpan.FromMilliseconds(1));

        var batch = Assert.Single(batches);
        Assert.Equal(["/s/A.cs", "/s/B.cs", "/s/C.cs"], batch.Files.Order());
        Assert.Empty(batch.Directories);
    }

    [Fact]
    public void Enqueue_ContinuousStreamLongerThanMaxDelay_FlushesNoLaterThanMaxDelay()
    {
        var time = new ManualTimeProvider();
        var batches = new List<FileSetBatch>();
        using var queue = NewQueue(time, batches);

        var elapsed = TimeSpan.Zero;
        while (elapsed < MaxDelay && batches.Count == 0)
        {
            queue.Enqueue($"/s/F{elapsed.TotalMilliseconds}.cs");
            time.Advance(TimeSpan.FromMilliseconds(300));
            elapsed += TimeSpan.FromMilliseconds(300);
        }

        Assert.Single(batches);
        Assert.True(elapsed <= MaxDelay + TimeSpan.FromMilliseconds(300), $"flushed after {elapsed}");
    }

    [Fact]
    public void Enqueue_DirectoryMarkerAndFileInOneWindow_FlushesBothInOneBatch()
    {
        var time = new ManualTimeProvider();
        var batches = new List<FileSetBatch>();
        using var queue = NewQueue(time, batches);

        queue.EnqueueDirectory("/s/Models");
        queue.Enqueue("/s/A.cs");
        time.Advance(Debounce);

        var batch = Assert.Single(batches);
        Assert.Equal(["/s/A.cs"], batch.Files);
        Assert.Equal(["/s/Models"], batch.Directories);
    }

    [Fact]
    public async Task Enqueue_DuringFlush_GoesToNextBatch()
    {
        var time = new ManualTimeProvider();
        var batches = new List<FileSetBatch>();
        var release = new TaskCompletionSource();
        using var queue = new FileSetChangeQueue(
            async batch =>
            {
                lock (batches)
                    batches.Add(batch);
                if (batches.Count == 1)
                    await release.Task;
            },
            Debounce,
            MaxDelay,
            time);

        queue.Enqueue("/s/A.cs");
        time.Advance(Debounce);
        queue.Enqueue("/s/B.cs");
        time.Advance(Debounce); // timer fires while the first flush still runs
        Assert.Single(batches);

        release.SetResult();
        await WaitUntilAsync(() => batches.Count == 2);

        Assert.Equal(["/s/A.cs"], batches[0].Files);
        Assert.Equal(["/s/B.cs"], batches[1].Files);
    }

    [Fact]
    public void Dispose_PendingPaths_AreNeverFlushed()
    {
        var time = new ManualTimeProvider();
        var batches = new List<FileSetBatch>();
        var queue = NewQueue(time, batches);

        queue.Enqueue("/s/A.cs");
        queue.Dispose();
        queue.Enqueue("/s/B.cs");
        time.Advance(MaxDelay);

        Assert.Empty(batches);
    }

    [Fact]
    public void Flush_CallbackThrows_QueueKeepsWorking()
    {
        var time = new ManualTimeProvider();
        var calls = 0;
        using var queue = new FileSetChangeQueue(
            _ =>
            {
                calls++;
                throw new InvalidOperationException("boom");
            },
            Debounce,
            MaxDelay,
            time);

        queue.Enqueue("/s/A.cs");
        time.Advance(Debounce);
        queue.Enqueue("/s/B.cs");
        time.Advance(Debounce);

        Assert.Equal(2, calls);
    }

    private static FileSetChangeQueue NewQueue(ManualTimeProvider time, List<FileSetBatch> batches)
        => new(
            batch =>
            {
                batches.Add(batch);
                return Task.CompletedTask;
            },
            Debounce,
            MaxDelay,
            time);

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("Condition not met in 5 s.");
            await Task.Delay(10);
        }
    }
}
