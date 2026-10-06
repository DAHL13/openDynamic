using System.Collections.ObjectModel;

namespace OpenDynamic.Core.Stopwatch;

/// <summary>
/// Domain implementation of <see cref="IStopwatchController"/> operating strictly via absolute timestamps.
/// Never accumulates ticks: always <c>now - start + accumulated</c>.
/// Adheres strictly to Golden Rule 1 (0% CPU, no busy loops) and Golden Rule 5 (isolated in Core,
/// zero UI dependencies, deterministic time via <see cref="TimeProvider"/>).
/// </summary>
public sealed class StopwatchController : IStopwatchController
{
    public const int MaxLaps = 999;

    private readonly TimeProvider _timeProvider;
    private readonly List<StopwatchLap> _laps = new();
    private ReadOnlyCollection<StopwatchLap> _cachedReadOnlyLaps;

    private StopwatchState _state = StopwatchState.Stopped;
    private DateTimeOffset? _sessionStartUtc;
    private TimeSpan _accumulated = TimeSpan.Zero;
    private TimeSpan _lastLapSplit = TimeSpan.Zero;
    private int _totalLapsRecorded;

    public StopwatchState State => _state;
    public IReadOnlyList<StopwatchLap> Laps => _cachedReadOnlyLaps;

    public TimeSpan ElapsedTime
    {
        get
        {
            if (_state == StopwatchState.Running && _sessionStartUtc.HasValue)
            {
                var now = _timeProvider.GetUtcNow();
                var delta = now - _sessionStartUtc.Value;
                return _accumulated + (delta > TimeSpan.Zero ? delta : TimeSpan.Zero);
            }
            return _accumulated;
        }
    }

    public TimeSpan CurrentLapTime
    {
        get
        {
            var total = ElapsedTime;
            var lapTime = total - _lastLapSplit;
            return lapTime >= TimeSpan.Zero ? lapTime : TimeSpan.Zero;
        }
    }

    public StopwatchSnapshot CurrentSnapshot => CreateSnapshot();

    public event EventHandler<StopwatchSnapshot>? Tick;
    public event EventHandler<StopwatchLap>? LapRecorded;

    public StopwatchController(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _cachedReadOnlyLaps = _laps.AsReadOnly();
    }

    /// <summary>
    /// Starts or resumes the stopwatch.
    /// </summary>
    public void Start()
    {
        if (_state == StopwatchState.Running) return;

        if (_state == StopwatchState.Paused)
        {
            Resume();
            return;
        }

        // Start from stopped state
        _accumulated = TimeSpan.Zero;
        _lastLapSplit = TimeSpan.Zero;
        _laps.Clear();
        _totalLapsRecorded = 0;
        _cachedReadOnlyLaps = _laps.AsReadOnly();
        _sessionStartUtc = _timeProvider.GetUtcNow();
        _state = StopwatchState.Running;

        var snapshot = CreateSnapshot();
        Tick?.Invoke(this, snapshot);
    }

    /// <summary>
    /// Pauses the running stopwatch without losing accumulated time.
    /// </summary>
    public void Pause()
    {
        if (_state != StopwatchState.Running) return;

        var now = _timeProvider.GetUtcNow();
        if (_sessionStartUtc.HasValue)
        {
            var delta = now - _sessionStartUtc.Value;
            _accumulated += delta > TimeSpan.Zero ? delta : TimeSpan.Zero;
        }

        _sessionStartUtc = null;
        _state = StopwatchState.Paused;

        var snapshot = CreateSnapshot();
        Tick?.Invoke(this, snapshot);
    }

    /// <summary>
    /// Resumes the paused stopwatch from current time.
    /// </summary>
    public void Resume()
    {
        if (_state != StopwatchState.Paused) return;

        _sessionStartUtc = _timeProvider.GetUtcNow();
        _state = StopwatchState.Running;

        var snapshot = CreateSnapshot();
        Tick?.Invoke(this, snapshot);
    }

    /// <summary>
    /// Resets the stopwatch to 0 and clears all laps.
    /// </summary>
    public void Reset()
    {
        _accumulated = TimeSpan.Zero;
        _sessionStartUtc = null;
        _lastLapSplit = TimeSpan.Zero;
        _laps.Clear();
        _totalLapsRecorded = 0;
        _cachedReadOnlyLaps = _laps.AsReadOnly();
        _state = StopwatchState.Stopped;

        var snapshot = CreateSnapshot();
        Tick?.Invoke(this, snapshot);
    }

