using OpenDynamic.Core.Positioning;
using Xunit;

namespace OpenDynamic.Tests.Positioning;

public class IslandPositionCalculatorTests
{
    [Theory]
    [InlineData(1.0, 96.0, 640, 240, 640, 0)]
    [InlineData(1.25, 120.0, 800, 300, 560, 0)]
    [InlineData(1.50, 144.0, 960, 360, 480, 0)]
    [InlineData(2.0, 192.0, 1280, 480, 320, 0)]
    public void CalculatePlacement_On1080pMonitor_CalculatesCorrectPhysicalCoordinatesForStandardScales(
        double expectedScale,
        double dpiValue,
        int expectedWidth,
        int expectedHeight,
        int expectedX,
        int expectedY)
    {
        // Monitor: 1920x1080 at origin (0, 0)
        var monitor = new MonitorArea(0, 0, 1920, 1080);
        var dpi = DisplayDpi.FromDpi(dpiValue);

        Assert.Equal(expectedScale, dpi.ScaleX);
        Assert.Equal(expectedScale, dpi.ScaleY);

        var placement = IslandPositionCalculator.CalculatePlacement(monitor, dpi);

        Assert.Equal(expectedWidth, placement.Width);
        Assert.Equal(expectedHeight, placement.Height);
        Assert.Equal(expectedX, placement.X);
        Assert.Equal(expectedY, placement.Y);
    }

    [Fact]
    public void CalculatePlacement_At100PercentScale_CentersProperlyOn4KMonitor()
    {
        var monitor = new MonitorArea(0, 0, 3840, 2160);
        var dpi = DisplayDpi.FromScale(1.0);

        var placement = IslandPositionCalculator.CalculatePlacement(monitor, dpi);

        Assert.Equal(640, placement.Width);
        Assert.Equal(240, placement.Height);
        Assert.Equal((3840 - 640) / 2, placement.X); // 1600
        Assert.Equal(0, placement.Y);
    }

    [Fact]
    public void CalculatePlacement_At200PercentScale_CentersProperlyOn4KMonitor()
    {
        var monitor = new MonitorArea(0, 0, 3840, 2160);
        var dpi = DisplayDpi.FromScale(2.0);

        var placement = IslandPositionCalculator.CalculatePlacement(monitor, dpi);

        Assert.Equal(1280, placement.Width);
        Assert.Equal(480, placement.Height);
        Assert.Equal((3840 - 1280) / 2, placement.X); // 1280
        Assert.Equal(0, placement.Y);
    }

    [Fact]
    public void CalculatePlacement_OnSecondaryMonitorLeftOfPrimary_ComputesNegativeCoordinates()
    {
        // Monitor located to the left of primary monitor: Left = -1920, Width = 1920
        var monitor = new MonitorArea(-1920, 0, 1920, 1080);
        var dpi = DisplayDpi.FromScale(1.0);

        var placement = IslandPositionCalculator.CalculatePlacement(monitor, dpi);

        Assert.Equal(640, placement.Width);
        Assert.Equal(240, placement.Height);
        // -1920 + (1920 - 640) / 2 = -1920 + 640 = -1280
        Assert.Equal(-1280, placement.X);
        Assert.Equal(0, placement.Y);
    }

    [Fact]
    public void CalculatePlacement_OnSecondaryMonitorWithVerticalOffsetAnd125PercentScale_ComputesAccurateOffsets()
    {
        // Secondary monitor at X=1920, Y=120, Width=2560, Height=1440 at 125% DPI
        var monitor = new MonitorArea(1920, 120, 2560, 1440);
        var dpi = DisplayDpi.FromScale(1.25);

        var placement = IslandPositionCalculator.CalculatePlacement(monitor, dpi);

        Assert.Equal(800, placement.Width);
        Assert.Equal(300, placement.Height);
        // 1920 + (2560 - 800) / 2 = 1920 + 880 = 2800
        Assert.Equal(2800, placement.X);
        // 120 + 0 * 1.25 = 120
        Assert.Equal(120, placement.Y);
    }

    [Fact]
    public void CalculatePlacement_WithCustomDimensionsAndMargin_RespectsCustomValues()
    {
        var monitor = new MonitorArea(0, 0, 1920, 1080);
        var dpi = DisplayDpi.FromScale(1.5);
        var customDimensions = new WindowDimensions(500, 200);
        double customMargin = 16.0;

        var placement = IslandPositionCalculator.CalculatePlacement(monitor, dpi, customDimensions, customMargin);

        Assert.Equal(750, placement.Width);   // 500 * 1.5
        Assert.Equal(300, placement.Height);  // 200 * 1.5
        Assert.Equal((1920 - 750) / 2, placement.X); // 585
        Assert.Equal((int)(16.0 * 1.5), placement.Y); // 24
    }

