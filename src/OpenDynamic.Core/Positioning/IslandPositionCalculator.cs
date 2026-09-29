namespace OpenDynamic.Core.Positioning;

/// <summary>
/// Pure logic calculator for overlay window placement and DPI scale compensation.
/// Computes physical screen coordinates without any dependency on WPF or Win32 APIs.
/// </summary>
public static class IslandPositionCalculator
{
    /// <summary>
    /// Default top margin from the top edge of the monitor in Device Independent Pixels (DIP).
    /// </summary>
    public const double DefaultTopMarginDip = 8.0;

    /// <summary>
    /// Calculates the physical placement (X, Y, Width, Height) of the overlay window,
    /// horizontally centered at the top of the specified monitor area with DPI compensation.
    /// </summary>
    /// <param name="monitorArea">The physical pixel bounds of the target monitor or work area.</param>
    /// <param name="dpi">The target display DPI and scale factor.</param>
    /// <param name="windowDimensions">The logical window dimensions in DIP (defaults to 640x240 DIP).</param>
    /// <param name="topMarginDip">The top margin in DIP (defaults to 8 DIP).</param>
    /// <returns>A <see cref="CalculatedWindowPlacement"/> with physical screen coordinates and dimensions.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if window dimensions are non-positive or DPI is non-positive.</exception>
    public static CalculatedWindowPlacement CalculatePlacement(
        MonitorArea monitorArea,
        DisplayDpi dpi,
        WindowDimensions? windowDimensions = null,
        double topMarginDip = DefaultTopMarginDip)
    {
        var dimensions = windowDimensions ?? WindowDimensions.DefaultIsland;

        if (dimensions.WidthDip <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(windowDimensions), "Window width must be greater than zero.");
        }

        if (dimensions.HeightDip <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(windowDimensions), "Window height must be greater than zero.");
        }

        if (dpi.DpiX <= 0 || dpi.DpiY <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dpi), "DPI values must be greater than zero.");
        }

        int physicalWidth = (int)Math.Round(dimensions.WidthDip * dpi.ScaleX, MidpointRounding.AwayFromZero);
        int physicalHeight = (int)Math.Round(dimensions.HeightDip * dpi.ScaleY, MidpointRounding.AwayFromZero);

        int physicalX = monitorArea.Left + (int)Math.Round((monitorArea.Width - physicalWidth) / 2.0, MidpointRounding.AwayFromZero);
        int physicalY = monitorArea.Top + (int)Math.Round(topMarginDip * dpi.ScaleY, MidpointRounding.AwayFromZero);

        return new CalculatedWindowPlacement(physicalX, physicalY, physicalWidth, physicalHeight);
    }
}