    /// <summary>
    /// Records a lap split at the current elapsed time.
    /// </summary>
    public StopwatchLap Lap()
    {
        if (_state == StopwatchState.Stopped)
        {
            // If stopped at zero, return zero lap or ignore
            var zeroLap = new StopwatchLap(1, TimeSpan.Zero, TimeSpan.Zero, "00:00.00", "00:00.00");
            return zeroLap;
        }

        var total = ElapsedTime;
        var lapDuration = total - _lastLapSplit;
        if (lapDuration < TimeSpan.Zero)
        {
            lapDuration = TimeSpan.Zero;
        }

        _lastLapSplit = total;
        int lapNumber = ++_totalLapsRecorded;

        var lap = new StopwatchLap(
            lapNumber,
            lapDuration,
            total,
            FormatPrecise(lapDuration),
            FormatPrecise(total));

        if (_laps.Count >= MaxLaps)
        {
            _laps.RemoveAt(0);
        }

        _laps.Add(lap);
        _cachedReadOnlyLaps = _laps.ToList().AsReadOnly();
        LapRecorded?.Invoke(this, lap);

        var snapshot = CreateSnapshot();
        Tick?.Invoke(this, snapshot);

        return lap;
    }

    /// <summary>
    /// Recalculates elapsed time from the injected clock and fires tick event.
    /// </summary>
    public StopwatchSnapshot UpdateTick()
    {
        var snapshot = CreateSnapshot();
        Tick?.Invoke(this, snapshot);
        return snapshot;
    }

    private StopwatchSnapshot CreateSnapshot()
    {
        var elapsed = ElapsedTime;
        var currentLap = elapsed - _lastLapSplit;
        if (currentLap < TimeSpan.Zero)
        {
            currentLap = TimeSpan.Zero;
        }

        return new StopwatchSnapshot(
            elapsed,
            currentLap,
            _cachedReadOnlyLaps,
            _state,
            FormatElapsed(elapsed),
            FormatPrecise(elapsed),
            FormatPrecise(currentLap));
    }

    /// <summary>
    /// Formats a duration into <c>mm:ss.cc</c> when under 1 hour, or <c>h:mm:ss</c> when 1 hour or more.
    /// </summary>
    public static string FormatElapsed(TimeSpan time)
    {
        if (time < TimeSpan.Zero) time = TimeSpan.Zero;
        int totalSeconds = (int)time.TotalSeconds;
        int hours = totalSeconds / 3600;

        return hours > 0
            ? FormatStandard(time)
            : FormatPrecise(time);
    }

    /// <summary>
    /// Formats a duration strictly into precise string with centiseconds (<c>mm:ss.cc</c> or <c>h:mm:ss.cc</c>).
    /// </summary>
    public static string FormatPrecise(TimeSpan time)
    {
        if (time < TimeSpan.Zero) time = TimeSpan.Zero;
        int centiseconds = time.Milliseconds / 10;
        int totalSeconds = (int)time.TotalSeconds;
        int hours = totalSeconds / 3600;
        int minutes = (totalSeconds % 3600) / 60;
        int seconds = totalSeconds % 60;

        return hours > 0
            ? $"{hours}:{minutes:D2}:{seconds:D2}.{centiseconds:D2}"
            : $"{minutes:D2}:{seconds:D2}.{centiseconds:D2}";
    }

    /// <summary>
    /// Formats a duration into standard string (<c>mm:ss</c> or <c>h:mm:ss</c>).
    /// </summary>
    public static string FormatStandard(TimeSpan time)
    {
        if (time < TimeSpan.Zero) time = TimeSpan.Zero;
        int totalSeconds = (int)time.TotalSeconds;
        int hours = totalSeconds / 3600;
        int minutes = (totalSeconds % 3600) / 60;
        int seconds = totalSeconds % 60;

        return hours > 0
            ? $"{hours}:{minutes:D2}:{seconds:D2}"
            : $"{minutes:D2}:{seconds:D2}";
    }
}
