using OpenDynamic.Core.Windowing;

namespace OpenDynamic.Tests.Windowing;

/// <summary>
/// Regression tests for AUD-001 / AUD-005: the Hidden-state sensor strip must be minimal and must never
/// capture input when the ambient clock is disabled, a fullscreen app is protected or the system is suspended.
/// </summary>
public class RestingSensorPolicyTests
{
    private const double WindowWidth = 640.0;

    [Fact]
    public void Sensor_IsMinimalStrip_NotTheOldBlockingBox()
    {
        Assert.Equal(120.0, RestingSensorPolicy.WidthDip);
        Assert.Equal(4.0, RestingSensorPolicy.HeightDip);
    }

    [Theory]
    [InlineData(true, false, false, true)]
    [InlineData(false, false, false, false)] // ambient clock off -> fully click-through
    [InlineData(true, true, false, false)]   // fullscreen app -> never capture
    [InlineData(true, false, true, false)]   // power suspended -> never capture
    [InlineData(false, true, true, false)]
    public void IsInteractive_RequiresClockEnabledAndNoSuppression(bool clock, bool fullscreen, bool suspended, bool expected)
    {
        Assert.Equal(expected, RestingSensorPolicy.IsInteractive(clock, fullscreen, suspended));
    }

    [Theory]
    [InlineData(320.0, 0.0, true)]    // center, flush with the bezel
    [InlineData(320.0, 4.0, true)]    // bottom edge of the strip
    [InlineData(320.0, -3.0, true)]   // tolerated top overshoot
    [InlineData(260.0, 2.0, true)]    // left edge (320 - 60)
    [InlineData(380.0, 2.0, true)]    // right edge (320 + 60)
    [InlineData(259.9, 2.0, false)]   // just outside left
    [InlineData(380.1, 2.0, false)]   // just outside right
    [InlineData(320.0, 4.1, false)]   // just below the strip: browser tab region must stay clickable
    [InlineData(320.0, 30.0, false)]  // old 44 DIP box area: must NOT capture anymore
    [InlineData(320.0, -5.1, false)]
    public void ContainsPoint_MatchesStripGeometry(double x, double y, bool expected)
    {
        Assert.Equal(expected, RestingSensorPolicy.ContainsPoint(x, y, WindowWidth));
    }

    [Theory]
    [InlineData(double.NaN, 1.0, 640.0)]
    [InlineData(320.0, double.NaN, 640.0)]
    [InlineData(320.0, 1.0, double.NaN)]
    [InlineData(320.0, 1.0, 0.0)]
    [InlineData(320.0, 1.0, -10.0)]
    public void ContainsPoint_RejectsInvalidInput(double x, double y, double width)
    {
        Assert.False(RestingSensorPolicy.ContainsPoint(x, y, width));
    }

    [Fact]
    public void ShouldCapture_FullscreenHoverAtTopCenter_DoesNotCapture()
    {
        // Regression for AUD-005: hovering the top-center during fullscreen must not hit-test as client.
        Assert.False(RestingSensorPolicy.ShouldCapture(true, true, false, 320.0, 1.0, WindowWidth));
    }

    [Fact]
    public void ShouldCapture_NormalIdleHoverOnStrip_Captures()
    {
        Assert.True(RestingSensorPolicy.ShouldCapture(true, false, false, 320.0, 1.0, WindowWidth));
    }

    [Fact]
    public void ShouldCapture_ClockDisabled_NeverCapturesEvenOnStrip()
    {
        Assert.False(RestingSensorPolicy.ShouldCapture(false, false, false, 320.0, 1.0, WindowWidth));
    }
}
