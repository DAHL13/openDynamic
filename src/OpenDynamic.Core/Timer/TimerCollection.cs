namespace OpenDynamic.Core.Timer;

/// <summary>
/// Domain implementation of <see cref="ITimerCollection"/> managing up to 5 concurrent timestamp-based timers.
/// Governs primary timer selection (earliest completion), sequential alert queueing, and background expiration.
/// Adheres strictly to Golden Rule 1 (0% CPU, no busy loops) and Golden Rule 5 (isolated in Core, zero UI dependencies).
/// </summary>
public sealed class TimerCollection : ITimerCollection, IDisposable
{
    public const int DefaultMaxTimers = 5;

    private readonly TimeProvider _timeProvider;
    private readonly int _maxTimers;
    private readonly List<TimerController> _timers = new();
    private readonly Queue<TimerAlert> _alertQueue = new();
    private readonly object _lock = new();

    private ITimer? _backgroundTimer;
    private TimerAlert? _activeAlert;
    private bool _disposed;

    public int MaxTimers => _maxTimers;

    public IReadOnlyList<TimerController> Timers
    {
        get
        {
            lock (_lock)
            {
                return _timers.ToList().AsReadOnly();
            }
        }
    }

    public TimerController PrimaryTimer
    {
        get
        {
            lock (_lock)
            {
                // Rule: The timer that finishes earliest among those currently running
                var running = _timers
                    .Where(t => t.State == TimerState.Running && t.TargetEndTimeUtc.HasValue)
                    .OrderBy(t => t.TargetEndTimeUtc!.Value)
                    .FirstOrDefault();

                if (running != null) return running;

                // Secondary fallback: Paused timers ordered by remaining duration
                var paused = _timers
                    .Where(t => t.State == TimerState.Paused)
                    .OrderBy(t => t.RemainingTime)
                    .FirstOrDefault();

                if (paused != null) return paused;

                // Final fallback: First timer in collection
                return _timers.Count > 0 ? _timers[0] : CreateDefaultTimer();
            }
        }
    }

    public bool AnyRunning
    {
        get
        {
            lock (_lock)
            {
                return _timers.Any(t => t.State == TimerState.Running);
            }
        }
    }

    public TimerAlert? ActiveAlert
    {
        get
        {
            lock (_lock)
            {
                return _activeAlert;
            }
        }
    }

    public IReadOnlyCollection<TimerAlert> PendingAlerts
    {
        get
        {
            lock (_lock)
            {
                return _alertQueue.ToList().AsReadOnly();
            }
        }
    }

    public event EventHandler<TimerAlert>? AlertTriggered;
    public event EventHandler<TimerSnapshot>? Tick;
    public event EventHandler? TimersChanged;

    public TimerCollection(
        TimeProvider? timeProvider = null,
        int maxTimers = DefaultMaxTimers,
        TimeSpan? defaultStandardDuration = null,
        TimeSpan? pomodoroWorkDuration = null,
        TimeSpan? pomodoroBreakDuration = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _maxTimers = maxTimers > 0 ? maxTimers : DefaultMaxTimers;

        // Create default initial timer (guarantees backward compatibility with Phase 6)
        var defaultTimer = new TimerController(
            _timeProvider,
            defaultStandardDuration,
            pomodoroWorkDuration,
            pomodoroBreakDuration,
            id: "primary",
            label: "Temporizador");

        HookTimer(defaultTimer);
        _timers.Add(defaultTimer);
    }

    public TimerController AddTimer(string label, TimeSpan duration, TimerMode mode = TimerMode.Standard)
    {
        lock (_lock)
        {
            if (_timers.Count >= _maxTimers)
            {
                throw new InvalidOperationException($"Cannot add more than {_maxTimers} timers.");
            }

            var timer = new TimerController(
                _timeProvider,
                defaultStandardDuration: duration,
                id: Guid.NewGuid().ToString("N"),
                label: string.IsNullOrWhiteSpace(label) ? $"Temporizador {_timers.Count + 1}" : label);

            timer.SetMode(mode, duration);
            HookTimer(timer);
            _timers.Add(timer);

            ScheduleNextCompletion();
            TimersChanged?.Invoke(this, EventArgs.Empty);
            return timer;
        }
    }

    public bool TryAddTimer(string label, TimeSpan duration, out TimerController? timer, TimerMode mode = TimerMode.Standard)
    {
        lock (_lock)
        {
            if (_timers.Count >= _maxTimers)
            {
                timer = null;
                return false;
            }

            timer = AddTimer(label, duration, mode);
            return true;
        }
    }

