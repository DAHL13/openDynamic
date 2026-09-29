namespace OpenDynamic.Core.Positioning;

/// <summary>
/// Represents display DPI and corresponding scale factors relative to standard 96 DPI (100%).
/// </summary>
/// <param name="DpiX">Horizontal DPI value (e.g. 96 for 100%, 120 for 125%, 144 for 150%, 192 for 200%).</param>
/// <param name="DpiY">Vertical DPI value.</param>
public readonly record struct DisplayDpi(double DpiX, double DpiY)
{
    /// <summary>
    /// Standard Windows baseline DPI (100% scaling).
    /// </summary>
    public const double StandardDpi = 96.0;

    /// <summary>
    /// Baseline 96x96 DPI (100% scale).
    /// </summary>
    public static readonly DisplayDpi Default = new(StandardDpi, StandardDpi);

    /// <summary>
    /// Horizontal scale factor (e.g. 1.0, 1.25, 1.5, 2.0).
    /// </summary>
    public double ScaleX => DpiX / StandardDpi;

    /// <summary>
    /// Vertical scale factor (e.g. 1.0, 1.25, 1.5, 2.0).
    /// </summary>
    public double ScaleY => DpiY / StandardDpi;

    /// <summary>
    /// Creates a <see cref="DisplayDpi"/> from a uniform scale factor (e.g. 1.25 for 125%).
    /// </summary>
    public static DisplayDpi FromScale(double scale) =>
        new(scale * StandardDpi, scale * StandardDpi);

    /// <summary>
    /// Creates a <see cref="DisplayDpi"/> from separate horizontal and vertical scale factors.
    /// </summary>
    public static DisplayDpi FromScale(double scaleX, double scaleY) =>
        new(scaleX * StandardDpi, scaleY * StandardDpi);

    /// <summary>
    /// Creates a <see cref="DisplayDpi"/> from a uniform DPI value (e.g. 120 for 125%).
    /// </summary>
    public static DisplayDpi FromDpi(double dpi) =>
        new(dpi, dpi);
}
