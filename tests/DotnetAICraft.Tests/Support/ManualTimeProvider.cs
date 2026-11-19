namespace DotnetAICraft.Tests.Support;

/// <summary>
/// A <see cref="TimeProvider"/> whose clock moves only on <see cref="Advance"/>.
/// Due timers fire synchronously inside <see cref="Advance"/>. Periodic timers
/// are not supported (one-shot only).
/// </summary>
internal sealed class ManualTimeProvider : TimeProvider
{
    private readonly object _lock = new();
    private readonly List<ManualTimer> _timers = [];
    private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow()
    {
        lock (_lock)
            return _now;
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state);
        lock (_lock)
            _timers.Add(timer);
        timer.Change(dueTime, period);
        return timer;
    }

    public void Advance(TimeSpan by)
    {
        lock (_lock)
            _now += by;

        while (true)
        {
            ManualTimer? due;
            lock (_lock)
            {
                due = _timers
                    .Where(t => t.DueAt is { } at && at <= _now)
                    .OrderBy(t => t.DueAt)
                    .FirstOrDefault();
                if (due is null)
                    return;
                due.DueAt = null;
            }

            due.Fire();
        }
    }

    private void Remove(ManualTimer timer)
    {
        lock (_lock)
            _timers.Remove(timer);
    }

    private sealed class ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state) : ITimer
    {
        public DateTimeOffset? DueAt { get; set; }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (owner._lock)
                DueAt = dueTime == Timeout.InfiniteTimeSpan ? null : owner._now + dueTime;
            return true;
        }

        public void Fire() => callback(state);

        public void Dispose() => owner.Remove(this);

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
