namespace OpenDynamic.Core.Timer;

/// <summary>
/// Operational mode for the timer controller.
/// </summary>
public enum TimerMode
{
    /// <summary>
    /// Standard countdown timer.
    /// </summary>
    Standard,

    /// <summary>
    /// Pomodoro focus/work session (default 25 minutes).
    /// </summary>
    PomodoroWork,

    /// <summary>
    /// Pomodoro rest/break session (default 5 minutes).
    /// </summary>
    PomodoroBreak
}
