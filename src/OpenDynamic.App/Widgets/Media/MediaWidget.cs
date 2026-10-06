using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using OpenDynamic.App.Services;
using OpenDynamic.App.Widgets.Media.Views;
using OpenDynamic.App.Widgets.Messages;
using OpenDynamic.Core.Audio.Spectrum;
using OpenDynamic.Core.EnergySaver;
using OpenDynamic.Core.Media;
using OpenDynamic.Core.Media.Color;
using OpenDynamic.Core.Media.Gestures;
using OpenDynamic.Core.Settings;
using OpenDynamic.Core.State;
using OpenDynamic.Core.Widgets;
using Serilog;

namespace OpenDynamic.App.Widgets.Media;

/// <summary>
/// Widget presenting media playback status and interactive controls via GSMTC.
/// Honors Golden Rule 1 (0% CPU at rest, progress extrapolation timer active ONLY when Expanded + Playing),
/// Golden Rule 4 (complete fault isolation), and configurable 10-second grace period on pause.
/// </summary>
public sealed class MediaWidget : IslandWidgetBase
{
    public const string WidgetId = "media";

    private readonly IMediaService _mediaService;
    private readonly AppSettings _settings;
    private readonly Dispatcher _dispatcher;
    private readonly IAudioSpectrumService? _spectrumService;
    private readonly IResourceProfileProvider? _resourceProfileProvider;

    private DispatcherTimer? _pauseGraceTimer;
    private DispatcherTimer? _progressExtrapolationTimer;
    private IMediaSession? _hookedSession;

    // Observable properties for UI Binding
    private string _trackTitle = "Sin reproducción";
    private string _trackArtist = "";
    private string _trackAlbum = "";
    private BitmapImage? _thumbnail;
    private bool _hasThumbnail;
    private bool _isPlaying;
    private TimeSpan _currentPosition = TimeSpan.Zero;
    private TimeSpan _duration = TimeSpan.Zero;
    private string _currentPositionFormatted = "0:00";
    private string _durationFormatted = "0:00";
    private double _progressRatio = 0.0;
    private bool _canPlay;
    private bool _canPause;
    private bool _canSkipPrevious;
    private bool _canSkipNext;
    private bool _canSeek;

    public override string Id => WidgetId;

    public string TrackTitle
    {
        get => _trackTitle;
        private set => SetProperty(ref _trackTitle, value);
    }

    public string TrackArtist
    {
        get => _trackArtist;
        private set => SetProperty(ref _trackArtist, value);
    }

    public string TrackAlbum
    {
        get => _trackAlbum;
        private set => SetProperty(ref _trackAlbum, value);
    }

    public BitmapImage? Thumbnail
    {
        get => _thumbnail;
        private set => SetProperty(ref _thumbnail, value);
    }

    public bool HasThumbnail
    {
        get => _hasThumbnail;
        private set => SetProperty(ref _hasThumbnail, value);
    }

    private readonly MediaColorService _colorService;
    private readonly SwipeGestureDetector _wheelGestureDetector;
    private readonly SwipeGestureDetector _dragGestureDetector;

    private SolidColorBrush _accentBrush = MediaColorService.DefaultAccentBrush;
    private Color _accentColor = Color.FromRgb(255, 255, 255);

    /// <summary>
    /// Frozen solid color brush derived dynamically from album cover artwork.
    /// Safe for cross-thread access and leak-free UI binding.
    /// </summary>
    public SolidColorBrush AccentBrush
    {
        get => _accentBrush;
        private set => SetProperty(ref _accentBrush, value);
    }

    /// <summary>
    /// Raw WPF Color matching the current album art accent.
    /// </summary>
    public Color AccentColor
    {
        get => _accentColor;
        private set => SetProperty(ref _accentColor, value);
    }

    /// <summary>
    /// Event raised when a horizontal swipe or scroll gesture is successfully triggered.
    /// </summary>
    public event EventHandler<SwipeGestureAction>? GestureTriggered;

    private bool _isDecorativeAllowed = true;