    public bool RemoveTimer(string id)
    {
        lock (_lock)
        {
            var timer = _timers.FirstOrDefault(t => t.Id == id);
            if (timer == null) return false;

            UnhookTimer(timer);
            timer.Stop();
            _timers.Remove(timer);

            // Ensure collection is never completely empty
            if (_timers.Count == 0)
            {
                var replacement = CreateDefaultTimer();
                HookTimer(replacement);
                _timers.Add(replacement);
            }

            ScheduleNextCompletion();
            TimersChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }
    }

    public TimerController? GetTimer(string id)
    {
        lock (_lock)
        {
            return _timers.FirstOrDefault(t => t.Id == id);
        }
    }

    public TimerAlert? DismissActiveAlertAndGetNext()
    {
        TimerAlert? nextAlert = null;
        lock (_lock)
        {
            if (_alertQueue.Count > 0)
            {
                _activeAlert = _alertQueue.Dequeue();
                nextAlert = _activeAlert;
            }
            else
            {
                _activeAlert = null;
            }
        }

        if (nextAlert != null)
        {
            AlertTriggered?.Invoke(this, nextAlert);
        }

        return nextAlert;
    }

    public TimerSnapshot UpdateTick()
    {
        List<TimerController> timersCopy;
        lock (_lock)
        {
            timersCopy = _timers.ToList();
        }

        foreach (var timer in timersCopy)
        {
            if (timer.State == TimerState.Running)
            {
                timer.UpdateTick();
            }
        }

        ScheduleNextCompletion();

        var primary = PrimaryTimer;
        var snapshot = primary.CurrentSnapshot;
        Tick?.Invoke(this, snapshot);
        return snapshot;
    }

    private void HookTimer(TimerController timer)
    {
        timer.Completed += OnTimerCompleted;
        timer.Tick += OnTimerIndividualTick;
    }

    private void UnhookTimer(TimerController timer)
    {
        timer.Completed -= OnTimerCompleted;
        timer.Tick -= OnTimerIndividualTick;
    }

    private void OnTimerCompleted(object? sender, TimerSnapshot snapshot)
    {
        if (sender is not TimerController controller) return;

        TimerAlert alert;
        bool shouldTriggerImmediately = false;

        lock (_lock)
        {
            alert = new TimerAlert(controller.Id, controller.Label, controller.Mode, _timeProvider.GetUtcNow());

            if (_activeAlert == null)
            {
                _activeAlert = alert;
                shouldTriggerImmediately = true;
            }
            else
            {
                _alertQueue.Enqueue(alert);
            }
        }

        if (shouldTriggerImmediately)
        {
            AlertTriggered?.Invoke(this, alert);
        }

        ScheduleNextCompletion();
    }

    private void OnTimerIndividualTick(object? sender, TimerSnapshot snapshot)
    {
        ScheduleNextCompletion();
        Tick?.Invoke(this, PrimaryTimer.CurrentSnapshot);
    }

    private void ScheduleNextCompletion()
    {
        lock (_lock)
        {
            if (_disposed) return;

            DateTimeOffset? earliestTarget = null;
            var now = _timeProvider.GetUtcNow();

            foreach (var timer in _timers)
            {
                if (timer.State == TimerState.Running && timer.TargetEndTimeUtc.HasValue)
                {
                    var target = timer.TargetEndTimeUtc.Value;
                    if (!earliestTarget.HasValue || target < earliestTarget.Value)
                    {
                        earliestTarget = target;
                    }
                }
            }

            _backgroundTimer?.Dispose();
            _backgroundTimer = null;

            if (earliestTarget.HasValue)
            {
                var dueTime = earliestTarget.Value - now;
                if (dueTime < TimeSpan.Zero)
                {
                    dueTime = TimeSpan.Zero;
                }

                _backgroundTimer = _timeProvider.CreateTimer(
                    _ => UpdateTick(),
                    null,
                    dueTime,
                    Timeout.InfiniteTimeSpan);
            }
        }
    }

    private TimerController CreateDefaultTimer()
    {
        return new TimerController(
            _timeProvider,
            id: "primary",
            label: "Temporizador");
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;

            _backgroundTimer?.Dispose();
            _backgroundTimer = null;

            foreach (var timer in _timers)
            {
                UnhookTimer(timer);
            }
            _timers.Clear();
            _alertQueue.Clear();
        }
    }
}
