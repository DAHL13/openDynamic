using OpenDynamic.Core.State;

namespace OpenDynamic.Core.Audio.Spectrum;

/// <summary>
/// Immutable context snapshot representing all conditions required to evaluate visualizer activation.
/// </summary>
public readonly record struct VisualizerActivationContext(
    AudioVisualizerMode Mode,
    bool IsMediaPlaying,
    bool IsMediaWidgetVisible,
    IslandState IslandState,
    bool IsFullscreenSuppressed);

/// <summary>
/// Pure domain policy determining whether WASAPI audio loopback capture must be actively running.
/// Satisfies Golden Rule 1 (0% CPU at rest, no capture when paused or hidden) and Golden Rule 5 (pure logic in Core).
/// Capture is activated IF AND ONLY IF all of the following conditions are simultaneously met:
/// 1. Setting mode is set to <see cref="AudioVisualizerMode.Real"/>.
/// 2. Active media playback is playing (GSMTC Playing).
/// 3. Media widget is currently visible on the island.
/// 4. Island display state is not hidden (<see cref="IslandState.Hidden"/>).
/// 5. No exclusive fullscreen application is currently suppressing the island.
/// </summary>
public static class VisualizerActivationPolicy
{
    /// <summary>
    /// Evaluates whether WASAPI loopback capture should be active given the context snapshot.
    /// </summary>
    public static bool ShouldCapture(in VisualizerActivationContext context)
    {
        return ShouldCapture(
            context.Mode,
            context.IsMediaPlaying,
            context.IsMediaWidgetVisible,
            context.IslandState,
            context.IsFullscreenSuppressed);
    }

    /// <summary>
    /// Evaluates whether WASAPI loopback capture should be active given individual state values.
    /// </summary>
    public static bool ShouldCapture(
        AudioVisualizerMode mode,
        bool isMediaPlaying,
        bool isMediaWidgetVisible,
        IslandState islandState,
        bool isFullscreenSuppressed)
    {
        if (mode != AudioVisualizerMode.Real)
        {
            return false;
        }

        if (!isMediaPlaying)
        {
            return false;
        }

        if (!isMediaWidgetVisible)
        {
            return false;
        }

        if (islandState == IslandState.Hidden)
        {
            return false;
        }

        if (isFullscreenSuppressed)
        {
            return false;
        }

        return true;
    }
}
