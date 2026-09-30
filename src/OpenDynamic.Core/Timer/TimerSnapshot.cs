namespace OpenDynamic.Core.Timer;

/// <summary>
/// Immutable snapshot representing the precise state and timestamp calculations of the timer.
/// Adheres strictly to Golden Rule 5 (isolated in Core, zero UI dependencies).
/// </summary>
/// <param name="RemainingTime">Time remaining until expiration.</param>
/// <param name="TotalDuration">Total planned duration of the current countdown session.</param>
/// <param name="TargetEndTimeUtc">Absolute target timestamp in UTC.</param>
/// <param name="Mode">Current timer or Pomodoro mode.</param>
/// <param name="State">Current lifecycle execution state.</param>
/// <param name="FormattedTime">Pre-formatted time string (<c>mm:ss</c> or <c>hh:mm:ss</c>).</param>
/// <param name="ProgressRatio">Normalized completion ratio from 0.0 (just started) to 1.0 (completed).</param>
/// <param name="RemainingRatio">Normalized remaining ratio from 1.0 (full) to 0.0 (expired).</param>
/// <param name="Label">Friendly user label for this timer.</param>
/// <param name="Id">Unique identifier of the timer.</param>
public sealed record TimerSnapshot(
    TimeSpan RemainingTime,
    TimeSpan TotalDuration,
    DateTimeOffset? TargetEndTimeUtc,
    TimerMode Mode,
    TimerState State,
    string FormattedTime,
    double ProgressRatio,
    double RemainingRatio,
    string Label = "Temporizador",
    string Id = "");
