namespace OpenDynamic.Core.Animation;

/// <summary>
/// Deterministically resolves the active <see cref="MotionProfile"/> according to the selected
/// <see cref="MotionMode"/> and the underlying operating system animation preference.
/// Pure Core logic with zero Windows/WPF dependencies (Golden Rule 5).
/// </summary>
public static class MotionProfileResolver
{
    /// <summary>
    /// Resolves the motion profile based on motion mode and system animation availability.
    /// </summary>
    /// <param name="mode">The motion preference selected by the user.</param>
    /// <param name="systemAnimationsEnabled">Whether the operating system has animation effects enabled.</param>
    /// <returns>The effective <see cref="MotionProfile"/>.</returns>
    public static MotionProfile Resolve(MotionMode mode, bool systemAnimationsEnabled)
    {
        return mode switch
        {
            MotionMode.Reduced => MotionProfile.Reduced,
            MotionMode.Full => MotionProfile.Full,
            MotionMode.Auto => systemAnimationsEnabled ? MotionProfile.Full : MotionProfile.Reduced,
            _ => systemAnimationsEnabled ? MotionProfile.Full : MotionProfile.Reduced
        };
    }
}
