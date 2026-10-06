namespace OpenDynamic.Core.Timer;

/// <summary>
/// Domain implementation of <see cref="ITimerController"/> operating strictly via absolute target timestamps.
/// Adheres strictly to Golden Rule 1 (0% CPU, no busy loops) and Golden Rule 5 (isolated domain in Core,
/// zero UI dependencies, deterministic time via <see cref="TimeProvider"/>).
/// </summary>
public sealed class TimerController : ITimerController
{
    private readonly TimeProvider _timeProvider;
    private readonly string _id;
    private string _label;
    private TimeSpan _standardDuration;
    private TimeSpan _pomodoroWorkDuration;
    private TimeSpan _pomodoroBreakDuration;

    private TimerMode _mode = TimerMode.Standard;
    private TimerState _state = TimerState.Stopped;
    private TimeSpan _totalDuration;
    private TimeSpan _remainingTime;
    private DateTimeOffset? _targetEndTimeUtc;

    public string Id => _id;
    public string Label
    {
        get => _label;
        set => _label = value ?? string.Empty;
    }

    public TimerMode Mode => _mode;
    public TimerState State => _state;
    public TimeSpan TotalDuration => _totalDuration;
    public TimeSpan RemainingTime
    {
        get
        {
            if (_state == TimerState.Running && _targetEndTimeUtc.HasValue)
            {
                var diff = _targetEndTimeUtc.Value - _timeProvider.GetUtcNow();
                return diff > TimeSpan.Zero ? diff : TimeSpan.Zero;
            }
            return _remainingTime;
        }
    }
    public DateTimeOffset? TargetEndTimeUtc => _targetEndTimeUtc;

    public TimerSnapshot CurrentSnapshot => CreateSnapshot();

    public event EventHandler<TimerSnapshot>? Tick;
    public event EventHandler<TimerSnapshot>? Completed;

    public TimerController(
        TimeProvider? timeProvider = null,
        TimeSpan? defaultStandardDuration = null,
        TimeSpan? pomodoroWorkDuration = null,
        TimeSpan? pomodoroBreakDuration = null,
        string? id = null,
        string? label = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _id = id ?? Guid.NewGuid().ToString("N");
        _label = label ?? "Temporizador";
        _standardDuration = defaultStandardDuration ?? TimeSpan.FromMinutes(10);
        _pomodoroWorkDuration = pomodoroWorkDuration ?? TimeSpan.FromMinutes(25);
        _pomodoroBreakDuration = pomodoroBreakDuration ?? TimeSpan.FromMinutes(5);

        _totalDuration = _standardDuration;
        _remainingTime = _totalDuration;
    }

    /// <summary>
    /// Starts countdown toward target UTC timestamp based on specified or current duration.
    /// </summary>
    public void Start(TimeSpan? duration = null, TimerMode? mode = null)
    {
        if (mode.HasValue)
        {
            _mode = mode.Value;
        }

        if (duration.HasValue)
        {
            _totalDuration = duration.Value;
        }
        else if (_state == TimerState.Stopped || _state == TimerState.Completed)
        {
            _totalDuration = GetDurationForMode(_mode);
        }

        _remainingTime = _totalDuration;
        _targetEndTimeUtc = _timeProvider.GetUtcNow() + _totalDuration;
        _state = TimerState.Running;

        var snapshot = CreateSnapshot();
        Tick?.Invoke(this, snapshot);
    }

    /// <summary>
    /// Restores a running timer from persisted state while preserving its original TotalDuration.
    /// </summary>
    public void RestoreRunning(TimeSpan totalDuration, DateTimeOffset targetEndTimeUtc, TimerMode mode = TimerMode.Standard)
    {
        _mode = mode;
        var diff = targetEndTimeUtc - _timeProvider.GetUtcNow();
        if (diff <= TimeSpan.Zero)
        {
            _totalDuration = totalDuration > TimeSpan.Zero ? totalDuration : GetDurationForMode(mode);
            _remainingTime = TimeSpan.Zero;
            _targetEndTimeUtc = null;
            _state = TimerState.Completed;
            return;
        }

        _totalDuration = totalDuration >= diff ? totalDuration : diff;
        _remainingTime = diff;
        _targetEndTimeUtc = targetEndTimeUtc;
        _state = TimerState.Running;

        var snapshot = CreateSnapshot();
        Tick?.Invoke(this, snapshot);
    }

