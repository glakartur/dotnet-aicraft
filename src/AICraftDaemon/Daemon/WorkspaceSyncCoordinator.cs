using DotnetAICraft.Daemon.FileSetSync;

namespace DotnetAICraft.Daemon;

internal enum WatcherKind
{
    /// <summary>The <c>*.cs</c> watcher.</summary>
    Source,

    /// <summary>The unfiltered watcher: metadata files and directory events.</summary>
    Tree,
}

/// <summary>
/// Keeps the in-memory solution in step with the disk. Owns the two file
/// watchers (start, error handling, recreate), the batched file-set queue and
/// the debounced full reload with its retry budget. It never touches the
/// solution or the workspace: <see cref="DaemonServer"/> owns both and the
/// solution lock, and supplies the batch handler and the load starter.
/// </summary>
internal sealed class WorkspaceSyncCoordinator
{
    private const int WatcherBufferSize = 64 * 1024; // honored on Windows only; Linux uses fs.inotify.max_queued_events

    private readonly string _watchRoot;
    private readonly Func<FileSetBatch, Task> _applyBatch;
    private readonly Action<Action<bool>> _startBackgroundLoad;
    private readonly Func<bool> _isAcceptingWork;
    private readonly Action<string> _log;

    private readonly object _fullReloadLock = new();
    private CancellationTokenSource? _fullReloadDebounceCts;
    private DateTime? _fullReloadPendingSince; // guarded by _fullReloadLock
    private int _fullReloadRetryCount; // guarded by _fullReloadLock

    // Guarded by _watcherLock: a watcher error can replace a watcher at any time.
    private readonly object _watcherLock = new();
    private FileSystemWatcher? _watcher;
    private FileSystemWatcher? _metadataWatcher;
    private bool _watchersStopped; // guarded by _watcherLock
    private readonly HashSet<WatcherKind> _watcherRetryPending = []; // guarded by _watcherLock

    private FileSetChangeQueue? _fileSetQueue;
    // Paths queued again after a read error; touched only by the queue's flush callback.
    private HashSet<string> _retriedPaths = new(StringComparer.Ordinal);

    public WorkspaceSyncCoordinator(
        string watchRoot,
        Func<FileSetBatch, Task> applyBatch,
        Action<Action<bool>> startBackgroundLoad,
        Func<bool> isAcceptingWork,
        Action<string> log)
    {
        _watchRoot = watchRoot;
        _applyBatch = applyBatch;
        _startBackgroundLoad = startBackgroundLoad;
        _isAcceptingWork = isAcceptingWork;
        _log = log;
    }

