namespace OpenDynamic.Core.Clock;

/// <summary>
/// Domain service that calculates the delay required until the next alignment boundary
/// (next minute :00 boundary by default, or next second boundary when seconds are enabled).
/// Enforces a minimum 50 ms delay and supports reactive recalculation on time/timezone changes.
/// Utilizes <see cref="TimeProvider"/> for deterministic and testable scheduling.
/// </summary>
public sealed class ClockTickScheduler
{
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Minimum scheduling delay in milliseconds to prevent redundant or jittery timer firing.
    /// </summary>
    public const int MinimumDelayMilliseconds = 50;

    /// <summary>
    /// Initializes a new instance of <see cref="ClockTickScheduler"/>.
    /// </summary>
    /// <param name="timeProvider">Optional time provider abstraction; defaults to <see cref="TimeProvider.System"/>.</param>
    public ClockTickScheduler(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Calculates the delay until the next tick boundary based on the current local time.
    /// </summary>
    /// <param name="showSeconds">If true, aligns to the next second boundary; otherwise aligns to the next minute :00 boundary.</param>
    /// <returns>The calculated <see cref="TimeSpan"/> delay (at least 50 ms).</returns>
    public TimeSpan GetDelayUntilNextTick(bool showSeconds = false)
    {
        var now = _timeProvider.GetLocalNow();
        return CalculateDelay(now, showSeconds);
    }

    /// <summary>
    /// Pure calculation of remaining time until the next alignment boundary.
    /// </summary>
    /// <param name="now">The current timestamp to evaluate from.</param>
    /// <param name="showSeconds">If true, calculates remaining time to next second; otherwise to next minute boundary.</param>
    /// <returns>A delay clamped to at least <see cref="MinimumDelayMilliseconds"/>.</returns>
    public static TimeSpan CalculateDelay(DateTimeOffset now, bool showSeconds = false)
    {
        int remainingMs;

        if (showSeconds)
        {
            // Align to the next integer second boundary: (1000 - current millisecond)
            int millis = now.Millisecond;
            remainingMs = 1000 - millis;
        }
        else
        {
            // Align to the next integer minute boundary: remaining seconds + remaining milliseconds
            int seconds = now.Second;
            int millis = now.Millisecond;
            int remainingSeconds = 59 - seconds;
            int subSecondRemainingMs = 1000 - millis;
            remainingMs = (remainingSeconds * 1000) + subSecondRemainingMs;
        }

        // Clamp to minimum 50 ms to satisfy Golden Rules and specifications
        if (remainingMs < MinimumDelayMilliseconds)
        {
            remainingMs = MinimumDelayMilliseconds;
        }

        return TimeSpan.FromMilliseconds(remainingMs);
    }
}
