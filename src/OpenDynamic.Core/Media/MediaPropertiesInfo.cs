namespace OpenDynamic.Core.Media;

/// <summary>
/// Metadata describing the currently active media track.
/// Free of UI types to respect Golden Rule 5 (Core domain isolation).
/// </summary>
public sealed record MediaPropertiesInfo(
    string Title = "",
    string Artist = "",
    string AlbumTitle = "",
    string AlbumArtist = "",
    int TrackNumber = 0,
    IReadOnlyList<string>? Genres = null,
    bool HasThumbnail = false);
