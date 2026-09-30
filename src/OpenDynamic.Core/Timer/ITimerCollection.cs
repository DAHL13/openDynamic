namespace OpenDynamic.Core.Timer;

/// <summary>
/// Domain interface managing a collection of up to 5 concurrent timestamp-based timers.
/// Selects the primary timer (the one ending earliest among running timers) and coordinates
/// alert sequencing for completed timers. Adheres to Golden Rule 5 (pure domain in Core).
/// </summary>
public interface ITimerCollection
{
    /// <summary>
    /// Maximum allowed concurrent timers.
    /// </summary>
    int MaxTimers { get; }

    /// <summary>
    /// Current list of active timers.
    /// </summary>
    IReadOnlyList<TimerController> Timers { get; }

    /// <summary>
    /// Primary timer to be displayed in the compact island notch.
    /// Rule: The timer that finishes earliest among those currently running.
    /// </summary>
    TimerController PrimaryTimer { get; }

    /// <summary>
    /// Indicates whether any timer is currently in the running state.
    /// </summary>
    bool AnyRunning { get; }

    /// <summary>
    /// Currently active transient completion alert, or null if no alert is being displayed.
    /// </summary>
    TimerAlert? ActiveAlert { get; }

    /// <summary>
    /// Queue of pending completion alerts awaiting presentation.
    /// </summary>
    IReadOnlyCollection<TimerAlert> PendingAlerts { get; }

    /// <summary>
    /// Adds a new timer to the collection with the specified label and duration.
    /// Throws <see cref="InvalidOperationException"/> if <see cref="MaxTimers"/> is reached.
    /// </summary>
    TimerController AddTimer(string label, TimeSpan duration, TimerMode mode = TimerMode.Standard);

    /// <summary>
    /// Attempts to add a new timer to the collection.
    /// </summary>
    bool TryAddTimer(string label, TimeSpan duration, out TimerController? timer, TimerMode mode = TimerMode.Standard);

    /// <summary>
    /// Removes a timer by its identifier.
    /// </summary>
    bool RemoveTimer(string id);

    /// <summary>
    /// Retrieves a timer by its identifier.
    /// </summary>
    TimerController? GetTimer(string id);

    /// <summary>
    /// Dismisses the currently active alert and promotes the next pending alert in the queue.
    /// </summary>
    TimerAlert? DismissActiveAlertAndGetNext();

    /// <summary>
    /// Updates elapsed time on all timers and returns the primary snapshot.
    /// </summary>
    TimerSnapshot UpdateTick();

    /// <summary>
    /// Event raised when a timer completes and an alert is ready for presentation.
    /// </summary>
    event EventHandler<TimerAlert>? AlertTriggered;

    /// <summary>
    /// Event raised when any timer ticks or updates.
    /// </summary>
    event EventHandler<TimerSnapshot>? Tick;

    /// <summary>
    /// Event raised when the timer collection is modified (timer added, removed, or label changed).
    /// </summary>
    event EventHandler? TimersChanged;
}