    /// <summary>
    /// Gets the attached audio spectrum analysis service, if available.
    /// </summary>
    public IAudioSpectrumService? SpectrumService => _spectrumService;

    /// <summary>
    /// Indicates whether active spectrum visualization should be rendered.
    /// </summary>
    public bool IsVisualizerActive =>
        _spectrumService != null &&
        _isDecorativeAllowed &&
        _isPlaying &&
        IsVisibleOnIsland &&
        (_spectrumService.IsCapturing || _spectrumService.ActiveMode == AudioVisualizerMode.Simulated);

    public bool IsPlaying
    {
        get => _isPlaying;
        private set
        {
            if (SetProperty(ref _isPlaying, value))
            {
                OnPropertyChanged(nameof(EqualizerVisibility));
                UpdateVisualizerState();
            }
        }
    }

    /// <summary>
    /// Indicates whether decorative animations (such as equalizer bars) are permitted.
    /// In reduced motion mode, decorative animations are suppressed (Task 4).
    /// </summary>
    public bool IsDecorativeAllowed
    {
        get => _isDecorativeAllowed;
        set
        {
            if (SetProperty(ref _isDecorativeAllowed, value))
            {
                OnPropertyChanged(nameof(EqualizerVisibility));
                UpdateVisualizerState();
            }
        }
    }

    /// <summary>
    /// Equalizer bars are visible ONLY if music is playing and visualizer is not disabled.
    /// </summary>
    public System.Windows.Visibility EqualizerVisibility
    {
        get
        {
            var effectiveMode = _resourceProfileProvider != null
                ? _resourceProfileProvider.CurrentProfile.VisualizerMode
                : _settings.VisualizerMode;

            return (_isPlaying && effectiveMode != AudioVisualizerMode.Disabled)
                ? System.Windows.Visibility.Visible
                : System.Windows.Visibility.Collapsed;
        }
    }

    public TimeSpan CurrentPosition
    {
        get => _currentPosition;
        private set => SetProperty(ref _currentPosition, value);
    }

    public TimeSpan Duration
    {
        get => _duration;
        private set => SetProperty(ref _duration, value);
    }

    public string CurrentPositionFormatted
    {
        get => _currentPositionFormatted;
        private set => SetProperty(ref _currentPositionFormatted, value);
    }

    public string DurationFormatted
    {
        get => _durationFormatted;
        private set => SetProperty(ref _durationFormatted, value);
    }

    public double ProgressRatio
    {
        get => _progressRatio;
        set
        {
            if (SetProperty(ref _progressRatio, value))
            {
                OnPropertyChanged(nameof(ProgressRatio));
            }
        }
    }

    public bool CanPlay
    {
        get => _canPlay;
        private set => SetProperty(ref _canPlay, value);
    }

    public bool CanPause
    {
        get => _canPause;
        private set => SetProperty(ref _canPause, value);
    }

    public bool CanSkipPrevious
    {
        get => _canSkipPrevious;
        private set => SetProperty(ref _canSkipPrevious, value);
    }

    public bool CanSkipNext
    {
        get => _canSkipNext;
        private set => SetProperty(ref _canSkipNext, value);
    }

    public bool CanSeek
    {
        get => _canSeek;
        private set => SetProperty(ref _canSeek, value);
    }

    // Commands for View Interaction
    public IRelayCommand TogglePlayPauseCommand { get; }
    public IRelayCommand SkipPreviousCommand { get; }
    public IRelayCommand SkipNextCommand { get; }
    public IRelayCommand ActivateAppCommand { get; }

