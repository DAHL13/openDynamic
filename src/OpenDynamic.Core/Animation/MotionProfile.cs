namespace OpenDynamic.Core.Animation;

/// <summary>
/// Physical parameters and decorative animation policies for motion profiles.
/// Pure Core class with zero Windows/WPF dependencies (Golden Rule 5).
/// </summary>
public sealed record MotionProfile(
    double Stiffness,
    double Damping,
    double Mass,
    int CrossFadeOutDurationMs,
    int CrossFadeInDurationMs,
    bool AllowDecorative)
{
    /// <summary>
    /// Profile for full spring animations (k=320, c=26, m=1, zeta ~0.73, normal cross-fade, decorative enabled).
    /// </summary>
    public static MotionProfile Full { get; } = new(
        Stiffness: 320.0,
        Damping: 26.0,
        Mass: 1.0,
        CrossFadeOutDurationMs: 70,
        CrossFadeInDurationMs: 90,
        AllowDecorative: true);

    /// <summary>
    /// Profile for reduced motion (critically damped k=400, c=40, m=1, zeta = 1.0, no bounce/overshoot, faster transitions &lt;= 150ms, decorative disabled).
    /// </summary>
    public static MotionProfile Reduced { get; } = new(
        Stiffness: 400.0,
        Damping: 40.0,
        Mass: 1.0,
        CrossFadeOutDurationMs: 40,
        CrossFadeInDurationMs: 50,
        AllowDecorative: false);
}
