namespace OpenDynamic.Core.Media;

/// <summary>
/// Declares the playback operations supported by the current media session.
/// </summary>
public sealed record MediaPlaybackCapabilities(
    bool CanPlay = false,
    bool CanPause = false,
    bool CanTogglePlayPause = false,
    bool CanSkipNext = false,
    bool CanSkipPrevious = false,
    bool CanSeek = false);