    /// <summary>Delay before a scheduled full reload starts; coalesces bursts of triggers.</summary>
    internal TimeSpan FullReloadDebounce { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>Upper bound for postponing a scheduled full reload by new triggers.</summary>
    internal TimeSpan FullReloadMaxDelay { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Delay before a failed watcher recreate is tried again.</summary>
    internal TimeSpan WatcherRecreateRetryDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Base delay of the exponential backoff between automatic full reload retries.</summary>
    internal TimeSpan FullReloadRetryBaseDelay { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>Retries after a failed automatic full reload, before the daemon gives up.</summary>
    internal const int MaxFullReloadRetries = 3;

    /// <summary>Trailing debounce of the file-set queue.</summary>
    internal TimeSpan FileSetDebounce { get; set; } = FileSetChangeQueue.DefaultDebounce;

    /// <summary>Number of watcher recreate retries that wait for their delay.</summary>
    internal int PendingWatcherRetryCount
    {
        get { lock (_watcherLock) return _watcherRetryPending.Count; }
    }

    internal int FullReloadRetryCount
    {
        get { lock (_fullReloadLock) return _fullReloadRetryCount; }
    }

    /// <summary>Starts the file-set queue and both watchers.</summary>
    internal void Start()
    {
        StartFileSetQueue();
        StartFileWatcher();
        StartMetadataWatcher();
    }

    /// <summary>
    /// Stops the watchers, waits for a running batch and cancels a pending
    /// reload, so nothing touches the solution lock after disposal.
    /// </summary>
    internal async ValueTask StopAsync()
    {
        FileSystemWatcher? watcher, metadataWatcher;
        lock (_watcherLock)
        {
            _watchersStopped = true;
            (watcher, metadataWatcher) = (_watcher, _metadataWatcher);
            (_watcher, _metadataWatcher) = (null, null);
        }
        watcher?.Dispose();
        metadataWatcher?.Dispose();
        if (_fileSetQueue is not null)
            await _fileSetQueue.DisposeAsync();
        lock (_fullReloadLock)
        {
            _fullReloadDebounceCts?.Cancel();
            _fullReloadDebounceCts?.Dispose();
            _fullReloadDebounceCts = null;
        }
    }

    internal FileSystemWatcher? GetWatcher(WatcherKind kind)
    {
        lock (_watcherLock)
            return kind == WatcherKind.Source ? _watcher : _metadataWatcher;
    }

    private void StartFileSetQueue()
    {
        _fileSetQueue?.Dispose();
        _fileSetQueue = new FileSetChangeQueue(
            _applyBatch,
            FileSetDebounce,
            FileSetChangeQueue.DefaultMaxDelay);
    }

    private void StartFileWatcher() => StartWatcher(WatcherKind.Source);

    private void StartMetadataWatcher() => StartWatcher(WatcherKind.Tree);

    /// <summary>
    /// Creates, starts and registers a watcher. Throws when the watcher cannot
    /// start; a watcher that stops while starting is disposed, never registered.
    /// </summary>
    private void StartWatcher(WatcherKind kind)
    {
        var watcher = kind == WatcherKind.Source ? CreateSourceWatcher() : CreateTreeWatcher();
        // The sender is passed on: an error raised while this watcher starts
        // (inside EnableRaisingEvents, e.g. an unreadable subdirectory on Linux)
        // must not recreate it again, or the recreate recurses without end.
        watcher.Error += (sender, e) => HandleWatcherError(kind, sender as FileSystemWatcher, e.GetException());
        try
        {
            watcher.EnableRaisingEvents = true;
            if (!watcher.EnableRaisingEvents)
                throw new IOException($"The {kind} watcher stopped while starting.");
        }
        catch
        {
            watcher.Dispose();
            throw;
        }

        FileSystemWatcher? previous;
        lock (_watcherLock)
        {
            if (_watchersStopped)
            {
                previous = watcher; // the daemon is shutting down: never register it
            }
            else if (kind == WatcherKind.Source)
            {
                previous = _watcher;
                _watcher = watcher;
            }
            else
            {
                previous = _metadataWatcher;
                _metadataWatcher = watcher;
            }
        }

        previous?.Dispose();
    }

    private FileSystemWatcher CreateSourceWatcher()
    {
        var watcher = new FileSystemWatcher(_watchRoot, "*.cs")
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName,
            InternalBufferSize = WatcherBufferSize,
        };

        watcher.Changed += (_, e) => EnqueueSourceFile(e.FullPath);
        watcher.Created += (_, e) => EnqueueSourceFile(e.FullPath);
        watcher.Deleted += (_, e) => EnqueueSourceFile(e.FullPath);
        watcher.Renamed += (_, e) =>
        {
            EnqueueSourceFile(e.OldFullPath);
            EnqueueSourceFile(e.FullPath);
        };
        return watcher;
    }

    private FileSystemWatcher CreateTreeWatcher()
    {
        var watcher = new FileSystemWatcher(_watchRoot)
        {
            IncludeSubdirectories = true,
            // DirectoryName is required to get rename/delete events for directories.
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName,
            InternalBufferSize = WatcherBufferSize,
        };

        watcher.Changed += (_, e) => OnTreeChange(e.FullPath, structural: false);
        watcher.Created += (_, e) => OnTreeChange(e.FullPath, structural: true);
        watcher.Deleted += (_, e) => OnTreeChange(e.FullPath, structural: true);
        watcher.Renamed += (_, e) =>
        {
            OnTreeChange(e.OldFullPath, structural: true);
            OnTreeChange(e.FullPath, structural: true);
        };
        return watcher;
    }

    private void EnqueueSourceFile(string path)
    {
        if (FileSetPaths.IsCSharpFile(path) && !FileSetPaths.IsIgnored(_watchRoot, path))
            _fileSetQueue?.Enqueue(path);
    }

    /// <param name="structural">True for Created/Deleted/Renamed, false for Changed.</param>
    private void OnTreeChange(string path, bool structural)
    {
        var root = _watchRoot;
        // Drop build output, .git and other hidden directories before any work,
        // so builds and git operations do not cause batches or reloads.
        if (FileSetPaths.IsIgnored(root, path))
            return;

        if (IsMetadataReloadCandidate(path))
        {
            ScheduleFullReload($"metadata changed: {Path.GetRelativePath(root, path)}");
            return;
        }

        // Also covers renames such as Foo.tmp -> Foo.cs that the *.cs watcher can miss.
        if (FileSetPaths.IsCSharpFile(path))
        {
            _fileSetQueue?.Enqueue(path);
            return;
        }

        // An event does not say if the path is a directory, so every other
        // structural change becomes a directory marker. Expanding a marker for a
        // plain file (notes.json, an editor temp file) gives an empty result.
        if (structural && !FileSetPaths.IsIgnoredDirectory(root, path))
            _fileSetQueue?.EnqueueDirectory(path);
    }

    private static readonly HashSet<string> MetadataFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props",
    };

    private static readonly HashSet<string> MetadataExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".sln", ".slnx", ".csproj", ".vbproj", ".fsproj",
    };

