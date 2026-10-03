using OpenDynamic.Core.Animation;
using OpenDynamic.Core.Audio.Spectrum;

namespace OpenDynamic.Core.EnergySaver;

/// <summary>
/// Pure domain policy calculating active resource profiles (Standard vs. BatterySaver / Efficient).
/// Dynamically adjusts spring physics, hardware sampling cadence, and audio spectrum visualizer caps.
/// Strictly enforces user precedence: explicit user settings (e.g. MotionMode.Full) are never overridden.
/// Adheres strictly to Golden Rule 5 (pure logic in Core, zero Windows/WPF dependencies).
/// </summary>
public static class ResourceProfilePolicy
{
    /// <summary>
    /// Deterministically computes the effective resource profile based on energy saver state and user configuration.
    /// </summary>
    public static ResourceProfile Resolve(
        EnergySaverState energySaverState,
        bool enableEfficientMode,
        MotionMode configuredMotionMode,
        bool systemAnimationsEnabled,
        AudioVisualizerMode configuredVisualizerMode,
        TimeSpan configuredHardwareInterval,
        bool reduceAnimationsEnabled = true,
        bool capAudioVisualizerEnabled = true,
        bool throttleHardwareEnabled = true,
        TimeSpan? efficientHardwareInterval = null)
    {
        var effInterval = efficientHardwareInterval ?? TimeSpan.FromSeconds(5.0);
        var baseInterval = configuredHardwareInterval > TimeSpan.Zero
            ? configuredHardwareInterval
            : TimeSpan.FromSeconds(2.0);

        bool isEfficient = enableEfficientMode && (energySaverState == EnergySaverState.On);

        if (!isEfficient)
        {
            var standardMotion = MotionProfileResolver.Resolve(configuredMotionMode, systemAnimationsEnabled);
            return new ResourceProfile(
                IsEfficientModeActive: false,
                MotionProfile: standardMotion,
                VisualizerMode: configuredVisualizerMode,
                HardwareSamplingInterval: baseInterval,
                ReduceAnimations: false,
                CapAudioVisualizer: false,
                ThrottleHardware: false);
        }

        // --- Efficient Mode Active ---

        // 1. Motion Profile resolution
        // Explicit user settings win:
        // - If user explicitly chose Full -> keep Full.
        // - If user explicitly chose Reduced -> keep Reduced.
        // - If user left Auto -> force Reduced if reduceAnimationsEnabled is true.
        MotionProfile resolvedMotion;
        bool didReduceAnimations = false;

        if (configuredMotionMode == MotionMode.Full)
        {
            resolvedMotion = MotionProfile.Full;
        }
        else if (configuredMotionMode == MotionMode.Reduced)
        {
            resolvedMotion = MotionProfile.Reduced;
        }
        else // MotionMode.Auto
        {
            if (reduceAnimationsEnabled)
            {
                resolvedMotion = MotionProfile.Reduced;
                didReduceAnimations = true;
            }
            else
            {
                resolvedMotion = MotionProfileResolver.Resolve(configuredMotionMode, systemAnimationsEnabled);
            }
        }

        // 2. Audio Visualizer Mode resolution
        // - Disabled remains Disabled.
        // - Simulated remains Simulated.
        // - Real is capped to Simulated if capAudioVisualizerEnabled is true.
        AudioVisualizerMode resolvedVisualizer = configuredVisualizerMode;
        bool didCapVisualizer = false;

        if (configuredVisualizerMode == AudioVisualizerMode.Real && capAudioVisualizerEnabled)
        {
            resolvedVisualizer = AudioVisualizerMode.Simulated;
            didCapVisualizer = true;
        }

        // 3. Hardware Sampling Interval resolution
        // - Throttles from baseline (e.g. 2s) to efficient interval (e.g. 5s) if enabled.
        TimeSpan resolvedHardwareInterval = baseInterval;
        bool didThrottleHardware = false;

        if (throttleHardwareEnabled)
        {
            if (effInterval > baseInterval)
            {
                resolvedHardwareInterval = effInterval;
                didThrottleHardware = true;
            }
        }

        return new ResourceProfile(
            IsEfficientModeActive: true,
            MotionProfile: resolvedMotion,
            VisualizerMode: resolvedVisualizer,
            HardwareSamplingInterval: resolvedHardwareInterval,
            ReduceAnimations: didReduceAnimations,
            CapAudioVisualizer: didCapVisualizer,
            ThrottleHardware: didThrottleHardware);
    }
}
