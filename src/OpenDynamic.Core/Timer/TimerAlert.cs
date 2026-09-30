namespace OpenDynamic.Core.Timer;

/// <summary>
/// Domain record representing a completed timer alert for notification sequencing.
/// Adheres strictly to Golden Rule 5 (isolated in Core, zero UI dependencies).
/// </summary>
/// <param name="TimerId">Identifier of the completed timer.</param>
/// <param name="Label">Label of the timer (e.g. "Huevos", "Pomodoro", "Pasta").</param>
/// <param name="Mode">Timer mode at time of completion.</param>
/// <param name="CompletedAtUtc">Timestamp of completion in UTC.</param>
public sealed record TimerAlert(
    string TimerId,
    string Label,
    TimerMode Mode,
    DateTimeOffset CompletedAtUtc);
