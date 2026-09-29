namespace OpenDynamic.Core.Positioning;

/// <summary>
/// Specifies the logical dimensions of the island overlay window in Device Independent Pixels (DIP).
/// </summary>
/// <param name="WidthDip">Window width in DIP (default 640 DIP).</param>
/// <param name="HeightDip">Window height in DIP (default 240 DIP).</param>
public readonly record struct WindowDimensions(double WidthDip, double HeightDip)
{
    /// <summary>
    /// Default fixed window size (640x240 DIP) sufficient to accommodate both compact and expanded states.
    /// </summary>
    public static readonly WindowDimensions DefaultIsland = new(640.0, 240.0);
}
