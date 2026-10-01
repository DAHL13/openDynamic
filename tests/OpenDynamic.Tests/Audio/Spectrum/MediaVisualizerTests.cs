using System;
using OpenDynamic.Core.Audio.Spectrum;
using OpenDynamic.Core.State;
using Xunit;

namespace OpenDynamic.Tests.Audio.Spectrum;

public class MediaVisualizerTests
{
    private sealed class FakeAudioSpectrumService : IAudioSpectrumService
    {
        public AudioVisualizerMode ActiveMode { get; private set; } = AudioVisualizerMode.Real;
        public bool IsCapturing { get; private set; }
        public VisualizerActivationContext LastContext { get; private set; }
        public int ActivationCallCount { get; private set; }

        public event EventHandler<AudioVisualizerMode>? ModeDegraded { add { } remove { } }

        public void UpdateActivation(in VisualizerActivationContext context)
        {
            LastContext = context;
            ActivationCallCount++;
            ActiveMode = context.Mode;
            IsCapturing = VisualizerActivationPolicy.ShouldCapture(in context);
        }

        public void GetCompactBands(Span<float> destination) => destination.Clear();
        public void GetExpandedBands(Span<float> destination) => destination.Clear();
        public void Dispose() { }
    }

    [Fact]
    public void Activation_StartsCapture_WhenMusicPlayingAndWidgetVisible()
    {
        var service = new FakeAudioSpectrumService();
        var context = new VisualizerActivationContext(
            Mode: AudioVisualizerMode.Real,
            IsMediaPlaying: true,
            IsMediaWidgetVisible: true,
            IslandState: IslandState.Compact,
            IsFullscreenSuppressed: false);

        service.UpdateActivation(in context);

        Assert.True(service.IsCapturing);
        Assert.Equal(AudioVisualizerMode.Real, service.ActiveMode);
    }

    [Fact]
    public void Activation_StopsCaptureImmediately_WhenMusicPaused()
    {
        var service = new FakeAudioSpectrumService();

        // 1. Started while playing
        service.UpdateActivation(new VisualizerActivationContext(
            Mode: AudioVisualizerMode.Real,
            IsMediaPlaying: true,
            IsMediaWidgetVisible: true,
            IslandState: IslandState.Compact,
            IsFullscreenSuppressed: false));
        Assert.True(service.IsCapturing);

        // 2. Music paused -> immediate capture stop (0% CPU at rest)
        service.UpdateActivation(new VisualizerActivationContext(
            Mode: AudioVisualizerMode.Real,
            IsMediaPlaying: false,
            IsMediaWidgetVisible: true,
            IslandState: IslandState.Compact,
            IsFullscreenSuppressed: false));

        Assert.False(service.IsCapturing);
    }

    [Fact]
    public void Activation_StopsCaptureImmediately_WhenIslandHidden()
    {
        var service = new FakeAudioSpectrumService();

        service.UpdateActivation(new VisualizerActivationContext(
            Mode: AudioVisualizerMode.Real,
            IsMediaPlaying: true,
            IsMediaWidgetVisible: false,
            IslandState: IslandState.Hidden,
            IsFullscreenSuppressed: false));

        Assert.False(service.IsCapturing);
    }

    [Fact]
    public void Activation_StopsCaptureImmediately_WhenFullscreenSuppressed()
    {
        var service = new FakeAudioSpectrumService();

        service.UpdateActivation(new VisualizerActivationContext(
            Mode: AudioVisualizerMode.Real,
            IsMediaPlaying: true,
            IsMediaWidgetVisible: true,
            IslandState: IslandState.Compact,
            IsFullscreenSuppressed: true));

        Assert.False(service.IsCapturing);
    }

    [Fact]
    public void Activation_NeverCaptures_WhenModeIsSimulatedOrDisabled()
    {
        var service = new FakeAudioSpectrumService();

        service.UpdateActivation(new VisualizerActivationContext(
            Mode: AudioVisualizerMode.Simulated,
            IsMediaPlaying: true,
            IsMediaWidgetVisible: true,
            IslandState: IslandState.Compact,
            IsFullscreenSuppressed: false));

        Assert.False(service.IsCapturing);
        Assert.Equal(AudioVisualizerMode.Simulated, service.ActiveMode);

        service.UpdateActivation(new VisualizerActivationContext(
            Mode: AudioVisualizerMode.Disabled,
            IsMediaPlaying: true,
            IsMediaWidgetVisible: true,
            IslandState: IslandState.Compact,
            IsFullscreenSuppressed: false));

        Assert.False(service.IsCapturing);
        Assert.Equal(AudioVisualizerMode.Disabled, service.ActiveMode);
    }
}