    public MediaWidget(
        IMediaService mediaService,
        AppSettings? settings = null,
        Dispatcher? dispatcher = null,
        MediaColorService? colorService = null,
        IAudioSpectrumService? spectrumService = null,
        IResourceProfileProvider? resourceProfileProvider = null)
        : base(settings?.DefaultMediaPriority ?? ActivityPriority.Media)
    {
        _mediaService = mediaService ?? throw new ArgumentNullException(nameof(mediaService));
        _settings = settings ?? new AppSettings();
        _dispatcher = dispatcher ?? (System.Windows.Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher);
        _colorService = colorService ?? new MediaColorService();
        _spectrumService = spectrumService;
        _resourceProfileProvider = resourceProfileProvider;

        _wheelGestureDetector = new SwipeGestureDetector(threshold: _settings.MediaGestureSensitivity > 0 ? _settings.MediaGestureSensitivity : SwipeGestureDetector.DefaultWheelThreshold);
        _dragGestureDetector = new SwipeGestureDetector(threshold: SwipeGestureDetector.DefaultDragThreshold);

        TogglePlayPauseCommand = new AsyncRelayCommand(ExecuteTogglePlayPauseAsync);
        SkipPreviousCommand = new AsyncRelayCommand(ExecuteSkipPreviousAsync);
        SkipNextCommand = new AsyncRelayCommand(ExecuteSkipNextAsync);
        ActivateAppCommand = new RelayCommand(ExecuteActivateApp);

        WeakReferenceMessenger.Default.Register<MotionProfileChangedMessage>(this, (_, msg) =>
        {
            _dispatcher.InvokeAsync(() =>
            {
                IsDecorativeAllowed = msg.Profile.AllowDecorative;
            });
        });
    }

    public override void Initialize()
    {
        base.Initialize();

        if (_resourceProfileProvider != null)
        {
            _resourceProfileProvider.ResourceProfileChanged += OnResourceProfileChanged;
        }

        _mediaService.CurrentSessionChanged += OnMediaServiceCurrentSessionChanged;
        HookCurrentSession(_mediaService.CurrentSession);
    }

    private void OnMediaServiceCurrentSessionChanged(object? sender, IMediaSession? newSession)
    {
        _dispatcher.InvokeAsync(() =>
        {
            HookCurrentSession(newSession);
        });
    }

