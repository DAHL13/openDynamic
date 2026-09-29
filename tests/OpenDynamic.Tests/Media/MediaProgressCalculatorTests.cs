using OpenDynamic.Core.Media;

namespace OpenDynamic.Tests.Media;

public class MediaProgressCalculatorTests
{
    [Fact]
    public void CalculateCurrentPosition_WhenPlaying_ExtrapolatesAccurately()
    {
        var basePosition = TimeSpan.FromSeconds(30);
        var lastUpdated = new DateTimeOffset(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);
        var now = lastUpdated.AddSeconds(5);
        var start = TimeSpan.Zero;
        var end = TimeSpan.FromMinutes(3);

        var position = MediaProgressCalculator.CalculateCurrentPosition(
            basePosition, lastUpdated, start, end, isPlaying: true, nowUtc: now);

        Assert.Equal(TimeSpan.FromSeconds(35), position);
    }

    [Fact]
    public void CalculateCurrentPosition_WhenPaused_ReturnsBasePositionWithoutExtrapolating()
    {
        var basePosition = TimeSpan.FromSeconds(45);
        var lastUpdated = new DateTimeOffset(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);
        var now = lastUpdated.AddSeconds(20);
        var start = TimeSpan.Zero;
        var end = TimeSpan.FromMinutes(4);

        var position = MediaProgressCalculator.CalculateCurrentPosition(
            basePosition, lastUpdated, start, end, isPlaying: false, nowUtc: now);

        Assert.Equal(TimeSpan.FromSeconds(45), position);
    }

    [Fact]
    public void CalculateCurrentPosition_WhenExtrapolatedPastEnd_ClampsToEndDuration()
    {
        var basePosition = TimeSpan.FromSeconds(170);
        var lastUpdated = new DateTimeOffset(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);
        var now = lastUpdated.AddSeconds(25);
        var start = TimeSpan.Zero;
        var end = TimeSpan.FromSeconds(180); // 3 minutes

        var position = MediaProgressCalculator.CalculateCurrentPosition(
            basePosition, lastUpdated, start, end, isPlaying: true, nowUtc: now);

        Assert.Equal(TimeSpan.FromSeconds(180), position);
    }

    [Fact]
    public void CalculateCurrentPosition_WhenBeforeStart_ClampsToStartTime()
    {
        var basePosition = TimeSpan.FromSeconds(-5);
        var lastUpdated = new DateTimeOffset(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);
        var now = lastUpdated;
        var start = TimeSpan.Zero;
        var end = TimeSpan.FromMinutes(3);

        var position = MediaProgressCalculator.CalculateCurrentPosition(
            basePosition, lastUpdated, start, end, isPlaying: false, nowUtc: now);

        Assert.Equal(TimeSpan.Zero, position);
    }

    [Fact]
    public void CalculateCurrentPosition_WithZeroOrReversedBounds_ReturnsZero()
    {
        var basePosition = TimeSpan.FromSeconds(10);
        var lastUpdated = DateTimeOffset.UtcNow;
        var now = lastUpdated;

        var position = MediaProgressCalculator.CalculateCurrentPosition(
            basePosition, lastUpdated, start: TimeSpan.FromMinutes(2), end: TimeSpan.FromMinutes(1), isPlaying: true, nowUtc: now);

        Assert.Equal(TimeSpan.Zero, position);
    }

    [Fact]
    public void CalculateProgressRatio_CalculatesNormalizedRatioCorrectly()
    {
        var start = TimeSpan.Zero;
        var end = TimeSpan.FromSeconds(200);

        var ratio0 = MediaProgressCalculator.CalculateProgressRatio(TimeSpan.Zero, start, end);
        var ratio50 = MediaProgressCalculator.CalculateProgressRatio(TimeSpan.FromSeconds(100), start, end);
        var ratio100 = MediaProgressCalculator.CalculateProgressRatio(TimeSpan.FromSeconds(200), start, end);

        Assert.Equal(0.0, ratio0);
        Assert.Equal(0.5, ratio50);
        Assert.Equal(1.0, ratio100);
    }

    [Fact]
    public void CalculateProgressRatio_ClampsBetweenZeroAndOne()
    {
        var start = TimeSpan.Zero;
        var end = TimeSpan.FromSeconds(100);

        var ratioNegative = MediaProgressCalculator.CalculateProgressRatio(TimeSpan.FromSeconds(-50), start, end);
        var ratioOver = MediaProgressCalculator.CalculateProgressRatio(TimeSpan.FromSeconds(150), start, end);

        Assert.Equal(0.0, ratioNegative);
        Assert.Equal(1.0, ratioOver);
    }

    [Theory]
    [InlineData(0, "0:00")]
    [InlineData(5, "0:05")]
    [InlineData(65, "1:05")]
    [InlineData(3599, "59:59")]
    [InlineData(3605, "1:00:05")]
    [InlineData(7322, "2:02:02")]
    public void FormatTime_FormatsCorrectly(int totalSeconds, string expected)
    {
        var formatted = MediaProgressCalculator.FormatTime(TimeSpan.FromSeconds(totalSeconds));
        Assert.Equal(expected, formatted);
    }
}
