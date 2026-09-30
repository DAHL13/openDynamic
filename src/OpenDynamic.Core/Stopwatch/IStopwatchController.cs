namespace OpenDynamic.Core.Stopwatch;

/// <summary>
/// Domain interface for timestamp-based stopwatch management with laps and zero drift.
/// Uses <see cref="TimeProvider"/> for deterministic testing and timing purity (Golden Rule 5).
/// </summary>
public interface IStopwatchController
{
    /// <summary>
    /// Current immutable state snapshot.
    /// </summary>
    StopwatchSnapshot CurrentSnapshot { get; }

    /// <summary>
    /// Current lifecycle execution state (Stopped, Running, Paused).
    /// </summary>
    StopwatchState State { get; }

    /// <summary>
    /// Total elapsed time across all running intervals.
    /// </summary>
    TimeSpan ElapsedTime { get; }

    /// <summary>
    /// Elapsed duration of the current uncommitted lap.
    /// </summary>
    TimeSpan CurrentLapTime { get; }

    /// <summary>
    /// Completed laps recorded so far.
    /// </summary>
    IReadOnlyList<StopwatchLap> Laps { get; }

    /// <summary>
    /// Starts or resumes the stopwatch.
    /// </summary>
    void Start();

    /// <summary>
    /// Pauses the running stopwatch without losing accumulated time.
    /// </summary>
    void Pause();

    /// <summary>
    /// Resumes the paused stopwatch from current time.
    /// </summary>
    void Resume();

    /// <summary>
    /// Resets the stopwatch to 0 and clears all laps.
    /// </summary>
    void Reset();

    /// <summary>
    /// Records a lap split at the current elapsed time.
    /// </summary>
    /// <returns>The newly created <see cref="StopwatchLap"/>.</returns>
    StopwatchLap Lap();

    /// <summary>
    /// Recalculates elapsed time from the injected clock and fires tick event.
    /// </summary>
    StopwatchSnapshot UpdateTick();

    /// <summary>
    /// Event raised periodically on ticks while the stopwatch is running or updated.
    /// </summary>
    event EventHandler<StopwatchSnapshot>? Tick;

    /// <summary>
    /// Event raised when a new lap is recorded.
    /// </summary>
    event EventHandler<StopwatchLap>? LapRecorded;
}