    /// <summary>
    /// Restores a paused timer from persisted state while preserving both TotalDuration and RemainingTime.
    /// </summary>
    public void RestorePaused(TimeSpan totalDuration, TimeSpan remainingTime, TimerMode mode = TimerMode.Standard)
    {
        _mode = mode;
        var safeRemaining = remainingTime > TimeSpan.Zero ? remainingTime : GetDurationForMode(mode);
        _totalDuration = totalDuration >= safeRemaining ? totalDuration : safeRemaining;
        _remainingTime = safeRemaining;
        _targetEndTimeUtc = null;
        _state = TimerState.Paused;

        var snapshot = CreateSnapshot();
        Tick?.Invoke(this, snapshot);
    }

    /// <summary>
    /// Suspends active countdown, preserving remaining time without drift.
    /// </summary>
    public void Pause()
    {
        if (_state != TimerState.Running) return;

        var now = _timeProvider.GetUtcNow();
        if (_targetEndTimeUtc.HasValue)
        {
            var diff = _targetEndTimeUtc.Value - now;
            _remainingTime = diff > TimeSpan.Zero ? diff : TimeSpan.Zero;
        }

        _targetEndTimeUtc = null;
        _state = TimerState.Paused;

        var snapshot = CreateSnapshot();
        Tick?.Invoke(this, snapshot);
    }

    /// <summary>
    /// Resumes paused countdown by establishing a new target timestamp based on preserved remaining time.
    /// </summary>
    public void Resume()
    {
        if (_state != TimerState.Paused) return;

        if (_remainingTime <= TimeSpan.Zero)
        {
            _remainingTime = _totalDuration;
        }

        _targetEndTimeUtc = _timeProvider.GetUtcNow() + _remainingTime;
        _state = TimerState.Running;

        var snapshot = CreateSnapshot();
        Tick?.Invoke(this, snapshot);
    }

    /// <summary>
    /// Stops countdown and restores initial duration for current mode.
    /// </summary>
    public void Stop()
    {
        _state = TimerState.Stopped;
        _targetEndTimeUtc = null;
        _totalDuration = GetDurationForMode(_mode);
        _remainingTime = _totalDuration;

        var snapshot = CreateSnapshot();
        Tick?.Invoke(this, snapshot);
    }

    /// <summary>
    /// Resets the timer to beginning of active mode duration.
    /// </summary>
    public void Reset()
    {
        Stop();
    }

    /// <summary>
    /// Switches operational mode and resets duration.
    /// </summary>
    public void SetMode(TimerMode mode, TimeSpan? duration = null)
    {
        _mode = mode;
        _totalDuration = duration ?? GetDurationForMode(mode);
        _remainingTime = _totalDuration;
        _targetEndTimeUtc = null;
        _state = TimerState.Stopped;

        var snapshot = CreateSnapshot();
        Tick?.Invoke(this, snapshot);
    }

