using OpenDynamic.Core.Media;

namespace OpenDynamic.Tests.Media;

/// <summary>
/// Controllable test double of IMediaSession for unit testing media domain and widget logic.
/// </summary>
public sealed class TestMediaSession : IMediaSession
{
    public string SourceAppId { get; set; } = "Spotify.exe";
    public MediaPlaybackStatus PlaybackStatus { get; set; } = MediaPlaybackStatus.Playing;
    public MediaPlaybackInfo PlaybackInfo { get; set; } = new(
        MediaPlaybackStatus.Playing,
        new MediaPlaybackCapabilities(
            CanPlay: true,
            CanPause: true,
            CanTogglePlayPause: true,
            CanSkipNext: true,
            CanSkipPrevious: true,
            CanSeek: true));

    public MediaTimelineInfo TimelineInfo { get; set; } = new(
        StartTime: TimeSpan.Zero,
        EndTime: TimeSpan.FromMinutes(3.5),
        Position: TimeSpan.FromSeconds(30),
        MinSeekTime: TimeSpan.Zero,
        MaxSeekTime: TimeSpan.FromMinutes(3.5),
        LastUpdatedUtc: DateTimeOffset.UtcNow);

    public MediaPropertiesInfo Properties { get; set; } = new(
        Title: "Blinding Lights",
        Artist: "The Weeknd",
        AlbumTitle: "After Hours",
        AlbumArtist: "The Weeknd",
        TrackNumber: 9,
        HasThumbnail: true);

    public bool IsDisposed { get; private set; }

    public int PlayCallCount { get; private set; }
    public int PauseCallCount { get; private set; }
    public int TogglePlayPauseCallCount { get; private set; }
    public int SkipNextCallCount { get; private set; }
    public int SkipPreviousCallCount { get; private set; }
    public TimeSpan? LastSeekPosition { get; private set; }

    public event EventHandler? MediaPropertiesChanged;
    public event EventHandler? PlaybackInfoChanged;
    public event EventHandler? TimelinePropertiesChanged;
    public event EventHandler? SessionClosed;

    public void TriggerPropertiesChanged() => MediaPropertiesChanged?.Invoke(this, EventArgs.Empty);

    public void TriggerPlaybackInfoChanged(MediaPlaybackStatus newStatus)
    {
        PlaybackStatus = newStatus;
        PlaybackInfo = PlaybackInfo with { Status = newStatus };
        PlaybackInfoChanged?.Invoke(this, EventArgs.Empty);
    }

    public void TriggerTimelinePropertiesChanged(TimeSpan newPosition, TimeSpan? newDuration = null)
    {
        TimelineInfo = TimelineInfo with
        {
            Position = newPosition,
            EndTime = newDuration ?? TimelineInfo.EndTime,
            LastUpdatedUtc = DateTimeOffset.UtcNow
        };
        TimelinePropertiesChanged?.Invoke(this, EventArgs.Empty);
    }

    public void TriggerSessionClosed()
    {
        PlaybackStatus = MediaPlaybackStatus.Closed;
        SessionClosed?.Invoke(this, EventArgs.Empty);
    }

    public Task<bool> TryPlayAsync()
    {
        PlayCallCount++;
        TriggerPlaybackInfoChanged(MediaPlaybackStatus.Playing);
        return Task.FromResult(true);
    }

    public Task<bool> TryPauseAsync()
    {
        PauseCallCount++;
        TriggerPlaybackInfoChanged(MediaPlaybackStatus.Paused);
        return Task.FromResult(true);
    }

    public Task<bool> TryTogglePlayPauseAsync()
    {
        TogglePlayPauseCallCount++;
        var nextStatus = PlaybackStatus == MediaPlaybackStatus.Playing
            ? MediaPlaybackStatus.Paused
            : MediaPlaybackStatus.Playing;
        TriggerPlaybackInfoChanged(nextStatus);
        return Task.FromResult(true);
    }

    public Task<bool> TrySkipNextAsync()
    {
        SkipNextCallCount++;
        return Task.FromResult(true);
    }

    public Task<bool> TrySkipPreviousAsync()
    {
        SkipPreviousCallCount++;
        return Task.FromResult(true);
    }

    public Task<bool> TryChangePlaybackPositionAsync(TimeSpan position)
    {
        LastSeekPosition = position;
        TriggerTimelinePropertiesChanged(position);
        return Task.FromResult(true);
    }

    public void Dispose()
    {
        IsDisposed = true;
    }
}
