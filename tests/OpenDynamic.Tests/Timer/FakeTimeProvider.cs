namespace OpenDynamic.Tests.Timer;

/// <summary>
/// Controllable <see cref="TimeProvider"/> implementation for deterministic time testing.
/// Supports virtual timer scheduling triggered via <see cref="Advance(TimeSpan)"/>.
/// </summary>
public sealed class FakeTimeProvider : TimeProvider
{
    private readonly object _lock = new();
    private readonly List<FakeTimer> _timers = new();
    private DateTimeOffset _utcNow;

    public FakeTimeProvider(DateTimeOffset? initialTime = null)
    {
        _utcNow = initialTime ?? new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    }

    public override DateTimeOffset GetUtcNow() => _utcNow;

    public void Advance(TimeSpan delta)
    {
        if (delta < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(delta), "Cannot advance backward in time.");

        List<FakeTimer> timersToTrigger;
        lock (_lock)
        {
            _utcNow = _utcNow.Add(delta);
            timersToTrigger = _timers.Where(t => t.IsDue(_utcNow)).ToList();
        }

        foreach (var timer in timersToTrigger)
        {
            timer.Trigger(_utcNow);
        }
    }

    public void SetUtcNow(DateTimeOffset time)
    {
        lock (_lock)
        {
            _utcNow = time;
        }
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new FakeTimer(this, callback, state, dueTime, period);
        lock (_lock)
        {
            _timers.Add(timer);
        }
        return timer;
    }

    private void RemoveTimer(FakeTimer timer)
    {
        lock (_lock)
        {
            _timers.Remove(timer);
        }
    }

    private sealed class FakeTimer : ITimer
    {
        private readonly FakeTimeProvider _owner;
        private readonly TimerCallback _callback;
        private readonly object? _state;
        private TimeSpan _period;
        private DateTimeOffset? _nextDueUtc;
        private bool _disposed;

        public FakeTimer(FakeTimeProvider owner, TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            _owner = owner;
            _callback = callback;
            _state = state;
            _period = period;

            Change(dueTime, period);
        }

        public bool IsDue(DateTimeOffset now)
        {
            return !_disposed && _nextDueUtc.HasValue && now >= _nextDueUtc.Value;
        }

        public void Trigger(DateTimeOffset now)
        {
            if (_disposed || !_nextDueUtc.HasValue || now < _nextDueUtc.Value) return;

            if (_period > TimeSpan.Zero && _period != Timeout.InfiniteTimeSpan)
            {
                _nextDueUtc = now + _period;
            }
            else
            {
                _nextDueUtc = null;
            }

            _callback(_state);
        }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            if (_disposed) return false;

            _period = period;
            if (dueTime == Timeout.InfiniteTimeSpan || dueTime < TimeSpan.Zero)
            {
                _nextDueUtc = null;
            }
            else
            {
                _nextDueUtc = _owner.GetUtcNow() + dueTime;
            }
            return true;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                _nextDueUtc = null;
                _owner.RemoveTimer(this);
            }
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
