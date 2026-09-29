using OpenDynamic.Core.Windowing;
using Xunit;

namespace OpenDynamic.Tests.Windowing;

public class FullscreenDetectorTests
{
    [Theory]
    [InlineData(FullscreenDetector.QUNS_BUSY, true)]
    [InlineData(FullscreenDetector.QUNS_RUNNING_D3D_FULL_SCREEN, true)]
    [InlineData(FullscreenDetector.QUNS_PRESENTATION_MODE, true)]
    [InlineData(FullscreenDetector.QUNS_ACCEPTS_NOTIFICATIONS, false)]
    [InlineData(FullscreenDetector.QUNS_QUIET_TIME, false)]
    [InlineData(FullscreenDetector.QUNS_NOT_PRESENT, false)]
    [InlineData(FullscreenDetector.QUNS_APP, false)]
    public void IsNotificationStateFullscreen_DetectsExpectedStates(int quns, bool expected)
    {
        bool result = FullscreenDetector.IsNotificationStateFullscreen(quns);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void IsWindowBoundsFullscreen_ExactMatchCoversMonitor()
    {
        // 1920x1080 display
        bool isFullscreen = FullscreenDetector.IsWindowBoundsFullscreen(
            winLeft: 0, winTop: 0, winRight: 1920, winBottom: 1080,
            monLeft: 0, monTop: 0, monRight: 1920, monBottom: 1080,
            isShellOrDesktop: false);

        Assert.True(isFullscreen);
    }

    [Fact]
    public void IsWindowBoundsFullscreen_WindowOversizedCoversMonitor()
    {
        // Borderless game window with -1 or -2 DIP border overflow
        bool isFullscreen = FullscreenDetector.IsWindowBoundsFullscreen(
            winLeft: -2, winTop: -2, winRight: 1922, winBottom: 1082,
            monLeft: 0, monTop: 0, monRight: 1920, monBottom: 1080,
            isShellOrDesktop: false);

        Assert.True(isFullscreen);
    }

    [Fact]
    public void IsWindowBoundsFullscreen_DesktopOrShellIgnored()
    {
        bool isFullscreen = FullscreenDetector.IsWindowBoundsFullscreen(
            winLeft: 0, winTop: 0, winRight: 1920, winBottom: 1080,
            monLeft: 0, monTop: 0, monRight: 1920, monBottom: 1080,
            isShellOrDesktop: true);

        Assert.False(isFullscreen);
    }

    [Fact]
    public void IsWindowBoundsFullscreen_PartialWindowNotFullscreen()
    {
        // Window only covering top half or smaller
        bool isFullscreen = FullscreenDetector.IsWindowBoundsFullscreen(
            winLeft: 100, winTop: 100, winRight: 1200, winBottom: 800,
            monLeft: 0, monTop: 0, monRight: 1920, monBottom: 1080,
            isShellOrDesktop: false);

        Assert.False(isFullscreen);
    }

    [Fact]
    public void IsWindowBoundsFullscreen_MaximizedWithTaskbarVisibleNotFullscreen()
    {
        // Standard maximized window (bottom leaves 40px for taskbar)
        bool isFullscreen = FullscreenDetector.IsWindowBoundsFullscreen(
            winLeft: 0, winTop: 0, winRight: 1920, winBottom: 1040,
            monLeft: 0, monTop: 0, monRight: 1920, monBottom: 1080,
            isShellOrDesktop: false);

        Assert.False(isFullscreen);
    }
}
