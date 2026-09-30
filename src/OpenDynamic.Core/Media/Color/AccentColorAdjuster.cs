namespace OpenDynamic.Core.Media.Color;

/// <summary>
/// Adjusts candidate accent colors to ensure high legibility, contrast, and visual vibrancy
/// against the deep black (#000000) background of the Dynamic Island notch.
/// Strictly adheres to Golden Rule 5 (no System.Drawing, Win32, or WPF dependencies).
/// </summary>
public static class AccentColorAdjuster
{
    public const double DefaultMinSaturation = 0.50; // 50% minimum saturation
    public const double DefaultMinLightness = 0.45;  // 45% minimum lightness (glows on pure black)
    public const double DefaultMaxLightness = 0.80;  // 80% maximum lightness (prevents washout)

    /// <summary>
    /// Adjusts the candidate color to enforce minimum saturation and bounded lightness.
    /// Returns <paramref name="fallback"/> (or <see cref="RgbColor.DefaultAccent"/>) if candidate is null.
    /// </summary>
    /// <param name="candidate">Extracted candidate color or null.</param>
    /// <param name="fallback">Fallback color if candidate is null.</param>
    /// <param name="minSaturation">Minimum saturation allowed [0.0, 1.0].</param>
    /// <param name="minLightness">Minimum lightness allowed [0.0, 1.0].</param>
    /// <param name="maxLightness">Maximum lightness allowed [0.0, 1.0].</param>
    /// <returns>Legible and vibrant adjusted <see cref="RgbColor"/>.</returns>
    public static RgbColor AdjustColor(
        RgbColor? candidate,
        RgbColor? fallback = null,
        double minSaturation = DefaultMinSaturation,
        double minLightness = DefaultMinLightness,
        double maxLightness = DefaultMaxLightness)
    {
        if (candidate == null)
        {
            return fallback ?? RgbColor.DefaultAccent;
        }

        var color = candidate.Value;
        color.ToHsl(out double h, out double s, out double l);

        // If candidate lacks sufficient chromatic color (monochromatic, pure black or washed-out white)
        if (s < 0.15 || l < 0.05 || (s < 0.20 && l > 0.90))
        {
            return fallback ?? RgbColor.DefaultAccent;
        }

        // Enforce minimum saturation for vibrant color expression
        if (s < minSaturation)
        {
            s = minSaturation;
        }

        // Clamp lightness to ensure high contrast against black notch and prevent blinding white
        if (l < minLightness)
        {
            l = minLightness;
        }
        else if (l > maxLightness)
        {
            l = maxLightness;
        }

        return RgbColor.FromHsl(h, s, l, color.A);
    }
}
