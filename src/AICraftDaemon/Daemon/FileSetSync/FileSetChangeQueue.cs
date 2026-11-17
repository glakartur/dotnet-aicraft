namespace DotnetAICraft.Daemon.FileSetSync;

/// <summary>
/// Collects changed paths from the watchers into one batch. The batch is flushed
/// after a trailing debounce, but never later than <c>maxDelay</c> after its first
/// path, so a continuous stream of events cannot postpone the update forever.
/// Only one flush runs at a time; paths that arrive during a flush go to the next batch.
/// </summary>
internal sealed class FileSetChangeQueue : IDisposable
{
    public static readonly TimeSpan DefaultDebounce = TimeSpan.FromMilliseconds(400);
    public static readonly TimeSpan DefaultMaxDelay = TimeSpan.FromSeconds(2);

    private readonly object _lock = new();
    private readonly Func<FileSetBatch, Task> _flush;
    private readonly TimeSpan _debounce;
    private readonly TimeSpan _maxDelay;
    private readonly TimeProvider _time;
    private readonly ITimer _timer;

    private HashSet<string> _files = new(StringComparer.Ordinal);
    private HashSet<string> _directories = new(StringComparer.Ordinal);
    private DateTimeOffset? _firstPendingAt;
    private bool _flushing;
    private bool _dueWhileFlushing;
    private bool _disposed;

    /// <param name="flush">Handles one batch. It should handle its own errors; an exception is swallowed.</param>
    public FileSetChangeQueue(
        Func<FileSetBatch, Task> flush,
        TimeSpan debounce,
        TimeSpan maxDelay,
        TimeProvider? timeProvider = null)
    {
        _flush = flush;
        _debounce = debounce;
        _maxDelay = maxDelay;
        _time = timeProvider ?? TimeProvider.System;
        _timer = _time.CreateTimer(_ => OnDue(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    public void Enqueue(string filePath) => Add(isDirectory: false, filePath);

    /// <summary>Queues a directory marker; the reconciler expands it when the batch is flushed.</summary>
    public void EnqueueDirectory(string path) => Add(isDirectory: true, path);

    public void Dispose()
    {
        lock (_lock)
        {
            _disposed = true;
            _files.Clear();
            _directories.Clear();
        }

        _timer.Dispose();
    }

    private void Add(bool isDirectory, string path)
    {
        lock (_lock)
        {
            if (_disposed)
                return;

            (isDirectory ? _directories : _files).Add(path);

            var now = _time.GetUtcNow();
            _firstPendingAt ??= now;
            var dueAt = Min(now + _debounce, _firstPendingAt.Value + _maxDelay);
            var dueIn = dueAt - now;
            _timer.Change(dueIn < TimeSpan.Zero ? TimeSpan.Zero : dueIn, Timeout.InfiniteTimeSpan);
        }
    }

    private void OnDue()
    {
        FileSetBatch batch;
        lock (_lock)
        {
            if (_disposed || (_files.Count == 0 && _directories.Count == 0))
                return;

            if (_flushing)
            {
                _dueWhileFlushing = true;
                return;
            }

            batch = new FileSetBatch(_files, _directories);
            _files = new HashSet<string>(StringComparer.Ordinal);
            _directories = new HashSet<string>(StringComparer.Ordinal);
            _firstPendingAt = null;
            _flushing = true;
        }

        _ = FlushAsync(batch);
    }

    private async Task FlushAsync(FileSetBatch batch)
    {
        try
        {
            await _flush(batch);
        }
        catch
        {
            // The flush callback owns error handling; keep the queue alive.
        }

        bool flushAgain;
        lock (_lock)
        {
            _flushing = false;
            flushAgain = _dueWhileFlushing;
            _dueWhileFlushing = false;
        }

        if (flushAgain)
            OnDue();
    }

    private static DateTimeOffset Min(DateTimeOffset left, DateTimeOffset right) => left < right ? left : right;
}
