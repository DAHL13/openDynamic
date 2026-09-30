namespace OpenDynamic.Core.Media.Color;

/// <summary>
/// Immutable pure RGB color structure for cross-platform color representation without System.Drawing or WPF dependencies.
/// Follows Golden Rule 5 (Core purity).
/// </summary>
public readonly record struct RgbColor(byte R, byte G, byte B, byte A = 255)
{
    public static readonly RgbColor DefaultAccent = new(0x1E, 0xD7, 0x60); // Vibrant Spotify / openDynamic green
    public static readonly RgbColor Black = new(0, 0, 0);
    public static readonly RgbColor White = new(255, 255, 255);

    public static RgbColor FromRgb(byte r, byte g, byte b) => new(r, g, b, 255);
    public static RgbColor FromArgb(byte a, byte r, byte g, byte b) => new(r, g, b, a);

    /// <summary>
    /// Returns the color in #RRGGBB or #AARRGGBB hexadecimal format.
    /// </summary>
    public string ToHex(bool includeAlpha = false) =>
        includeAlpha ? $"#{A:X2}{R:X2}{G:X2}{B:X2}" : $"#{R:X2}{G:X2}{B:X2}";

    /// <summary>
    /// Converts RGB components to HSL color space.
    /// </summary>
    /// <param name="h">Hue in degrees [0, 360).</param>
    /// <param name="s">Saturation [0.0, 1.0].</param>
    /// <param name="l">Lightness [0.0, 1.0].</param>
    public void ToHsl(out double h, out double s, out double l)
    {
        double rNorm = R / 255.0;
        double gNorm = G / 255.0;
        double bNorm = B / 255.0;

        double max = Math.Max(rNorm, Math.Max(gNorm, bNorm));
        double min = Math.Min(rNorm, Math.Min(gNorm, bNorm));
        double delta = max - min;

        l = (max + min) / 2.0;

        if (delta <= 1e-6)
        {
            h = 0.0;
            s = 0.0;
            return;
        }

        s = l > 0.5 ? delta / (2.0 - max - min) : delta / (max + min);

        if (Math.Abs(max - rNorm) <= 1e-6)
        {
            h = ((gNorm - bNorm) / delta) + (gNorm < bNorm ? 6.0 : 0.0);
        }
        else if (Math.Abs(max - gNorm) <= 1e-6)
        {
            h = ((bNorm - rNorm) / delta) + 2.0;
        }
        else
        {
            h = ((rNorm - gNorm) / delta) + 4.0;
        }

        h *= 60.0;
        if (h < 0.0) h += 360.0;
        if (h >= 360.0) h = 0.0;
    }

    /// <summary>
    /// Creates an <see cref="RgbColor"/> from HSL color coordinates.
    /// </summary>
    public static RgbColor FromHsl(double h, double s, double l, byte a = 255)
    {
        h = (h % 360.0 + 360.0) % 360.0;
        s = Math.Clamp(s, 0.0, 1.0);
        l = Math.Clamp(l, 0.0, 1.0);

        if (s <= 1e-6)
        {
            byte gray = (byte)Math.Clamp(Math.Round(l * 255.0), 0, 255);
            return new RgbColor(gray, gray, gray, a);
        }

        double c = (1.0 - Math.Abs(2.0 * l - 1.0)) * s;
        double x = c * (1.0 - Math.Abs((h / 60.0) % 2.0 - 1.0));
        double m = l - c / 2.0;

        double rP, gP, bP;
        if (h < 60.0)
        {
            rP = c; gP = x; bP = 0.0;
        }
        else if (h < 120.0)
        {
            rP = x; gP = c; bP = 0.0;
        }
        else if (h < 180.0)
        {
            rP = 0.0; gP = c; bP = x;
        }
        else if (h < 240.0)
        {
            rP = 0.0; gP = x; bP = c;
        }
        else if (h < 300.0)
        {
            rP = x; gP = 0.0; bP = c;
        }
        else
        {
            rP = c; gP = 0.0; bP = x;
        }

        byte r = (byte)Math.Clamp(Math.Round((rP + m) * 255.0), 0, 255);
        byte g = (byte)Math.Clamp(Math.Round((gP + m) * 255.0), 0, 255);
        byte b = (byte)Math.Clamp(Math.Round((bP + m) * 255.0), 0, 255);

        return new RgbColor(r, g, b, a);
    }
}