    internal static bool IsMetadataReloadCandidate(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        return MetadataFileNames.Contains(Path.GetFileName(path)) ||
               MetadataExtensions.Contains(Path.GetExtension(path));
    }

    /// <summary>
    /// A watcher error can mean lost events (buffer overflow) or a dead watcher.
    /// Reload when the sender is the registered watcher, because the in-memory
    /// solution may have missed changes; errors of other senders are only logged.
    /// Recreate the watcher only when the registered watcher itself has stopped;
    /// errors from a watcher that is still running, or from a watcher that is
    /// still starting, do not recreate it.
    /// </summary>
    internal void HandleWatcherError(WatcherKind kind, Exception? error)
        => HandleWatcherError(kind, GetWatcher(kind), error);

    internal void HandleWatcherError(WatcherKind kind, FileSystemWatcher? sender, Exception? error)
    {
        _log($"Watcher error ({kind}): {error?.Message}");

        // A watcher that is still starting, or was already replaced, is not the
        // registered one: its error only gets logged. Its owner covers the reload.
        if (!IsRegisteredWatcher(kind, sender))
            return;

        if (IsRegisteredWatcherStopped(kind, sender))
            RecreateWatcher(kind);

        ScheduleFullReload($"watcher error ({kind}): {error?.GetType().Name}");
    }

    private bool IsRegisteredWatcher(WatcherKind kind, FileSystemWatcher? sender)
    {
        lock (_watcherLock)
            return ReferenceEquals(sender, kind == WatcherKind.Source ? _watcher : _metadataWatcher);
    }

    private bool IsRegisteredWatcherStopped(WatcherKind kind, FileSystemWatcher? sender)
    {
        lock (_watcherLock)
        {
            var registered = kind == WatcherKind.Source ? _watcher : _metadataWatcher;
            if (!ReferenceEquals(sender, registered))
                return false; // a watcher that is starting, or one already replaced

            return registered is null || !registered.EnableRaisingEvents;
        }
    }

    private void RecreateWatcher(WatcherKind kind)
    {
        if (!_isAcceptingWork())
            return;

        try
        {
            StartWatcher(kind);
            _log($"Watcher recreated ({kind})");
        }
        catch (Exception ex)
        {
            _log($"Watcher recreate failed ({kind}): {ex.Message}; retry in {WatcherRecreateRetryDelay.TotalSeconds:0} s");
            ScheduleWatcherRecreateRetry(kind);
        }
    }

