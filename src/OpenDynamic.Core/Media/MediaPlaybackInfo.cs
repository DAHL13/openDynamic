namespace OpenDynamic.Core.Media;

/// <summary>
/// Immutable snapshot of media playback state and capabilities.
/// </summary>
public sealed record MediaPlaybackInfo(
    MediaPlaybackStatus Status,
    MediaPlaybackCapabilities Capabilities);
