using System;

namespace OpenDynamic.Core.Audio.Spectrum;

/// <summary>
/// Domain contract for audio spectrum analysis and visualization.
/// Honors Golden Rule 1 (active only when needed, 0% CPU in rest) and Golden Rule 5 (isolated contract in Core).
/// </summary>
public interface IAudioSpectrumService : IDisposable
{
    /// <summary>
    /// Current operational mode (Real, Simulated, or Disabled).
    /// </summary>
    AudioVisualizerMode ActiveMode { get; }

    /// <summary>
    /// Indicates whether real-time WASAPI loopback capture is currently active.
    /// </summary>
    bool IsCapturing { get; }

    /// <summary>
    /// Updates activation state according to current policy context.
    /// Starts or stops WASAPI capture immediately to fulfill Golden Rule 1.
    /// </summary>
    void UpdateActivation(in VisualizerActivationContext context);

    /// <summary>
    /// Copies the current 12 smoothed frequency bands for Compact view.
    /// </summary>
    void GetCompactBands(Span<float> destination);

    /// <summary>
    /// Copies the current 24 smoothed frequency bands for Expanded view.
    /// </summary>
    void GetExpandedBands(Span<float> destination);

    /// <summary>
    /// Occurs when real-time capture encounters an error and degrades gracefully to Simulated mode.
    /// </summary>
    event EventHandler<AudioVisualizerMode>? ModeDegraded;
}