    /// <summary>One pending retry per watcher kind; a retry that succeeds also reloads.</summary>
    private void ScheduleWatcherRecreateRetry(WatcherKind kind)
    {
        lock (_watcherLock)
        {
            if (_watchersStopped || !_watcherRetryPending.Add(kind))
                return;
        }

        _ = Task.Run(async () =>
        {
            await Task.Delay(WatcherRecreateRetryDelay);
            lock (_watcherLock)
                _watcherRetryPending.Remove(kind);

            if (!IsRegisteredWatcherStopped(kind, GetWatcher(kind)))
                return;

            RecreateWatcher(kind);
            if (GetWatcher(kind) is { EnableRaisingEvents: true })
                ScheduleFullReload($"watcher recreated ({kind})");
        });
    }

    /// <summary>
    /// Debounced full reload. Always goes through StartBackgroundLoad, which
    /// chains after an in-flight load, so a request that arrives during a reload
    /// is never lost.
    /// </summary>
    internal void ScheduleFullReload(string reason)
    {
        CancellationToken token;
        lock (_fullReloadLock)
        {
            if (!_isAcceptingWork())
                return;

            _log($"Full reload scheduled: {reason}");

            // Trailing debounce with a cap: a steady stream of triggers must not
            // postpone the reload forever. Past the cap, the pending reload stands.
            var now = DateTime.UtcNow;
            if (_fullReloadPendingSince is { } since && now - since >= FullReloadMaxDelay)
                return;

            _fullReloadPendingSince ??= now;
            _fullReloadDebounceCts?.Cancel();
            _fullReloadDebounceCts?.Dispose();
            _fullReloadDebounceCts = new CancellationTokenSource();
            token = _fullReloadDebounceCts.Token;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(FullReloadDebounce, token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            lock (_fullReloadLock)
            {
                if (token.IsCancellationRequested)
                    return;
                _fullReloadPendingSince = null;
            }

            _startBackgroundLoad(OnAutomaticReloadCompleted);
        }, CancellationToken.None);
    }

    /// <summary>
    /// Result of a load that ScheduleFullReload started. A failure retries through
    /// ScheduleFullReload after an exponential backoff with jitter; a success
    /// resets the counter.
    /// </summary>
    private void OnAutomaticReloadCompleted(bool loaded)
    {
        int attempt;
        lock (_fullReloadLock)
        {
            if (loaded)
            {
                _fullReloadRetryCount = 0;
                return;
            }

            if (_fullReloadRetryCount >= MaxFullReloadRetries)
            {
                _fullReloadRetryCount = 0;
                _log($"Automatic full reload failed; giving up after {MaxFullReloadRetries} retries. Run 'server reload' to try again.");
                return;
            }

            attempt = ++_fullReloadRetryCount;
        }

        var backoff = FullReloadRetryBaseDelay * Math.Pow(2, attempt - 1);
        var delay = backoff * (0.5 + Random.Shared.NextDouble()); // jitter: 50%..150%
        _log($"Automatic full reload failed; retry {attempt}/{MaxFullReloadRetries} in {delay.TotalSeconds:0.0} s");

        _ = Task.Run(async () =>
        {
            await Task.Delay(delay).ConfigureAwait(false);
            ScheduleFullReload($"retry {attempt}/{MaxFullReloadRetries} after failed reload");
        });
    }


    // Called only from the queue's flush callback; the queue runs one flush at a time.
    internal void RequeueForRetry(IReadOnlyList<string> retry)
    {
        var next = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in retry)
        {
            if (_retriedPaths.Contains(path))
            {
                _log($"File update error: cannot read {path}");
                continue;
            }

            _log($"File update queued for retry: {path}");
            next.Add(path);
            _fileSetQueue?.Enqueue(path);
        }

        _retriedPaths = next;
    }
}
