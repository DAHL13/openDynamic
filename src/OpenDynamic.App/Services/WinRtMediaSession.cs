using System.IO;
using System.Windows.Media.Imaging;
using Windows.Media.Control;
using OpenDynamic.Core.Media;
using Serilog;

namespace OpenDynamic.App.Services;

/// <summary>
/// WinRT implementation of <see cref="IMediaSession"/> wrapping <see cref="GlobalSystemMediaTransportControlsSession"/>.
/// Strictly encapsulates all WinRT COM calls in try/catch blocks with Serilog logging (Golden Rule 4)
/// and safely freezes thumbnails before handing them to UI or storing them.
/// </summary>
public sealed class WinRtMediaSession : IMediaSession
{
    private readonly GlobalSystemMediaTransportControlsSession _session;
    private bool _isDisposed;

    public string SourceAppId { get; }
    public MediaPlaybackStatus PlaybackStatus { get; private set; } = MediaPlaybackStatus.Closed;
    public MediaPlaybackInfo PlaybackInfo { get; private set; } = new(MediaPlaybackStatus.Closed, new MediaPlaybackCapabilities());
    public MediaTimelineInfo TimelineInfo { get; private set; } = new(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, DateTimeOffset.UtcNow);
    public MediaPropertiesInfo Properties { get; private set; } = new();

    /// <summary>
    /// Frozen BitmapImage thumbnail, safe for cross-thread access.
    /// </summary>
    public BitmapImage? ThumbnailImage { get; private set; }

    public event EventHandler? MediaPropertiesChanged;
    public event EventHandler? PlaybackInfoChanged;
    public event EventHandler? TimelinePropertiesChanged;
    public event EventHandler? SessionClosed;

