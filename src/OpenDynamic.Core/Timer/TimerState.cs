namespace OpenDynamic.Core.Timer;

/// <summary>
/// Execution state of the timer.
/// </summary>
public enum TimerState
{
    /// <summary>
    /// Timer is idle or reset.
    /// </summary>
    Stopped,

    /// <summary>
    /// Timer is actively counting down towards <c>TargetEndTimeUtc</c>.
    /// </summary>
    Running,

    /// <summary>
    /// Timer is suspended; remaining time preserved without drift.
    /// </summary>
    Paused,

    /// <summary>
    /// Target timestamp reached; countdown complete.
    /// </summary>
    Completed
}
