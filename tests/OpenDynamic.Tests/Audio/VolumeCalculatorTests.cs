using OpenDynamic.Core.Audio;
using Xunit;

namespace OpenDynamic.Tests.Audio;

public class VolumeCalculatorTests
{
    [Theory]
    [InlineData(-0.5f, 0.0f)]
    [InlineData(0.0f, 0.0f)]
    [InlineData(0.45f, 0.45f)]
    [InlineData(1.0f, 1.0f)]
    [InlineData(1.5f, 1.0f)]
    [InlineData(float.NaN, 0.0f)]
    public void Normalize_ClampsValueCorrectly(float raw, float expected)
    {
        var result = VolumeCalculator.Normalize(raw);
        Assert.Equal(expected, result, precision: 4);
    }

    [Fact]
    public void CalculateLevelStep_ScrollUpIncreasesVolume()
    {
        // 1 notch up (120) with 2% step: 0.50 + 0.02 = 0.52
        float updated = VolumeCalculator.CalculateLevelStep(0.50f, 120, step: 0.02f);
        Assert.Equal(0.52f, updated, precision: 4);
    }

    [Fact]
    public void CalculateLevelStep_ScrollDownDecreasesVolume()
    {
        // 1 notch down (-120) with 2% step: 0.50 - 0.02 = 0.48
        float updated = VolumeCalculator.CalculateLevelStep(0.50f, -120, step: 0.02f);
        Assert.Equal(0.48f, updated, precision: 4);
    }

    [Fact]
    public void CalculateLevelStep_ClampsAtBoundaries()
    {
        float atMax = VolumeCalculator.CalculateLevelStep(0.99f, 120, step: 0.05f);
        Assert.Equal(1.0f, atMax, precision: 4);

        float atMin = VolumeCalculator.CalculateLevelStep(0.01f, -120, step: 0.05f);
        Assert.Equal(0.0f, atMin, precision: 4);
    }

    [Fact]
    public void CalculateLevelStep_ZeroDeltaReturnsCurrentNormalized()
    {
        float current = VolumeCalculator.CalculateLevelStep(0.65f, 0);
        Assert.Equal(0.65f, current, precision: 4);
    }

    [Theory]
    [InlineData(0.0f, 0)]
    [InlineData(0.456f, 46)]
    [InlineData(0.999f, 100)]
    [InlineData(1.0f, 100)]
    public void ToPercentage_RoundsProperly(float level, int expectedPercent)
    {
        int percent = VolumeCalculator.ToPercentage(level);
        Assert.Equal(expectedPercent, percent);
    }

    [Theory]
    [InlineData(0.5f, true, VolumeIconType.Muted)]
    [InlineData(0.0f, false, VolumeIconType.Muted)]
    [InlineData(0.15f, false, VolumeIconType.Low)]
    [InlineData(0.33f, false, VolumeIconType.Low)]
    [InlineData(0.50f, false, VolumeIconType.Medium)]
    [InlineData(0.66f, false, VolumeIconType.Medium)]
    [InlineData(0.85f, false, VolumeIconType.High)]
    [InlineData(1.0f, false, VolumeIconType.High)]
    public void GetVolumeIconType_MapsCorrectIcon(float level, bool isMuted, VolumeIconType expectedIcon)
    {
        var icon = VolumeCalculator.GetVolumeIconType(level, isMuted);
        Assert.Equal(expectedIcon, icon);
    }
}
