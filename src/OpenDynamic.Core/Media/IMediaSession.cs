namespace OpenDynamic.Core.Media;

/// <summary>
/// Domain contract for an active media session (e.g. Spotify, Edge, Chrome, VLC).
/// Isolates playback commands and notifications from the WinRT platform.
/// </summary>
public interface IMediaSession : IDisposable
{
    /// <summary>
    /// Application identifier or AppUserModelId that owns this playback session.
    /// </summary>
    string SourceAppId { get; }

    /// <summary>
    /// Current playback status (Playing, Paused, Stopped, etc.).
    /// </summary>
    MediaPlaybackStatus PlaybackStatus { get; }

    /// <summary>
    /// Current playback info including capabilities (CanPlay, CanPause, CanSkipNext, etc.).
    /// </summary>
    MediaPlaybackInfo PlaybackInfo { get; }

    /// <summary>
    /// Timeline bounds and last known track position.
    /// </summary>
    MediaTimelineInfo TimelineInfo { get; }

    /// <summary>
    /// Metadata properties for the track (Title, Artist, Album, etc.).
    /// </summary>
    MediaPropertiesInfo Properties { get; }

    /// <summary>
    /// Occurs when track metadata (Title, Artist, Album, Thumbnail) changes.
    /// </summary>
    event EventHandler? MediaPropertiesChanged;

    /// <summary>
    /// Occurs when playback state (Playing, Paused, Capabilities) changes.
    /// </summary>
    event EventHandler? PlaybackInfoChanged;

    /// <summary>
    /// Occurs when timeline properties (duration, position, seek limits) change.
    /// </summary>
    event EventHandler? TimelinePropertiesChanged;

    /// <summary>
    /// Occurs when the player closes or the session is invalidated.
    /// </summary>
    event EventHandler? SessionClosed;

    /// <summary>
    /// Attempts to resume or begin playback.
    /// </summary>
    Task<bool> TryPlayAsync();

    /// <summary>
    /// Attempts to pause playback.
    /// </summary>
    Task<bool> TryPauseAsync();

    /// <summary>
    /// Attempts to toggle between play and pause.
    /// </summary>
    Task<bool> TryTogglePlayPauseAsync();

    /// <summary>
    /// Attempts to skip to the next track.
    /// </summary>
    Task<bool> TrySkipNextAsync();

    /// <summary>
    /// Attempts to skip to the previous track.
    /// </summary>
    Task<bool> TrySkipPreviousAsync();

    /// <summary>
    /// Attempts to seek to a specific position within the track timeline.
    /// </summary>
    Task<bool> TryChangePlaybackPositionAsync(TimeSpan position);
}
