namespace OpenDynamic.Core.Audio;

/// <summary>
/// Domain contract for reading, altering, and monitoring system volume.
/// Adheres strictly to Golden Rule 5 (isolated in Core, zero UI/platform dependencies).
/// </summary>
public interface IVolumeController
{
    /// <summary>
    /// Current normalized volume level scalar between 0.0f and 1.0f.
    /// </summary>
    float Volume { get; }

    /// <summary>
    /// Indicates whether audio output is currently muted.
    /// </summary>
    bool IsMuted { get; }

    /// <summary>
    /// Sets the master volume level.
    /// </summary>
    /// <param name="level">Normalized level in the range [0.0f, 1.0f].</param>
    void SetVolume(float level);

    /// <summary>
    /// Changes the master volume level by a relative delta step.
    /// </summary>
    /// <param name="step">Relative scalar delta (e.g. +0.02f or -0.02f).</param>
    void ChangeVolume(float step);

    /// <summary>
    /// Toggles the mute state.
    /// </summary>
    void ToggleMute();

    /// <summary>
    /// Sets the mute state explicitly.
    /// </summary>
    void SetMute(bool isMuted);

    /// <summary>
    /// Occurs when master volume or mute status changes on the active endpoint.
    /// </summary>
    event EventHandler<VolumeChangedEventArgs>? VolumeChanged;

    /// <summary>
    /// Occurs when Windows default audio rendering endpoint has changed.
    /// </summary>
    event EventHandler? DefaultDeviceChanged;
}
