namespace OpenDynamic.Core.Audio;

/// <summary>
/// Event arguments providing the latest normalized volume and mute state.
/// </summary>
public sealed class VolumeChangedEventArgs : EventArgs
{
    public float Volume { get; }
    public bool IsMuted { get; }

    public VolumeChangedEventArgs(float volume, bool isMuted)
    {
        Volume = volume;
        IsMuted = isMuted;
    }
}
