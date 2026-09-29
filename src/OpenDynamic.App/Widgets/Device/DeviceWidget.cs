using System.Windows.Controls;
using System.Windows.Threading;
using OpenDynamic.App.Services;
using OpenDynamic.App.Widgets.Device.Views;
using OpenDynamic.Core.Devices;
using OpenDynamic.Core.Settings;
using Serilog;

namespace OpenDynamic.App.Widgets.Device;

/// <summary>
/// Transient widget displaying USB and Bluetooth peripheral connection/disconnection alerts.
/// Registered with Priority 60 and auto-expires after 3 seconds.
/// Adheres strictly to Golden Rule 1 (0% CPU at rest, zero continuous timers) and v1.1 Privacy Rule.
/// </summary>
public sealed class DeviceWidget : IslandWidgetBase
{
    public const string WidgetId = "device";

    private readonly DeviceService _deviceService;
    private readonly AppSettings _settings;
    private readonly Dispatcher _dispatcher;

    private DispatcherTimer? _transientTimer;

    // Observable UI properties
    private DeviceEventType _eventType = DeviceEventType.Connected;
    private DeviceCategory _category = DeviceCategory.Other;
    private string _deviceName = "Dispositivo";
    private int? _batteryPercent;
    private string _title = "Dispositivo";
    private string _subtitle = "Conectado";

    public override string Id => WidgetId;

    public DeviceEventType EventType
    {
        get => _eventType;
        private set
        {
            if (SetProperty(ref _eventType, value))
            {
                OnPropertyChanged(nameof(IsConnected));
            }
        }
    }

    public DeviceCategory Category
    {
        get => _category;
        private set
        {
            if (SetProperty(ref _category, value))
            {
                OnPropertyChanged(nameof(IsAudio));
                OnPropertyChanged(nameof(IsKeyboard));
                OnPropertyChanged(nameof(IsMouse));
                OnPropertyChanged(nameof(IsStorage));
                OnPropertyChanged(nameof(IsOther));
            }
        }
    }

    public string DeviceName
    {
        get => _deviceName;
        private set => SetProperty(ref _deviceName, value);
    }

    public int? BatteryPercent
    {
        get => _batteryPercent;
        private set
        {
            if (SetProperty(ref _batteryPercent, value))
            {
                OnPropertyChanged(nameof(HasBattery));
            }
        }
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

    public bool IsConnected => EventType == DeviceEventType.Connected;
    public bool HasBattery => BatteryPercent.HasValue;

    public bool IsAudio => Category == DeviceCategory.Audio;
    public bool IsKeyboard => Category == DeviceCategory.Keyboard;
    public bool IsMouse => Category == DeviceCategory.Mouse;
    public bool IsStorage => Category == DeviceCategory.Storage;
    public bool IsOther => Category == DeviceCategory.Other;

    public DeviceWidget(
        DeviceService deviceService,
        AppSettings settings,
        Dispatcher? dispatcher = null)
        : base(settings?.DefaultDevicePriority ?? 60)
    {
        _deviceService = deviceService ?? throw new ArgumentNullException(nameof(deviceService));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _dispatcher = dispatcher ?? (System.Windows.Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher);
    }

    public override void Initialize()
    {
        base.Initialize();

        try
        {
            _deviceService.DeviceAlertTriggered += OnDeviceAlertTriggered;
            Log.Debug("DeviceWidget initialized.");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to initialize DeviceWidget.");
        }
    }

    private void OnDeviceAlertTriggered(object? sender, DeviceEvent devEvent)
    {
        _dispatcher.InvokeAsync(() =>
        {
            EventType = devEvent.Type;
            Category = devEvent.Category;
            DeviceName = devEvent.DeviceName;
            BatteryPercent = devEvent.BatteryPercent;

            Title = !string.IsNullOrWhiteSpace(devEvent.DeviceName) ? devEvent.DeviceName : "Dispositivo";

            string statusText = devEvent.Type == DeviceEventType.Connected ? "Conectado" : "Desconectado";
            if (devEvent.BatteryPercent.HasValue && devEvent.Type == DeviceEventType.Connected)
            {
                Subtitle = $"{statusText} • {devEvent.BatteryPercent.Value}%";
            }
            else
            {
                Subtitle = statusText;
            }

            var duration = TimeSpan.FromSeconds(_settings.DeviceTransientDurationSeconds > 0 ? _settings.DeviceTransientDurationSeconds : 3.0);

            // Privacy: do not log DeviceName
            Log.Information("DeviceWidget transient alert triggered: Category={Category}, Type={Type}, Duration={Duration}s",
                devEvent.Category, devEvent.Type, duration.TotalSeconds);

            // Activate transient notice with priority 60
            Activate(transientDuration: duration, priorityOverride: _settings.DefaultDevicePriority);
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
            Log.Debug("DeviceWidget transient lifespan expired. Deactivating.");
            Deactivate();
        };
        _transientTimer.Start();
    }

    public override UserControl? CreateCompactView() => new DeviceCompactView { DataContext = this };

    public override UserControl? CreateExpandedView() => new DeviceExpandedView { DataContext = this };

    public override UserControl? CreateSplitView() => new DeviceSplitView { DataContext = this };

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _transientTimer?.Stop();
            _transientTimer = null;
            _deviceService.DeviceAlertTriggered -= OnDeviceAlertTriggered;
        }

        base.Dispose(disposing);
    }
}
