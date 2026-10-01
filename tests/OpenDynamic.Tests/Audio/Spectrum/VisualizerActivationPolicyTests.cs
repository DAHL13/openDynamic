using OpenDynamic.Core.Audio.Spectrum;
using OpenDynamic.Core.State;
using Xunit;

namespace OpenDynamic.Tests.Audio.Spectrum;

public class VisualizerActivationPolicyTests
{
    [Fact]
    public void ShouldCapture_ReturnsTrue_OnlyWhenAllConditionsMet()
    {
        bool result = VisualizerActivationPolicy.ShouldCapture(
            mode: AudioVisualizerMode.Real,
            isMediaPlaying: true,
            isMediaWidgetVisible: true,
            islandState: IslandState.Compact,
            isFullscreenSuppressed: false);

        Assert.True(result);
    }

    [Theory]
    [InlineData(AudioVisualizerMode.Disabled, true, true, IslandState.Compact, false)]
    [InlineData(AudioVisualizerMode.Simulated, true, true, IslandState.Compact, false)]
    [InlineData(AudioVisualizerMode.Real, false, true, IslandState.Compact, false)]
    [InlineData(AudioVisualizerMode.Real, true, false, IslandState.Compact, false)]
    [InlineData(AudioVisualizerMode.Real, true, true, IslandState.Hidden, false)]
    [InlineData(AudioVisualizerMode.Real, true, true, IslandState.Compact, true)]
    [InlineData(AudioVisualizerMode.Real, true, true, IslandState.Expanded, true)]
    [InlineData(AudioVisualizerMode.Disabled, false, false, IslandState.Hidden, true)]
    public void ShouldCapture_ReturnsFalse_WhenAnyConditionFails(
        AudioVisualizerMode mode,
        bool isPlaying,
        bool isVisible,
        IslandState state,
        bool isFullscreen)
    {
        bool result = VisualizerActivationPolicy.ShouldCapture(
            mode: mode,
            isMediaPlaying: isPlaying,
            isMediaWidgetVisible: isVisible,
            islandState: state,
            isFullscreenSuppressed: isFullscreen);

        Assert.False(result);
    }

    [Theory]
    [InlineData(IslandState.Compact)]
    [InlineData(IslandState.Expanded)]
    [InlineData(IslandState.Split)]
    public void ShouldCapture_AllowsAllVisibleIslandStates(IslandState visibleState)
    {
        bool result = VisualizerActivationPolicy.ShouldCapture(
            mode: AudioVisualizerMode.Real,
            isMediaPlaying: true,
            isMediaWidgetVisible: true,
            islandState: visibleState,
            isFullscreenSuppressed: false);

        Assert.True(result);
    }

    [Fact]
    public void ShouldCapture_WithContextStruct_MatchesDirectParameters()
    {
        var context = new VisualizerActivationContext(
            Mode: AudioVisualizerMode.Real,
            IsMediaPlaying: true,
            IsMediaWidgetVisible: true,
            IslandState: IslandState.Expanded,
            IsFullscreenSuppressed: false);

        Assert.True(VisualizerActivationPolicy.ShouldCapture(in context));
    }
}
