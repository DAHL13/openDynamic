namespace OpenDynamic.Core.Media.Gestures;

/// <summary>
/// Pure state machine detecting horizontal swipe and scroll gestures with delta accumulation
/// and cooldown absorption to eliminate accidental double track skipping (e.g. touchpad inertia).
/// Strictly adheres to Golden Rule 5 (no Windows/WPF dependencies, injectable TimeProvider).
/// </summary>
public sealed class SwipeGestureDetector
{
    private readonly TimeProvider _timeProvider;
    private double _accumulatedDelta;
    private DateTimeOffset _lastTriggerTimeUtc = DateTimeOffset.MinValue;

    /// <summary>
    /// Default cooldown window to absorb touchpad inertia and prevent double skips (~400ms).
    /// </summary>
    public static readonly TimeSpan DefaultCooldown = TimeSpan.FromMilliseconds(400);

    /// <summary>
    /// Default horizontal delta threshold for mouse wheel / touchpad scroll (120 units).
    /// </summary>
    public const double DefaultWheelThreshold = 120.0;

    /// <summary>
    /// Default drag threshold for direct touch or mouse drag (~40 DIPs).
    /// </summary>
    public const double DefaultDragThreshold = 40.0;

    /// <summary>
    /// Delta accumulation threshold required to trigger an action.
    /// </summary>
    public double Threshold { get; set; }

    /// <summary>
    /// Minimum duration that must elapse after a trigger before another trigger can fire.
    /// </summary>
    public TimeSpan Cooldown { get; set; }

    /// <summary>
    /// Current accumulated delta in progress.
    /// </summary>
    public double AccumulatedDelta => _accumulatedDelta;

    /// <summary>
    /// Initializes a new instance of <see cref="SwipeGestureDetector"/>.
    /// </summary>
    /// <param name="timeProvider">Time provider for deterministic testing, or null for system time.</param>
    /// <param name="threshold">Delta threshold to trigger action.</param>
    /// <param name="cooldown">Cooldown interval after a trigger.</param>
    public SwipeGestureDetector(
        TimeProvider? timeProvider = null,
        double threshold = DefaultWheelThreshold,
        TimeSpan? cooldown = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        Threshold = threshold > 0.0 ? threshold : DefaultWheelThreshold;
        Cooldown = cooldown ?? DefaultCooldown;
    }

    /// <summary>
    /// Indicates whether the detector is currently within its cooldown window.
    /// </summary>
    public bool IsInCooldown()
    {
        var now = _timeProvider.GetUtcNow();
        return (now - _lastTriggerTimeUtc) < Cooldown;
    }

    /// <summary>
    /// Processes a mouse wheel horizontal delta (WM_MOUSEHWHEEL).
    /// Positive delta indicates wheel tilt right (Skip Next).
    /// Negative delta indicates wheel tilt left (Skip Previous).
    /// </summary>
    /// <param name="delta">Horizontal delta value.</param>
    /// <returns>Triggered gesture action.</returns>
    public SwipeGestureAction ProcessWheelDelta(double delta)
    {
        if (Math.Abs(delta) <= 1e-4)
        {
            return SwipeGestureAction.None;
        }

        var now = _timeProvider.GetUtcNow();
        if ((now - _lastTriggerTimeUtc) < Cooldown)
        {
            // Absorbing touchpad inertia: reset accumulation and suppress trigger
            _accumulatedDelta = 0.0;
            return SwipeGestureAction.None;
        }

        // If delta direction flips opposite to existing accumulation, restart accumulation
        if (_accumulatedDelta != 0.0 && Math.Sign(_accumulatedDelta) != Math.Sign(delta))
        {
            _accumulatedDelta = 0.0;
        }

        _accumulatedDelta += delta;

        if (_accumulatedDelta >= Threshold)
        {
            _lastTriggerTimeUtc = now;
            _accumulatedDelta = 0.0;
            return SwipeGestureAction.Next;
        }

        if (_accumulatedDelta <= -Threshold)
        {
            _lastTriggerTimeUtc = now;
            _accumulatedDelta = 0.0;
            return SwipeGestureAction.Previous;
        }

        return SwipeGestureAction.None;
    }

    /// <summary>
    /// Processes a direct horizontal drag delta (e.g. mouse or touch drag).
    /// Swiping left (deltaX &lt; 0) advances to Next track.
    /// Swiping right (deltaX &gt; 0) returns to Previous track.
    /// </summary>
    /// <param name="deltaX">Horizontal displacement in DIPs.</param>
    /// <returns>Triggered gesture action.</returns>
    public SwipeGestureAction ProcessDragDelta(double deltaX)
    {
        if (Math.Abs(deltaX) <= 1e-4)
        {
            return SwipeGestureAction.None;
        }

        var now = _timeProvider.GetUtcNow();
        if ((now - _lastTriggerTimeUtc) < Cooldown)
        {
            _accumulatedDelta = 0.0;
            return SwipeGestureAction.None;
        }

        // For touch/drag: dragging left (negative delta) corresponds to Next (carousel advancing)
        // Invert delta so positive accumulation = Next, negative accumulation = Previous
        double normalizedDelta = -deltaX;

        if (_accumulatedDelta != 0.0 && Math.Sign(_accumulatedDelta) != Math.Sign(normalizedDelta))
        {
            _accumulatedDelta = 0.0;
        }

        _accumulatedDelta += normalizedDelta;

        if (_accumulatedDelta >= Threshold)
        {
            _lastTriggerTimeUtc = now;
            _accumulatedDelta = 0.0;
            return SwipeGestureAction.Next;
        }

        if (_accumulatedDelta <= -Threshold)
        {
            _lastTriggerTimeUtc = now;
            _accumulatedDelta = 0.0;
            return SwipeGestureAction.Previous;
        }

        return SwipeGestureAction.None;
    }

    /// <summary>
    /// Resets the current accumulated delta without altering the cooldown state.
    /// </summary>
    public void Reset()
    {
        _accumulatedDelta = 0.0;
    }

    /// <summary>
    /// Clears both accumulated delta and cooldown state.
    /// </summary>
    public void ResetAll()
    {
        _accumulatedDelta = 0.0;
        _lastTriggerTimeUtc = DateTimeOffset.MinValue;
    }
}
