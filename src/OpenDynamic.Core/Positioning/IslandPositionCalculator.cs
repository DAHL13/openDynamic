namespace OpenDynamic.Core.Positioning;

/// <summary>
/// Pure logic calculator for overlay window placement and DPI scale compensation.
/// Computes physical screen coordinates without any dependency on WPF or Win32 APIs.
/// </summary>
public static class IslandPositionCalculator
{
    /// <summary>
    /// Default top margin from the top edge of the monitor in Device Independent Pixels (DIP).
    /// Default is 0.0 DIP to anchor the notch flush against the top display bezel.
    /// </summary>
    public const double DefaultTopMarginDip = 0.0;

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
        double topMarginDip = DefaultTopMarginDip,
        double offsetXDip = 0.0)
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

        int physicalX = monitorArea.Left + (int)Math.Round(((monitorArea.Width - physicalWidth) / 2.0) + (offsetXDip * dpi.ScaleX), MidpointRounding.AwayFromZero);
        int physicalY = monitorArea.Top + (int)Math.Round(topMarginDip * dpi.ScaleY, MidpointRounding.AwayFromZero);

        return new CalculatedWindowPlacement(physicalX, physicalY, physicalWidth, physicalHeight);
    }

    /// <summary>
    /// Evaluates if a device-independent pixel (DIP) coordinate within the window falls inside the resting notch sensor zone.
    /// Used for hit-testing in Hidden state to detect cursor hover over the idle notch.
    /// </summary>
    /// <param name="mouseXDip">Horizontal coordinate in DIP relative to window left.</param>
    /// <param name="mouseYDip">Vertical coordinate in DIP relative to window top.</param>
    /// <param name="windowWidthDip">Total window width in DIP (e.g. 640 DIP).</param>
    /// <param name="capsuleWidthDip">Nominal resting capsule width in DIP (e.g. 200 DIP).</param>
    /// <param name="sensorHeightDip">Height of the sensor notch in DIP (defaults to 28.0 DIP).</param>
    /// <returns>True if the coordinate is within the sensor bounds; otherwise, false.</returns>
    public static bool IsPointInRestingSensorZone(
        double mouseXDip,
        double mouseYDip,
        double windowWidthDip = 640.0,
        double capsuleWidthDip = 200.0,
        double sensorHeightDip = 28.0)
    {
        if (windowWidthDip <= 0 || capsuleWidthDip <= 0 || sensorHeightDip <= 0)
        {
            return false;
        }

        double centerDip = windowWidthDip / 2.0;
        double halfWidthDip = capsuleWidthDip / 2.0;

        double minXDip = centerDip - halfWidthDip;
        double maxXDip = centerDip + halfWidthDip;

        const double minYDip = 0.0;
        double maxYDip = sensorHeightDip;

        // Tolerances for sub-pixel boundary rounding
        bool inX = mouseXDip >= (minXDip - 2.0) && mouseXDip <= (maxXDip + 2.0);
        bool inY = mouseYDip >= (minYDip - 1.0) && mouseYDip <= (maxYDip + 1.0);

        return inX && inY;
    }
}
