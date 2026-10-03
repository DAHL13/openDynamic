using OpenDynamic.Core.Animation;
using OpenDynamic.Core.Audio.Spectrum;
using OpenDynamic.Core.EnergySaver;
using Xunit;

namespace OpenDynamic.Tests.EnergySaver;

public class ResourceProfilePolicyTests
{
    [Fact]
    public void StandardMode_WhenEnergySaverOff_ReturnsStandardProfile()
    {
        var profile = ResourceProfilePolicy.Resolve(
            energySaverState: EnergySaverState.Off,
            enableEfficientMode: true,
            configuredMotionMode: MotionMode.Auto,
            systemAnimationsEnabled: true,
            configuredVisualizerMode: AudioVisualizerMode.Real,
            configuredHardwareInterval: TimeSpan.FromSeconds(2.0));

        Assert.False(profile.IsEfficientModeActive);
        Assert.Equal(MotionProfile.Full, profile.MotionProfile);
        Assert.Equal(AudioVisualizerMode.Real, profile.VisualizerMode);
        Assert.Equal(TimeSpan.FromSeconds(2.0), profile.HardwareSamplingInterval);
        Assert.False(profile.ReduceAnimations);
        Assert.False(profile.CapAudioVisualizer);
        Assert.False(profile.ThrottleHardware);
    }

    [Fact]
    public void StandardMode_WhenEfficientModeDisabledInSettings_ReturnsStandardProfile()
    {
        var profile = ResourceProfilePolicy.Resolve(
            energySaverState: EnergySaverState.On,
            enableEfficientMode: false,
            configuredMotionMode: MotionMode.Auto,
            systemAnimationsEnabled: true,
            configuredVisualizerMode: AudioVisualizerMode.Real,
            configuredHardwareInterval: TimeSpan.FromSeconds(2.0));

        Assert.False(profile.IsEfficientModeActive);
        Assert.Equal(MotionProfile.Full, profile.MotionProfile);
        Assert.Equal(AudioVisualizerMode.Real, profile.VisualizerMode);
        Assert.Equal(TimeSpan.FromSeconds(2.0), profile.HardwareSamplingInterval);
    }

    [Fact]
    public void DesktopPc_NotSupported_ReturnsStandardProfile()
    {
        var profile = ResourceProfilePolicy.Resolve(
            energySaverState: EnergySaverState.NotSupported,
            enableEfficientMode: true,
            configuredMotionMode: MotionMode.Auto,
            systemAnimationsEnabled: true,
            configuredVisualizerMode: AudioVisualizerMode.Real,
            configuredHardwareInterval: TimeSpan.FromSeconds(2.0));

        Assert.False(profile.IsEfficientModeActive);
        Assert.Equal(MotionProfile.Full, profile.MotionProfile);
        Assert.Equal(AudioVisualizerMode.Real, profile.VisualizerMode);
    }

    [Fact]
    public void LaptopConnectedToAc_WhenEnergySaverDisabledInWinRt_ResolvesToStandardProfile()
    {
        // On a laptop plugged into AC, Windows reports WinRtDisabled (0) with physical battery present
        var state = EnergySaverStateMapper.FromWinRt(EnergySaverStateMapper.WinRtDisabled, hasBattery: true);
        Assert.Equal(EnergySaverState.Off, state);

        var profile = ResourceProfilePolicy.Resolve(
            energySaverState: state,
            enableEfficientMode: true,
            configuredMotionMode: MotionMode.Auto,
            systemAnimationsEnabled: true,
            configuredVisualizerMode: AudioVisualizerMode.Real,
            configuredHardwareInterval: TimeSpan.FromSeconds(2.0));

        Assert.False(profile.IsEfficientModeActive);
        Assert.Equal(MotionProfile.Full, profile.MotionProfile);
        Assert.Equal(AudioVisualizerMode.Real, profile.VisualizerMode);
        Assert.Equal(TimeSpan.FromSeconds(2.0), profile.HardwareSamplingInterval);
    }

    [Fact]
    public void EfficientMode_ForcesReducedMotion_WhenMotionModeIsAuto()
    {
        var profile = ResourceProfilePolicy.Resolve(
            energySaverState: EnergySaverState.On,
            enableEfficientMode: true,
            configuredMotionMode: MotionMode.Auto,
            systemAnimationsEnabled: true,
            configuredVisualizerMode: AudioVisualizerMode.Real,
            configuredHardwareInterval: TimeSpan.FromSeconds(2.0),
            reduceAnimationsEnabled: true);

        Assert.True(profile.IsEfficientModeActive);
        Assert.Equal(MotionProfile.Reduced, profile.MotionProfile);
        Assert.True(profile.ReduceAnimations);
    }

    [Fact]
    public void ExplicitUserSettingsWin_MotionModeFull_IsNotOverridden()
    {
        // User explicitly set MotionMode to Full. Efficient mode MUST NOT override this preference!
        var profile = ResourceProfilePolicy.Resolve(
            energySaverState: EnergySaverState.On,
            enableEfficientMode: true,
            configuredMotionMode: MotionMode.Full,
            systemAnimationsEnabled: true,
            configuredVisualizerMode: AudioVisualizerMode.Real,
            configuredHardwareInterval: TimeSpan.FromSeconds(2.0),
            reduceAnimationsEnabled: true);

        Assert.True(profile.IsEfficientModeActive);
        Assert.Equal(MotionProfile.Full, profile.MotionProfile);
        Assert.False(profile.ReduceAnimations);
    }