    public WinRtMediaSession(GlobalSystemMediaTransportControlsSession session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));

        string appId = "Unknown";
        try
        {
            appId = _session.SourceAppUserModelId ?? "Unknown";
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Could not retrieve SourceAppUserModelId from GSMTC session.");
        }
        SourceAppId = appId;

        // Subscribe to WinRT events
        try
        {
            _session.MediaPropertiesChanged += OnMediaPropertiesChanged;
            _session.PlaybackInfoChanged += OnPlaybackInfoChanged;
            _session.TimelinePropertiesChanged += OnTimelinePropertiesChanged;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to subscribe to GSMTC session events for '{SourceAppId}'.", SourceAppId);
        }
    }

    /// <summary>
    /// Loads initial properties, playback status, and timeline asynchronously.
    /// </summary>
    public async Task RefreshAllAsync()
    {
        if (_isDisposed) return;

        UpdatePlaybackInfo();
        UpdateTimelineProperties();
        await UpdateMediaPropertiesAsync();
    }

    private void OnPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args)
    {
        if (_isDisposed) return;

        UpdatePlaybackInfo();
        PlaybackInfoChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnTimelinePropertiesChanged(GlobalSystemMediaTransportControlsSession sender, TimelinePropertiesChangedEventArgs args)
    {
        if (_isDisposed) return;

        UpdateTimelineProperties();
        TimelinePropertiesChanged?.Invoke(this, EventArgs.Empty);
    }

    private async void OnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args)
    {
        if (_isDisposed) return;

        await UpdateMediaPropertiesAsync();
        MediaPropertiesChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdatePlaybackInfo()
    {
        try
        {
            var winRtInfo = _session.GetPlaybackInfo();
            if (winRtInfo == null) return;

            var status = winRtInfo.PlaybackStatus switch
            {
                GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing => MediaPlaybackStatus.Playing,
                GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused => MediaPlaybackStatus.Paused,
                GlobalSystemMediaTransportControlsSessionPlaybackStatus.Stopped => MediaPlaybackStatus.Stopped,
                GlobalSystemMediaTransportControlsSessionPlaybackStatus.Opened => MediaPlaybackStatus.Opened,
                GlobalSystemMediaTransportControlsSessionPlaybackStatus.Changing => MediaPlaybackStatus.Changing,
                _ => MediaPlaybackStatus.Closed
            };

            var controls = winRtInfo.Controls;
            var capabilities = controls != null
                ? new MediaPlaybackCapabilities(
                    CanPlay: controls.IsPlayEnabled,
                    CanPause: controls.IsPauseEnabled,
                    CanTogglePlayPause: controls.IsPlayPauseToggleEnabled,
                    CanSkipNext: controls.IsNextEnabled,
                    CanSkipPrevious: controls.IsPreviousEnabled,
                    CanSeek: controls.IsPlaybackPositionEnabled)
                : new MediaPlaybackCapabilities();

            PlaybackStatus = status;
            PlaybackInfo = new MediaPlaybackInfo(status, capabilities);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Exception while updating playback info for '{SourceAppId}'. Player may have closed.", SourceAppId);
            NotifySessionClosedIfDead();
        }
    }

    private void UpdateTimelineProperties()
    {
        try
        {
            var winRtTimeline = _session.GetTimelineProperties();
            if (winRtTimeline == null) return;

            TimelineInfo = new MediaTimelineInfo(
                StartTime: winRtTimeline.StartTime,
                EndTime: winRtTimeline.EndTime,
                Position: winRtTimeline.Position,
                MinSeekTime: winRtTimeline.MinSeekTime,
                MaxSeekTime: winRtTimeline.MaxSeekTime,
                LastUpdatedUtc: winRtTimeline.LastUpdatedTime.ToUniversalTime());
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Exception while updating timeline properties for '{SourceAppId}'.", SourceAppId);
        }
    }

    private async Task UpdateMediaPropertiesAsync()
    {
        try
        {
            var winRtProps = await _session.TryGetMediaPropertiesAsync();
            if (winRtProps == null) return;

            BitmapImage? frozenBitmap = null;
            if (winRtProps.Thumbnail != null)
            {
                try
                {
                    using var stream = await winRtProps.Thumbnail.OpenReadAsync();
                    using var netStream = stream.AsStreamForRead();
                    using var memStream = new MemoryStream();
                    await netStream.CopyToAsync(memStream);
                    memStream.Position = 0;

                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.StreamSource = memStream;
                    bmp.EndInit();
                    bmp.Freeze();
                    frozenBitmap = bmp;
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "Could not decode thumbnail for media session '{SourceAppId}'.", SourceAppId);
                }
            }

            ThumbnailImage = frozenBitmap;
            Properties = new MediaPropertiesInfo(
                Title: winRtProps.Title ?? string.Empty,
                Artist: winRtProps.Artist ?? string.Empty,
                AlbumTitle: winRtProps.AlbumTitle ?? string.Empty,
                AlbumArtist: winRtProps.AlbumArtist ?? string.Empty,
                TrackNumber: winRtProps.TrackNumber,
                Genres: winRtProps.Genres != null ? winRtProps.Genres.ToList() : null,
                HasThumbnail: frozenBitmap != null);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Exception while retrieving media properties for '{SourceAppId}'.", SourceAppId);
        }
    }

    public async Task<bool> TryPlayAsync()
    {
        if (_isDisposed) return false;
        try
        {
            return await _session.TryPlayAsync();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "TryPlayAsync failed for '{SourceAppId}'.", SourceAppId);
            return false;
        }
    }

    public async Task<bool> TryPauseAsync()
    {
        if (_isDisposed) return false;
        try
        {
            return await _session.TryPauseAsync();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "TryPauseAsync failed for '{SourceAppId}'.", SourceAppId);
            return false;
        }
    }

    public async Task<bool> TryTogglePlayPauseAsync()
    {
        if (_isDisposed) return false;
        try
        {
            return await _session.TryTogglePlayPauseAsync();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "TryTogglePlayPauseAsync failed for '{SourceAppId}'.", SourceAppId);
            return false;
        }
    }

    public async Task<bool> TrySkipNextAsync()
    {
        if (_isDisposed) return false;
        try
        {
            return await _session.TrySkipNextAsync();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "TrySkipNextAsync failed for '{SourceAppId}'.", SourceAppId);
            return false;
        }
    }

    public async Task<bool> TrySkipPreviousAsync()
    {
        if (_isDisposed) return false;
        try
        {
            return await _session.TrySkipPreviousAsync();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "TrySkipPreviousAsync failed for '{SourceAppId}'.", SourceAppId);
            return false;
        }
    }

    public async Task<bool> TryChangePlaybackPositionAsync(TimeSpan position)
    {
        if (_isDisposed) return false;
        try
        {
            return await _session.TryChangePlaybackPositionAsync(position.Ticks);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "TryChangePlaybackPositionAsync failed for '{SourceAppId}' at {Position}.", SourceAppId, position);
            return false;
        }
    }

    private void NotifySessionClosedIfDead()
    {
        try
        {
            SessionClosed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error firing SessionClosed event for '{SourceAppId}'.", SourceAppId);
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        try
        {
            _session.MediaPropertiesChanged -= OnMediaPropertiesChanged;
            _session.PlaybackInfoChanged -= OnPlaybackInfoChanged;
            _session.TimelinePropertiesChanged -= OnTimelinePropertiesChanged;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Exception unsubscribing events on WinRtMediaSession '{SourceAppId}'.", SourceAppId);
        }

        MediaPropertiesChanged = null;
        PlaybackInfoChanged = null;
        TimelinePropertiesChanged = null;
        SessionClosed = null;
        ThumbnailImage = null;

        Log.Debug("WinRtMediaSession for '{SourceAppId}' successfully disposed and unsubscribed.", SourceAppId);
    }
}
