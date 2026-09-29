namespace OpenDynamic.Core.Timer;

/// <summary>
/// Domain interface for timestamp-based countdown and Pomodoro management.
/// Uses <see cref="TimeProvider"/> for deterministic testing and zero drift.
/// </summary>
public interface ITimerController
{
    /// <summary>
    /// Current immutable state snapshot.
    /// </summary>
    TimerSnapshot CurrentSnapshot { get; }

    /// <summary>
    /// Current timer mode (Standard, PomodoroWork, PomodoroBreak).
    /// </summary>
    TimerMode Mode { get; }

    /// <summary>
    /// Current execution state (Stopped, Running, Paused, Completed).
    /// </summary>
    TimerState State { get; }

    /// <summary>
    /// Target planned session duration.
    /// </summary>
    TimeSpan TotalDuration { get; }

    /// <summary>
    /// Remaining time until countdown reaches zero.
    /// </summary>
    TimeSpan RemainingTime { get; }

    /// <summary>
    /// Absolute UTC timestamp of completion when running.
    /// </summary>
    DateTimeOffset? TargetEndTimeUtc { get; }

    /// <summary>
    /// Starts or restarts the timer with an optional duration and mode.
    /// </summary>
    void Start(TimeSpan? duration = null, TimerMode? mode = null);

    /// <summary>
    /// Pauses the running countdown without losing remaining time.
    /// </summary>
    void Pause();

    /// <summary>
    /// Resumes the paused countdown recalculating <see cref="TargetEndTimeUtc"/> from current time.
    /// </summary>
    void Resume();

    /// <summary>
    /// Stops and resets the timer back to its initial configured state.
    /// </summary>
    void Stop();

    /// <summary>
    /// Resets the timer to the beginning of the current mode's duration without starting.
    /// </summary>
    void Reset();

    /// <summary>
    /// Switches the timer mode.
    /// </summary>
    void SetMode(TimerMode mode, TimeSpan? duration = null);

    /// <summary>
    /// Adds or subtracts duration to/from the active session.
    /// </summary>
    void AddTime(TimeSpan additionalTime);

    /// <summary>
    /// Recalculates elapsed time from the injected clock and fires lifecycle events if completed.
    /// </summary>
    TimerSnapshot UpdateTick();

    /// <summary>
    /// Event raised periodically on ticks while the timer is running.
    /// </summary>
    event EventHandler<TimerSnapshot>? Tick;

    /// <summary>
    /// Event raised exactly once when the countdown reaches zero.
    /// </summary>
    event EventHandler<TimerSnapshot>? Completed;
}
