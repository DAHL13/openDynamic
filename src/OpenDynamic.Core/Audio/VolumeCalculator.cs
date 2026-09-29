namespace OpenDynamic.Core.Audio;

/// <summary>
/// Pure calculation helper for volume operations, level stepping, and icon determination.
/// Follows Golden Rule 5 (fully isolated in Core with zero UI dependencies).
/// </summary>
public static class VolumeCalculator
{
    /// <summary>
    /// Clamps any floating-point volume value into the normalized range [0.0f, 1.0f].
    /// </summary>
    public static float Normalize(float rawLevel)
    {
        if (float.IsNaN(rawLevel) || rawLevel <= 0.0f)
        {
            return 0.0f;
        }

        if (rawLevel >= 1.0f)
        {
            return 1.0f;
        }

        return rawLevel;
    }

    /// <summary>
    /// Calculates a new normalized volume level given a mouse wheel delta and step size.
    /// Standard Windows mouse wheel delta is typically 120 per notch.
    /// </summary>
    /// <param name="currentLevel">Current normalized volume level [0.0f, 1.0f].</param>
    /// <param name="scrollDelta">Mouse wheel delta (positive for up, negative for down).</param>
    /// <param name="step">Base step scalar per wheel notch (default 0.02f = 2%).</param>
    /// <returns>New clamped normalized volume level.</returns>
    public static float CalculateLevelStep(float currentLevel, int scrollDelta, float step = 0.02f)
    {
        if (scrollDelta == 0)
        {
            return Normalize(currentLevel);
        }

        // Standardize notches (at least 1 notch in sign direction)
        float notches = scrollDelta / 120.0f;
        if (MathF.Abs(notches) < 1.0f)
        {
            notches = MathF.Sign(scrollDelta);
        }

        float delta = notches * step;
        return Normalize(currentLevel + delta);
    }

    /// <summary>
    /// Converts a normalized volume level [0.0f, 1.0f] to an integer percentage [0, 100].
    /// </summary>
    public static int ToPercentage(float level)
    {
        return (int)MathF.Round(Normalize(level) * 100f);
    }

    /// <summary>
    /// Determines the appropriate speaker icon category based on mute state and volume level.
    /// </summary>
    public static VolumeIconType GetVolumeIconType(float level, bool isMuted)
    {
        if (isMuted || level <= 0.001f)
        {
            return VolumeIconType.Muted;
        }

        if (level <= 0.33f)
        {
            return VolumeIconType.Low;
        }

        if (level <= 0.66f)
        {
            return VolumeIconType.Medium;
        }

        return VolumeIconType.High;
    }
}
