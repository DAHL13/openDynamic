namespace OpenDynamic.Core.Audio.Spectrum;

/// <summary>
/// Mode of operation for the audio spectrum visualizer in the notch capsule.
/// </summary>
public enum AudioVisualizerMode
{
    /// <summary>
    /// Visualizer is disabled; equalizer bars remain hidden.
    /// </summary>
    Disabled,

    /// <summary>
    /// Simulated wave / equalizer bars driven by lightweight procedural animation.
    /// </summary>
    Simulated,

    /// <summary>
    /// Real-time audio spectrum analysis via WASAPI loopback capture and native FFT.
    /// </summary>
    Real
}
