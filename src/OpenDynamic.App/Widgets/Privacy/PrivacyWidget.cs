using System.Windows.Controls;
using System.Windows.Threading;
using OpenDynamic.App.Services;
using OpenDynamic.App.Widgets.Privacy.Views;
using OpenDynamic.Core.Privacy;
using OpenDynamic.Core.Settings;
using OpenDynamic.Core.Widgets;
using Serilog;

namespace OpenDynamic.App.Widgets.Privacy;

/// <summary>
/// Transient widget displaying sensor access notifications (microphone / webcam started or stopped).
/// Operates strictly passively without polling (Regla de Oro 1) and without accessing hardware (Regla de Oro 10).
/// Registered with Priority 85 and auto-expires after configured transient duration (default 3 seconds).
/// </summary>
public sealed class PrivacyWidget : IslandWidgetBase
{
    public const string WidgetId = "privacy";

    private readonly IPrivacyAccessMonitor _monitor;
    private readonly AppSettings _settings;
    private readonly Dispatcher _dispatcher;

    private DispatcherTimer? _transientTimer;

    // Observable UI properties
    private PrivacyResourceType _lastResource = PrivacyResourceType.Microphone;
    private PrivacyAccessEventKind _lastEventKind = PrivacyAccessEventKind.Started;
    private string _title = "Privacidad";
    private string _subtitle = string.Empty;
    private string _appName = string.Empty;
    private bool _isMicrophone = true;
    private bool _isCamera;
    private bool _isStarted = true;

    public override string Id => WidgetId;

    public PrivacyResourceType LastResource
    {
        get => _lastResource;
        private set => SetProperty(ref _lastResource, value);
    }

    public PrivacyAccessEventKind LastEventKind
    {
        get => _lastEventKind;
        private set => SetProperty(ref _lastEventKind, value);
    }

    public string Title
    {
        get => _title;
        private set => SetProperty(ref _title, value);
    }

    public string Subtitle
    {
        get => _subtitle;
        private set => SetProperty(ref _subtitle, value);
    }

    public string AppName
    {
        get => _appName;
        private set => SetProperty(ref _appName, value);
    }

    public bool IsMicrophone
    {
        get => _isMicrophone;
        private set => SetProperty(ref _isMicrophone, value);
    }

    public bool IsCamera
    {
        get => _isCamera;
        private set => SetProperty(ref _isCamera, value);
    }

    public bool IsStarted
    {
        get => _isStarted;
        private set => SetProperty(ref _isStarted, value);
    }

    public PrivacyAccessState CurrentState => _monitor.CurrentState;

    public PrivacyWidget(
        IPrivacyAccessMonitor monitor,
        AppSettings settings,
        Dispatcher? dispatcher = null)
        : base(settings?.DefaultPrivacyPriority ?? ActivityPriority.Privacy)
    {
        _monitor = monitor ?? throw new ArgumentNullException(nameof(monitor));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _dispatcher = dispatcher ?? (System.Windows.Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher);
    }

    public override void Initialize()
    {
        base.Initialize();

        try
        {
            _monitor.AccessAlertTriggered += OnAccessAlertTriggered;
            _monitor.StateChanged += OnMonitorStateChanged;

            Log.Debug("PrivacyWidget initialized successfully.");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to initialize PrivacyWidget.");
        }
    }

    private void OnMonitorStateChanged(object? sender, PrivacyAccessState state)
    {
        _dispatcher.InvokeAsync(() =>
        {
            OnPropertyChanged(nameof(CurrentState));
        });
    }

    private void OnAccessAlertTriggered(object? sender, PrivacyAccessChange alert)
    {
        if (!_settings.EnablePrivacyAlerts)
        {
            return;
        }

        if (alert.Resource == PrivacyResourceType.Microphone && !_settings.EnableMicrophoneIndicator)
        {
            return;
        }

        if (alert.Resource == PrivacyResourceType.Camera && !_settings.EnableCameraIndicator)
        {
            return;
        }

        _dispatcher.InvokeAsync(() =>
        {
            LastResource = alert.Resource;
            LastEventKind = alert.EventKind;
            AppName = alert.AppName;
            IsMicrophone = alert.Resource == PrivacyResourceType.Microphone;
            IsCamera = alert.Resource == PrivacyResourceType.Camera;
            IsStarted = alert.EventKind == PrivacyAccessEventKind.Started;

            if (IsMicrophone)
            {
                if (IsStarted)
                {
                    Title = $"Micrófono en uso: {alert.AppName}";
                    Subtitle = "Micrófono activado";
                }
                else
                {
                    Title = "Micrófono liberado";
                    Subtitle = !string.IsNullOrWhiteSpace(alert.AppName) ? alert.AppName : "Dispositivo inactivo";
                }
            }
            else
            {
                if (IsStarted)
                {
                    Title = $"Cámara en uso: {alert.AppName}";
                    Subtitle = "Cámara activada";
                }
                else
                {
                    Title = "Cámara liberada";
                    Subtitle = !string.IsNullOrWhiteSpace(alert.AppName) ? alert.AppName : "Dispositivo inactivo";
                }
            }

            var duration = TimeSpan.FromSeconds(_settings.PrivacyTransientDurationSeconds > 0
                ? _settings.PrivacyTransientDurationSeconds
                : 3.0);

            int priority = _settings.DefaultPrivacyPriority > 0
                ? _settings.DefaultPrivacyPriority
                : ActivityPriority.Privacy;

            Log.Information("PrivacyWidget transient alert triggered: '{Title}' ({Subtitle}, Duration: {Duration}s, Priority: {Priority})",
                Title, Subtitle, duration.TotalSeconds, priority);

            Activate(transientDuration: duration, priorityOverride: priority);
            ResetTransientTimer(duration);
        });
    }

    private void ResetTransientTimer(TimeSpan duration)
    {
        _transientTimer?.Stop();
        _transientTimer = new DispatcherTimer
        {
            Interval = duration
        };
        _transientTimer.Tick += (s, e) =>
        {
            _transientTimer.Stop();
            _transientTimer = null;
            Log.Debug("PrivacyWidget transient lifespan expired. Deactivating.");
            Deactivate();
        };
        _transientTimer.Start();
    }

    public override UserControl? CreateCompactView() => new PrivacyCompactView { DataContext = this };

    public override UserControl? CreateExpandedView() => new PrivacyExpandedView { DataContext = this };

    public override UserControl? CreateSplitView() => new PrivacySplitView { DataContext = this };

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _transientTimer?.Stop();
            _transientTimer = null;
            _monitor.AccessAlertTriggered -= OnAccessAlertTriggered;
            _monitor.StateChanged -= OnMonitorStateChanged;
        }

        base.Dispose(disposing);
    }
}
