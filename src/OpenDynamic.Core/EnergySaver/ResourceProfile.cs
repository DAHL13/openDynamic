using OpenDynamic.Core.Animation;
using OpenDynamic.Core.Audio.Spectrum;

namespace OpenDynamic.Core.EnergySaver;

/// <summary>
/// Immutable snapshot representing the resolved system resource profile.
/// Governs spring physics parameters, visualizer operation caps, and hardware telemetry cadence.
/// Adheres to Golden Rule 5 (pure domain record, zero Windows/WPF dependencies).
/// </summary>
public sealed record ResourceProfile(
    bool IsEfficientModeActive,
    MotionProfile MotionProfile,
    AudioVisualizerMode VisualizerMode,
    TimeSpan HardwareSamplingInterval,
    bool ReduceAnimations,
    bool CapAudioVisualizer,
    bool ThrottleHardware)
{
    /// <summary>
    /// Default standard resource profile with full animations, real visualizer, and 2s hardware sampling.
    /// </summary>
    public static ResourceProfile Standard { get; } = new(
        IsEfficientModeActive: false,
        MotionProfile: MotionProfile.Full,
        VisualizerMode: AudioVisualizerMode.Real,
        HardwareSamplingInterval: TimeSpan.FromSeconds(2.0),
        ReduceAnimations: false,
        CapAudioVisualizer: false,
        ThrottleHardware: false);
}