    private void HookCurrentSession(IMediaSession? session)
    {
        try
        {
            if (ReferenceEquals(_hookedSession, session)) return;

            // Clean up previous hooked session
            UnhookCurrentSession();

            _hookedSession = session;

            if (_hookedSession != null)
            {
                _hookedSession.PlaybackInfoChanged += OnSessionPlaybackInfoChanged;
                _hookedSession.MediaPropertiesChanged += OnSessionMediaPropertiesChanged;
                _hookedSession.TimelinePropertiesChanged += OnSessionTimelinePropertiesChanged;
                _hookedSession.SessionClosed += OnSessionClosed;

                UpdateSessionState();
            }
            else
            {
                HandleSessionTerminated();
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Fault isolation: Error hooking media session in MediaWidget.");
        }
    }

    private void UnhookCurrentSession()
    {
        try
        {
            if (_hookedSession != null)
            {
                _hookedSession.PlaybackInfoChanged -= OnSessionPlaybackInfoChanged;
                _hookedSession.MediaPropertiesChanged -= OnSessionMediaPropertiesChanged;
                _hookedSession.TimelinePropertiesChanged -= OnSessionTimelinePropertiesChanged;
                _hookedSession.SessionClosed -= OnSessionClosed;
                _hookedSession = null;
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Error unhooking current media session.");
            _hookedSession = null;
        }
    }

    private void OnSessionPlaybackInfoChanged(object? sender, EventArgs e)
    {
        _dispatcher.InvokeAsync(UpdateSessionState);
    }

    private void OnSessionMediaPropertiesChanged(object? sender, EventArgs e)
    {
        _dispatcher.InvokeAsync(UpdateSessionProperties);
    }

    private void OnSessionTimelinePropertiesChanged(object? sender, EventArgs e)
    {
        _dispatcher.InvokeAsync(UpdateSessionTimeline);
    }

    private void OnSessionClosed(object? sender, EventArgs e)
    {
        _dispatcher.InvokeAsync(HandleSessionTerminated);
    }

    private void UpdateSessionState()
    {
        try
        {
            if (!_settings.EnableMediaWidget)
            {
                HandleSessionTerminated();
                return;
            }

            var session = _hookedSession;
            if (session == null)
            {
                HandleSessionTerminated();
                return;
            }

            var status = session.PlaybackStatus;
            var info = session.PlaybackInfo;

            IsPlaying = status == MediaPlaybackStatus.Playing;
            if (info?.Capabilities != null)
            {
                CanPlay = info.Capabilities.CanPlay;
                CanPause = info.Capabilities.CanPause;
                CanSkipPrevious = info.Capabilities.CanSkipPrevious;
                CanSkipNext = info.Capabilities.CanSkipNext;
                CanSeek = info.Capabilities.CanSeek;
            }

            if (status == MediaPlaybackStatus.Playing)
            {
                // Cancel pause grace timer immediately
                CancelPauseGraceTimer();

                // Mark active
                IsActive = true;

                // Start progress extrapolation timer if in expanded mode
                EvaluateProgressTimerState();
            }
            else if (status == MediaPlaybackStatus.Paused)
            {
                // Stop progress extrapolation timer immediately when paused
                StopProgressTimer();

                if (IsActive)
                {
                    // Start 10-second grace timer
                    StartPauseGraceTimer();
                }
            }
            else if (status is MediaPlaybackStatus.Stopped or MediaPlaybackStatus.Closed)
            {
                HandleSessionTerminated();
                return;
            }

            UpdateSessionProperties();
            UpdateSessionTimeline();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Fault isolation: Error updating session state in MediaWidget.");
        }
    }

    private void UpdateSessionProperties()
    {
        try
        {
            var session = _hookedSession;
            if (session == null) return;

            var props = session.Properties;
            if (props == null) return;

            TrackTitle = string.IsNullOrWhiteSpace(props.Title) ? "Reproduciendo" : props.Title;
            TrackArtist = props.Artist ?? string.Empty;
            TrackAlbum = props.AlbumTitle ?? string.Empty;

            // Retrieve frozen BitmapImage from WinRtMediaSession if available
            if (session is WinRtMediaSession winRtSession)
            {
                Thumbnail = winRtSession.ThumbnailImage;
                HasThumbnail = winRtSession.ThumbnailImage != null;
            }
            else
            {
                Thumbnail = null;
                HasThumbnail = props.HasThumbnail;
            }

            CurrentActivity = new IslandActivity(
                Id: Id,
                Title: TrackTitle,
                Subtitle: TrackArtist,
                Priority: Priority);

            var trackKey = $"{TrackTitle}|{TrackArtist}";
            _ = UpdateAccentColorAsync(Thumbnail, trackKey);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Fault isolation: Error updating session properties in MediaWidget.");
        }
    }

    private async Task UpdateAccentColorAsync(BitmapImage? thumbnail, string trackKey)
    {
        try
        {
            if (!_settings.EnableDynamicMediaColor || thumbnail == null)
            {
                await _dispatcher.InvokeAsync(() =>
                {
                    AccentColor = Color.FromRgb(RgbColor.DefaultAccent.R, RgbColor.DefaultAccent.G, RgbColor.DefaultAccent.B);
                    AccentBrush = MediaColorService.DefaultAccentBrush;
                    WeakReferenceMessenger.Default.Send(new MediaAccentColorChangedMessage(null));
                });
                return;
            }

            var rgb = await _colorService.GetAccentColorAsync(thumbnail, trackKey);
            await _dispatcher.InvokeAsync(() =>
            {
                var color = Color.FromRgb(rgb.R, rgb.G, rgb.B);
                AccentColor = color;
                AccentBrush = MediaColorService.CreateFrozenBrush(rgb);
                WeakReferenceMessenger.Default.Send(new MediaAccentColorChangedMessage(color));
            });
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Fault isolation: Error in UpdateAccentColorAsync. Restoring fallback accent.");
            try
            {
                await _dispatcher.InvokeAsync(() =>
                {
                    AccentColor = Color.FromRgb(RgbColor.DefaultAccent.R, RgbColor.DefaultAccent.G, RgbColor.DefaultAccent.B);
                    AccentBrush = MediaColorService.DefaultAccentBrush;
                    WeakReferenceMessenger.Default.Send(new MediaAccentColorChangedMessage(null));
                });
            }
            catch
            {
                // Dispatcher shutdown
            }
        }
    }

    /// <summary>
    /// Handles horizontal mouse wheel tilt (WM_MOUSEHWHEEL) over the media capsule.
    /// Honors user sensitivity threshold, 400ms inertia cooldown, and player skip capabilities.
    /// </summary>
    public bool HandleWheelDelta(double delta)
    {
        if (!_settings.EnableMediaGestures) return false;

        _wheelGestureDetector.Threshold = _settings.MediaGestureSensitivity > 0
            ? _settings.MediaGestureSensitivity
            : SwipeGestureDetector.DefaultWheelThreshold;

        var action = _wheelGestureDetector.ProcessWheelDelta(delta);
        return ExecuteGestureAction(action);
    }

    /// <summary>
    /// Handles touch or mouse drag gestures over the artwork / title area in expanded view.
    /// Uses a minimum ~40 DIP threshold and 400ms cooldown without interfering with the seek bar.
    /// </summary>
    public bool HandleDragDelta(double deltaX)
    {
        if (!_settings.EnableMediaGestures) return false;

        var action = _dragGestureDetector.ProcessDragDelta(deltaX);
        return ExecuteGestureAction(action);
    }

    private bool ExecuteGestureAction(SwipeGestureAction action)
    {
        if (action == SwipeGestureAction.Next && CanSkipNext)
        {
            Log.Information("Media Next track gesture executed.");
            GestureTriggered?.Invoke(this, SwipeGestureAction.Next);
            SkipNextCommand.Execute(null);
            return true;
        }

        if (action == SwipeGestureAction.Previous && CanSkipPrevious)
        {
            Log.Information("Media Previous track gesture executed.");
            GestureTriggered?.Invoke(this, SwipeGestureAction.Previous);
            SkipPreviousCommand.Execute(null);
            return true;
        }

        return false;
    }

    private void UpdateSessionTimeline()
    {
        if (_hookedSession == null) return;

        var timeline = _hookedSession.TimelineInfo;
        Duration = timeline.EndTime;
        DurationFormatted = MediaProgressCalculator.FormatTime(Duration);

        UpdateExtrapolatedProgress();
    }

    /// <summary>
    /// Extrapolates current track position locally using <see cref="MediaProgressCalculator"/> without polling WinRT.
    /// </summary>
    public void UpdateExtrapolatedProgress()
    {
        if (_hookedSession == null)
        {
            CurrentPosition = TimeSpan.Zero;
            CurrentPositionFormatted = "0:00";
            ProgressRatio = 0.0;
            return;
        }

        var timeline = _hookedSession.TimelineInfo;
        var extrapolated = MediaProgressCalculator.CalculateCurrentPosition(
            timeline.Position,
            timeline.LastUpdatedUtc,
            timeline.StartTime,
            timeline.EndTime,
            IsPlaying,
            DateTimeOffset.UtcNow);

        CurrentPosition = extrapolated;
        CurrentPositionFormatted = MediaProgressCalculator.FormatTime(extrapolated);
        ProgressRatio = MediaProgressCalculator.CalculateProgressRatio(extrapolated, timeline.StartTime, timeline.EndTime);
    }

    private void StartPauseGraceTimer()
    {
        CancelPauseGraceTimer();

        var graceSeconds = Math.Max(1, _settings.MediaPauseGracePeriodSeconds);
        Log.Debug("Starting {Seconds}s grace timer for paused media session.", graceSeconds);

        _pauseGraceTimer = new DispatcherTimer(DispatcherPriority.Normal, _dispatcher)
        {
            Interval = TimeSpan.FromSeconds(graceSeconds)
        };
        _pauseGraceTimer.Tick += OnPauseGraceTimerTick;
        _pauseGraceTimer.Start();
    }

    private void OnPauseGraceTimerTick(object? sender, EventArgs e)
    {
        CancelPauseGraceTimer();
        Log.Information("Media pause grace period elapsed. Deactivating media activity.");
        IsActive = false;
        StopProgressTimer();
    }

    private void CancelPauseGraceTimer()
    {
        if (_pauseGraceTimer != null)
        {
            _pauseGraceTimer.Stop();
            _pauseGraceTimer.Tick -= OnPauseGraceTimerTick;
            _pauseGraceTimer = null;
        }
    }

    private void HandleSessionTerminated()
    {
        CancelPauseGraceTimer();
        StopProgressTimer();

        IsActive = false;
        IsPlaying = false;
        TrackTitle = "Sin reproducción";
        TrackArtist = string.Empty;
        TrackAlbum = string.Empty;
        Thumbnail = null;
        HasThumbnail = false;
        CurrentPosition = TimeSpan.Zero;
        Duration = TimeSpan.Zero;
        ProgressRatio = 0.0;
        CurrentPositionFormatted = "0:00";
        DurationFormatted = "0:00";
        AccentColor = Color.FromRgb(RgbColor.DefaultAccent.R, RgbColor.DefaultAccent.G, RgbColor.DefaultAccent.B);
        AccentBrush = MediaColorService.DefaultAccentBrush;
        WeakReferenceMessenger.Default.Send(new MediaAccentColorChangedMessage(null));

        _wheelGestureDetector.ResetAll();
        _dragGestureDetector.ResetAll();

        Log.Information("Media session terminated or removed. Widget set to inactive.");
        UpdateVisualizerState();
    }

    private bool _isFullscreenSuppressed;

    /// <summary>
    /// Updates fullscreen suppression state and refreshes the audio visualizer activation context.
    /// </summary>
    public void SetFullscreenSuppressed(bool isSuppressed)
    {
        if (_isFullscreenSuppressed == isSuppressed) return;
        _isFullscreenSuppressed = isSuppressed;
        UpdateVisualizerState();
    }

    /// <summary>
    /// Applies live changes to media widget settings (EnableMediaWidget, DefaultMediaPriority, VisualizerMode).
    /// </summary>
    public void ApplySettingsLive()
    {
        Priority = _settings.DefaultMediaPriority;
        if (!_settings.EnableMediaWidget)
        {
            HandleSessionTerminated();
        }
        else
        {
            UpdateSessionState();
            UpdateVisualizerState();
        }
    }

    /// <summary>
    /// Golden Rule 1: The progress timer MUST run ONLY when Expanded AND Playing.
    /// In Compact, Split, Hidden or Paused, it must stop immediately.
    /// </summary>
    private void EvaluateProgressTimerState()
    {
        if (DisplayMode == WidgetDisplayMode.Expanded && IsPlaying)
        {
            StartProgressTimer();
        }
        else
        {
            StopProgressTimer();
        }
    }

    private void StartProgressTimer()
    {
        if (_progressExtrapolationTimer == null)
        {
            _progressExtrapolationTimer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher)
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _progressExtrapolationTimer.Tick += OnProgressTimerTick;
        }

        if (!_progressExtrapolationTimer.IsEnabled)
        {
            _progressExtrapolationTimer.Start();
            Log.Debug("Media progress extrapolation timer started (Expanded + Playing).");
        }
    }

    private void StopProgressTimer()
    {
        if (_progressExtrapolationTimer != null && _progressExtrapolationTimer.IsEnabled)
        {
            _progressExtrapolationTimer.Stop();
            Log.Debug("Media progress extrapolation timer stopped (CPU ~0% at rest).");
        }
    }

    private void OnProgressTimerTick(object? sender, EventArgs e)
    {
        UpdateExtrapolatedProgress();
    }

    public override void SetDisplayState(WidgetDisplayMode mode, bool isVisible)
    {
        base.SetDisplayState(mode, isVisible);
        UpdateVisualizerState();
    }

    public override void OnExpand()
    {
        base.OnExpand();
        DisplayMode = WidgetDisplayMode.Expanded;
        UpdateExtrapolatedProgress();
        EvaluateProgressTimerState();
        UpdateVisualizerState();
    }

    public override void OnCollapse()
    {
        base.OnCollapse();
        DisplayMode = WidgetDisplayMode.Compact;
        StopProgressTimer();
        UpdateVisualizerState();
    }

    private void OnResourceProfileChanged(object? sender, ResourceProfile profile)
    {
        _dispatcher.InvokeAsync(() =>
        {
            UpdateVisualizerState();
        });
    }

    public void UpdateVisualizerState()
    {
        if (_spectrumService == null) return;

        IslandState state = !IsVisibleOnIsland
            ? IslandState.Hidden
            : (DisplayMode == WidgetDisplayMode.Expanded
                ? IslandState.Expanded
                : (DisplayMode == WidgetDisplayMode.Split ? IslandState.Split : IslandState.Compact));

        var effectiveMode = _resourceProfileProvider != null
            ? _resourceProfileProvider.CurrentProfile.VisualizerMode
            : _settings.VisualizerMode;

        var context = new VisualizerActivationContext(
            Mode: effectiveMode,
            IsMediaPlaying: _isPlaying && _settings.EnableMediaWidget,
            IsMediaWidgetVisible: IsVisibleOnIsland && _settings.EnableMediaWidget,
            IslandState: state,
            IsFullscreenSuppressed: _isFullscreenSuppressed);

        _spectrumService.UpdateActivation(in context);
        OnPropertyChanged(nameof(IsVisualizerActive));
        OnPropertyChanged(nameof(EqualizerVisibility));
    }

    public async Task SeekToRatioAsync(double ratio)
    {
        if (_hookedSession == null || Duration <= TimeSpan.Zero) return;

        var clampedRatio = Math.Clamp(ratio, 0.0, 1.0);
        var targetPosition = TimeSpan.FromMilliseconds(Duration.TotalMilliseconds * clampedRatio);

        try
        {
            await _hookedSession.TryChangePlaybackPositionAsync(targetPosition);
            CurrentPosition = targetPosition;
            CurrentPositionFormatted = MediaProgressCalculator.FormatTime(targetPosition);
            ProgressRatio = clampedRatio;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to seek media to ratio {Ratio}.", ratio);
        }
    }

    private async Task ExecuteTogglePlayPauseAsync()
    {
        if (_hookedSession == null) return;
        try
        {
            await _hookedSession.TryTogglePlayPauseAsync();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to toggle play/pause.");
        }
    }

    private async Task ExecuteSkipPreviousAsync()
    {
        if (_hookedSession == null) return;
        try
        {
            await _hookedSession.TrySkipPreviousAsync();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to skip previous.");
        }
    }

    private async Task ExecuteSkipNextAsync()
    {
        if (_hookedSession == null) return;
        try
        {
            await _hookedSession.TrySkipNextAsync();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to skip next.");
        }
    }

    private void ExecuteActivateApp()
    {
        if (_hookedSession == null) return;
        try
        {
            _mediaService.TryActivateApp(_hookedSession.SourceAppId);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to activate source media app '{SourceAppId}'.", _hookedSession.SourceAppId);
        }
    }

    public override UserControl? CreateCompactView() => new MediaCompactView(this);

    public override UserControl? CreateExpandedView() => new MediaExpandedView(this);

    public override UserControl? CreateSplitView() => new MediaSplitView(this);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (_resourceProfileProvider != null)
            {
                _resourceProfileProvider.ResourceProfileChanged -= OnResourceProfileChanged;
            }

            CancelPauseGraceTimer();
            StopProgressTimer();

            if (_progressExtrapolationTimer != null)
            {
                _progressExtrapolationTimer.Tick -= OnProgressTimerTick;
                _progressExtrapolationTimer = null;
            }

            _mediaService.CurrentSessionChanged -= OnMediaServiceCurrentSessionChanged;
            UnhookCurrentSession();
            _spectrumService?.Dispose();
        }

        base.Dispose(disposing);
    }
}