    [Fact]
    public void ExplicitUserSettingsWin_MotionModeReduced_StaysReduced()
    {
        var profile = ResourceProfilePolicy.Resolve(
            energySaverState: EnergySaverState.On,
            enableEfficientMode: true,
            configuredMotionMode: MotionMode.Reduced,
            systemAnimationsEnabled: true,
            configuredVisualizerMode: AudioVisualizerMode.Real,
            configuredHardwareInterval: TimeSpan.FromSeconds(2.0),
            reduceAnimationsEnabled: true);

        Assert.True(profile.IsEfficientModeActive);
        Assert.Equal(MotionProfile.Reduced, profile.MotionProfile);
    }

    [Fact]
    public void EfficientMode_PreservesMotion_WhenReduceAnimationsOptionDisabled()
    {
        var profile = ResourceProfilePolicy.Resolve(
            energySaverState: EnergySaverState.On,
            enableEfficientMode: true,
            configuredMotionMode: MotionMode.Auto,
            systemAnimationsEnabled: true,
            configuredVisualizerMode: AudioVisualizerMode.Real,
            configuredHardwareInterval: TimeSpan.FromSeconds(2.0),
            reduceAnimationsEnabled: false);

        Assert.True(profile.IsEfficientModeActive);
        Assert.Equal(MotionProfile.Full, profile.MotionProfile);
        Assert.False(profile.ReduceAnimations);
    }

    [Fact]
    public void AudioVisualizer_CappedToSimulated_WhenRealAndCapEnabled()
    {
        var profile = ResourceProfilePolicy.Resolve(
            energySaverState: EnergySaverState.On,
            enableEfficientMode: true,
            configuredMotionMode: MotionMode.Auto,
            systemAnimationsEnabled: true,
            configuredVisualizerMode: AudioVisualizerMode.Real,
            configuredHardwareInterval: TimeSpan.FromSeconds(2.0),
            capAudioVisualizerEnabled: true);

        Assert.True(profile.IsEfficientModeActive);
        Assert.Equal(AudioVisualizerMode.Simulated, profile.VisualizerMode);
        Assert.True(profile.CapAudioVisualizer);
    }

    [Fact]
    public void AudioVisualizer_Preserved_WhenCapOptionDisabled()
    {
        var profile = ResourceProfilePolicy.Resolve(
            energySaverState: EnergySaverState.On,
            enableEfficientMode: true,
            configuredMotionMode: MotionMode.Auto,
            systemAnimationsEnabled: true,
            configuredVisualizerMode: AudioVisualizerMode.Real,
            configuredHardwareInterval: TimeSpan.FromSeconds(2.0),
            capAudioVisualizerEnabled: false);

        Assert.True(profile.IsEfficientModeActive);
        Assert.Equal(AudioVisualizerMode.Real, profile.VisualizerMode);
        Assert.False(profile.CapAudioVisualizer);
    }

    [Fact]
    public void AudioVisualizer_Disabled_StaysDisabled()
    {
        var profile = ResourceProfilePolicy.Resolve(
            energySaverState: EnergySaverState.On,
            enableEfficientMode: true,
            configuredMotionMode: MotionMode.Auto,
            systemAnimationsEnabled: true,
            configuredVisualizerMode: AudioVisualizerMode.Disabled,
            configuredHardwareInterval: TimeSpan.FromSeconds(2.0),
            capAudioVisualizerEnabled: true);

        Assert.True(profile.IsEfficientModeActive);
        Assert.Equal(AudioVisualizerMode.Disabled, profile.VisualizerMode);
        Assert.False(profile.CapAudioVisualizer);
    }

    [Fact]
    public void HardwareThrottled_FromTwoToFiveSeconds_WhenEnabled()
    {
        var profile = ResourceProfilePolicy.Resolve(
            energySaverState: EnergySaverState.On,
            enableEfficientMode: true,
            configuredMotionMode: MotionMode.Auto,
            systemAnimationsEnabled: true,
            configuredVisualizerMode: AudioVisualizerMode.Real,
            configuredHardwareInterval: TimeSpan.FromSeconds(2.0),
            throttleHardwareEnabled: true,
            efficientHardwareInterval: TimeSpan.FromSeconds(5.0));

        Assert.True(profile.IsEfficientModeActive);
        Assert.Equal(TimeSpan.FromSeconds(5.0), profile.HardwareSamplingInterval);
        Assert.True(profile.ThrottleHardware);
    }

    [Fact]
    public void HardwareNotThrottled_WhenThrottleOptionDisabled()
    {
        var profile = ResourceProfilePolicy.Resolve(
            energySaverState: EnergySaverState.On,
            enableEfficientMode: true,
            configuredMotionMode: MotionMode.Auto,
            systemAnimationsEnabled: true,
            configuredVisualizerMode: AudioVisualizerMode.Real,
            configuredHardwareInterval: TimeSpan.FromSeconds(2.0),
            throttleHardwareEnabled: false,
            efficientHardwareInterval: TimeSpan.FromSeconds(5.0));

        Assert.True(profile.IsEfficientModeActive);
        Assert.Equal(TimeSpan.FromSeconds(2.0), profile.HardwareSamplingInterval);
        Assert.False(profile.ThrottleHardware);
    }
}