    [Fact]
    public void CalculatePlacement_WithInvalidInputs_ThrowsArgumentOutOfRangeException()
    {
        var monitor = new MonitorArea(0, 0, 1920, 1080);
        var validDpi = DisplayDpi.Default;

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            IslandPositionCalculator.CalculatePlacement(monitor, validDpi, new WindowDimensions(-10, 100)));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            IslandPositionCalculator.CalculatePlacement(monitor, validDpi, new WindowDimensions(100, 0)));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            IslandPositionCalculator.CalculatePlacement(monitor, new DisplayDpi(0, 96)));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            IslandPositionCalculator.CalculatePlacement(monitor, new DisplayDpi(96, -10)));
    }

    [Fact]
    public void DisplayDpi_FromScale_ComputesAccurateDpiAndScaleProperties()
    {
        var dpi = DisplayDpi.FromScale(1.25);
        Assert.Equal(120.0, dpi.DpiX);
        Assert.Equal(120.0, dpi.DpiY);
        Assert.Equal(1.25, dpi.ScaleX);
        Assert.Equal(1.25, dpi.ScaleY);
    }

    [Fact]
    public void MonitorArea_FromEdges_InitializesCorrectWidthAndHeight()
    {
        var area = MonitorArea.FromEdges(100, 50, 1920, 1080);
        Assert.Equal(100, area.Left);
        Assert.Equal(50, area.Top);
        Assert.Equal(1820, area.Width);
        Assert.Equal(1030, area.Height);
        Assert.Equal(1920, area.Right);
        Assert.Equal(1080, area.Bottom);
    }

    [Fact]
    public void CalculatePlacement_WithCustomOffsets_AppliesDpiScaledOffsetsCorrectly()
    {
        var monitor = new MonitorArea(0, 0, 1920, 1080);
        var dpi = DisplayDpi.FromScale(1.5); // 150% scale
        double topMargin = 20.0;
        double offsetX = 50.0;

        var placement = IslandPositionCalculator.CalculatePlacement(
            monitor,
            dpi,
            WindowDimensions.DefaultIsland,
            topMarginDip: topMargin,
            offsetXDip: offsetX);

        // Physical width: 640 * 1.5 = 960
        Assert.Equal(960, placement.Width);
        // Base centered X: (1920 - 960) / 2 = 480. Offset: 50 * 1.5 = 75. Expected X: 480 + 75 = 555
        Assert.Equal(555, placement.X);
        // Expected Y: 20 * 1.5 = 30
        Assert.Equal(30, placement.Y);
    }

    [Fact]
    public void CalculatePlacement_OnSecondaryMonitorRightOfPrimary_ComputesCorrectAbsolutePlacement()
    {
        // Primary: 1920x1080 at (0, 0). Secondary: 2560x1440 at (1920, 0) with 100% scale
        var secondaryMonitor = new MonitorArea(1920, 0, 2560, 1440);
        var dpi = DisplayDpi.FromScale(1.0);

        var placement = IslandPositionCalculator.CalculatePlacement(secondaryMonitor, dpi);

        // Island centered on secondary monitor: 1920 + (2560 - 640)/2 = 1920 + 960 = 2880
        Assert.Equal(2880, placement.X);
        Assert.Equal(0, placement.Y);
        Assert.Equal(640, placement.Width);
        Assert.Equal(240, placement.Height);
    }

    [Fact]
    public void CalculatePlacement_OnPortraitSecondaryMonitor_CentersHorizontallyOnPortraitBounds()
    {
        // Portrait secondary monitor: 1080x1920 at (-1080, 0) with 100% scale
        var portraitMonitor = new MonitorArea(-1080, 0, 1080, 1920);
        var dpi = DisplayDpi.FromScale(1.0);

        var placement = IslandPositionCalculator.CalculatePlacement(portraitMonitor, dpi);

        // Island centered on portrait monitor: -1080 + (1080 - 640)/2 = -1080 + 220 = -860
        Assert.Equal(-860, placement.X);
        Assert.Equal(0, placement.Y);
        Assert.Equal(640, placement.Width);
        Assert.Equal(240, placement.Height);
    }

    [Fact]
    public void CalculatePlacement_OnMixedDpiSecondaryMonitor_ComputesCorrectScaledDimensionsAndOffsets()
    {
        // Primary: 1920x1080 @ 100% scale (0, 0).
        // Secondary: 3840x2160 @ 150% scale located at (1920, 0).
        var secondaryMonitor = new MonitorArea(1920, 0, 3840, 2160);
        var secondaryDpi = DisplayDpi.FromScale(1.5);

        var placement = IslandPositionCalculator.CalculatePlacement(secondaryMonitor, secondaryDpi);

        // At 150% scale, base 640x240 becomes 960x360.
        // Horizontally centered on secondary monitor: 1920 + (3840 - 960)/2 = 1920 + 1440 = 3360.
        Assert.Equal(3360, placement.X);
        Assert.Equal(0, placement.Y);
        Assert.Equal(960, placement.Width);
        Assert.Equal(360, placement.Height);
    }

    [Fact]
    public void CalculatePlacement_FallbackToPrimaryWhenMonitorAreaIsEmpty_ReturnsSafeCenteredCoordinates()
    {
        // Fallback primary 1080p monitor
        var fallbackPrimary = new MonitorArea(0, 0, 1920, 1080);
        var dpi = DisplayDpi.FromScale(1.0);

        var placement = IslandPositionCalculator.CalculatePlacement(fallbackPrimary, dpi);

        Assert.Equal(640, placement.X);
        Assert.Equal(0, placement.Y);
        Assert.Equal(640, placement.Width);
        Assert.Equal(240, placement.Height);
    }

    [Theory]
    [InlineData(320.0, 2.0, true)]     // Center, Y=2
    [InlineData(320.0, 10.0, true)]    // Real notch hover depth, Y=10
    [InlineData(320.0, 15.0, true)]    // Real notch hover depth, Y=15
    [InlineData(320.0, 25.0, true)]    // Real notch hover depth, Y=25
    [InlineData(320.0, 28.0, true)]    // Lower edge of resting notch, Y=28
    [InlineData(220.0, 15.0, true)]    // Left edge, Y=15
    [InlineData(420.0, 15.0, true)]    // Right edge, Y=15
    [InlineData(218.5, 15.0, true)]    // Within left tolerance (-2.0)
    [InlineData(421.5, 15.0, true)]    // Within right tolerance (+2.0)
    [InlineData(320.0, -0.5, true)]    // Sub-pixel top bezel rounding (-1.0)
    [InlineData(320.0, 28.8, true)]    // Sub-pixel bottom border rounding (+1.0)
    [InlineData(215.0, 15.0, false)]   // Too far left
    [InlineData(425.0, 15.0, false)]   // Too far right
    [InlineData(320.0, 32.0, false)]   // Too far down (Y > 29.0)
    [InlineData(320.0, -2.5, false)]   // Too far up (Y < -1.0)
    [InlineData(0.0, 100.0, false)]    // Far away
    public void IsPointInRestingSensorZone_WithDefaultDimensions_EvaluatesCorrectly(double x, double y, bool expected)
    {
        bool result = IslandPositionCalculator.IsPointInRestingSensorZone(x, y, windowWidthDip: 640.0, capsuleWidthDip: 200.0, sensorHeightDip: 28.0);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void IsPointInRestingSensorZone_WithCustomCapsuleWidth_AdaptsHitArea()
    {
        // Capsule width 300 DIP: centered in 640 DIP means [320-150, 320+150] = [170, 470]
        Assert.True(IslandPositionCalculator.IsPointInRestingSensorZone(175.0, 2.0, windowWidthDip: 640.0, capsuleWidthDip: 300.0));
        Assert.True(IslandPositionCalculator.IsPointInRestingSensorZone(465.0, 2.0, windowWidthDip: 640.0, capsuleWidthDip: 300.0));
        Assert.False(IslandPositionCalculator.IsPointInRestingSensorZone(160.0, 2.0, windowWidthDip: 640.0, capsuleWidthDip: 300.0));
    }

    [Fact]
    public void IsPointInRestingSensorZone_WithInvalidDimensions_ReturnsFalse()
    {
        Assert.False(IslandPositionCalculator.IsPointInRestingSensorZone(320.0, 2.0, windowWidthDip: 0));
        Assert.False(IslandPositionCalculator.IsPointInRestingSensorZone(320.0, 2.0, capsuleWidthDip: 0));
        Assert.False(IslandPositionCalculator.IsPointInRestingSensorZone(320.0, 2.0, sensorHeightDip: 0));
    }

    [Theory]
    [InlineData(320.0, 0.0, true)]      // Top bezel
    [InlineData(320.0, 36.0, true)]     // Nominal capsule height
    [InlineData(320.0, 44.0, true)]     // Bottom edge of expanded sensor
    [InlineData(200.0, 20.0, true)]     // Left edge of 240-wide notch (320 - 120 = 200)
    [InlineData(440.0, 20.0, true)]     // Right edge of 240-wide notch (320 + 120 = 440)
    [InlineData(195.0, 20.0, false)]    // Outside left
    [InlineData(445.0, 20.0, false)]    // Outside right
    [InlineData(320.0, 48.0, false)]    // Below bottom edge
    public void IsPointInRestingSensorZone_WithExpandedDimensions_EvaluatesCorrectly(double x, double y, bool expected)
    {
        bool result = IslandPositionCalculator.IsPointInRestingSensorZone(x, y, windowWidthDip: 640.0, capsuleWidthDip: 240.0, sensorHeightDip: 44.0);
        Assert.Equal(expected, result);
    }
}