    /// <summary>
    /// Adds or subtracts duration from active session.
    /// </summary>
    public void AddTime(TimeSpan additionalTime)
    {
        if (_state == TimerState.Running && _targetEndTimeUtc.HasValue)
        {
            _targetEndTimeUtc = _targetEndTimeUtc.Value + additionalTime;
            if (additionalTime > TimeSpan.Zero)
            {
                _totalDuration += additionalTime;
            }

            var liveRem = RemainingTime;
            if (_totalDuration < liveRem)
            {
                _totalDuration = liveRem;
            }
            if (_totalDuration < TimeSpan.FromSeconds(1))
            {
                _totalDuration = TimeSpan.FromSeconds(1);
            }
            UpdateTick();
        }
        else if (_state == TimerState.Paused)
        {
            _remainingTime += additionalTime;
            if (additionalTime > TimeSpan.Zero)
            {
                _totalDuration += additionalTime;
            }
            if (_remainingTime < TimeSpan.Zero)
            {
                _remainingTime = TimeSpan.Zero;
            }
            if (_remainingTime > _totalDuration)
            {
                _totalDuration = _remainingTime;
            }
            if (_totalDuration < TimeSpan.FromSeconds(1))
            {
                _totalDuration = TimeSpan.FromSeconds(1);
            }

            var snapshot = CreateSnapshot();
            Tick?.Invoke(this, snapshot);
        }
        else
        {
            _remainingTime += additionalTime;
            if (_remainingTime < TimeSpan.Zero)
            {
                _remainingTime = TimeSpan.Zero;
            }
            _totalDuration = _remainingTime;

            var snapshot = CreateSnapshot();
            Tick?.Invoke(this, snapshot);
        }
    }

    /// <summary>
    /// Computes delta between target timestamp and clock time.
    /// Fires Completed event once when target timestamp is surpassed.
    /// </summary>
    public TimerSnapshot UpdateTick()
    {
        if (_state == TimerState.Running && _targetEndTimeUtc.HasValue)
        {
            var now = _timeProvider.GetUtcNow();
            var diff = _targetEndTimeUtc.Value - now;

            if (diff <= TimeSpan.Zero)
            {
                _remainingTime = TimeSpan.Zero;
                _state = TimerState.Completed;
                _targetEndTimeUtc = null;

                var completedSnapshot = CreateSnapshot();
                Completed?.Invoke(this, completedSnapshot);
                return completedSnapshot;
            }

            _remainingTime = diff;
        }

        var tickSnapshot = CreateSnapshot();
        Tick?.Invoke(this, tickSnapshot);
        return tickSnapshot;
    }

    private TimeSpan GetDurationForMode(TimerMode mode) => mode switch
    {
        TimerMode.PomodoroWork => _pomodoroWorkDuration,
        TimerMode.PomodoroBreak => _pomodoroBreakDuration,
        _ => _standardDuration
    };

    private TimerSnapshot CreateSnapshot()
    {
        var effectiveRemaining = RemainingTime;
        double progressRatio = 0.0;
        double remainingRatio = 1.0;

        if (_totalDuration > TimeSpan.Zero)
        {
            double totalSecs = _totalDuration.TotalSeconds;
            double remSecs = Math.Clamp(effectiveRemaining.TotalSeconds, 0.0, totalSecs);
            remainingRatio = remSecs / totalSecs;
            progressRatio = 1.0 - remainingRatio;
        }

        string formatted = FormatTime(effectiveRemaining);

        return new TimerSnapshot(
            effectiveRemaining,
            _totalDuration,
            _targetEndTimeUtc,
            _mode,
            _state,
            formatted,
            progressRatio,
            remainingRatio,
            _label,
            _id);
    }

    /// <summary>
    /// Formats a duration into standard countdown display (<c>mm:ss</c> or <c>hh:mm:ss</c>).
    /// Uses ceiling rounding on sub-seconds to ensure countdown transitions naturally.
    /// </summary>
    public static string FormatTime(TimeSpan time)
    {
        if (time < TimeSpan.Zero)
        {
            time = TimeSpan.Zero;
        }

        int totalSeconds = (int)Math.Ceiling(time.TotalSeconds);
        if (totalSeconds < 0) totalSeconds = 0;

        int hours = totalSeconds / 3600;
        int minutes = (totalSeconds % 3600) / 60;
        int seconds = totalSeconds % 60;

        return hours > 0
            ? $"{hours:D2}:{minutes:D2}:{seconds:D2}"
            : $"{minutes:D2}:{seconds:D2}";
    }
}
