namespace OpenDynamic.Core.Animation;

/// <summary>
/// Specifies the animation motion mode preference.
/// Pure Core enum with zero Windows/WPF dependencies (Golden Rule 5).
/// </summary>
public enum MotionMode
{
    /// <summary>
    /// Follows the Windows operating system setting ("Animation effects").
    /// If system animations are enabled, behaves as Full; otherwise behaves as Reduced.
    /// </summary>
    Auto = 0,

    /// <summary>
    /// Suppresses bouncing (critical damping without overshoot), eliminates decorative animations,
    /// and uses short, direct transitions (&lt;= 150 ms).
    /// </summary>
    Reduced = 1,

    /// <summary>
    /// Full spring physics animations with natural bounce and subtle elasticity.
    /// </summary>
    Full = 2
}
