namespace OpenDynamic.Core.Media;

/// <summary>
/// Status of media playback corresponding to GSMTC playback states.
/// </summary>
public enum MediaPlaybackStatus
{
    Closed = 0,
    Opened = 1,
    Changing = 2,
    Stopped = 3,
    Playing = 4,
    Paused = 5
}
