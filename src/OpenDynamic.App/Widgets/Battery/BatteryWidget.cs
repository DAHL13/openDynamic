using System.Windows.Controls;
using System.Windows.Threading;
using OpenDynamic.App.Widgets.Battery.Views;
using OpenDynamic.Core.Power;
using OpenDynamic.Core.Settings;
using Serilog;

namespace OpenDynamic.App.Widgets.Battery;

/// <summary>
/// Transient widget displaying battery alerts (charger connection/disconnection and single-shot low/critical warnings).
/// Registered with high priority (Priority 90) and auto-expires after 3 seconds.
/// Adheres to Golden Rule 1 (zero continuous polling).
/// </summary>
public sealed class BatteryWidget : IslandWidgetBase
{
    public const string WidgetId = "battery";

    private readonly IBatteryMonitor _batteryMonitor;
    private readonly AppSettings _settings;
    private readonly Dispatcher _dispatcher;

    private DispatcherTimer? _transientTimer;

    // Observable UI properties
    private int _percent = 100;
    private bool _isCharging;
    private bool _hasBattery = true;
    private BatteryAlertKind _alertKind = BatteryAlertKind.None;
    private string _title = "Batería";
    private string _subtitle = "100%";

    public override string Id => WidgetId;

    public int Percent
    {
        get => _percent;
        private set => SetProperty(ref _percent, value);
    }

    public bool IsCharging
    {
        get => _isCharging;
        private set => SetProperty(ref _isCharging, value);
    }

    public bool HasBattery
    {
        get => _hasBattery;
        private set => SetProperty(ref _hasBattery, value);
    }

    public BatteryAlertKind AlertKind
    {
        get => _alertKind;
        private set => SetProperty(ref _alertKind, value);
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

    public BatteryWidget(
        IBatteryMonitor batteryMonitor,
        AppSettings settings,
        Dispatcher? dispatcher = null)
        : base(settings?.DefaultBatteryPriority ?? 90)
    {
        _batteryMonitor = batteryMonitor ?? throw new ArgumentNullException(nameof(batteryMonitor));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _dispatcher = dispatcher ?? (System.Windows.Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher);
    }

    public override void Initialize()
    {
        base.Initialize();

        try
        {
            var initial = _batteryMonitor.CurrentStatus;
            _percent = initial.Percent;
            _isCharging = initial.IsCharging;
            _hasBattery = initial.HasBattery;
            _subtitle = $"{_percent}%";

            _batteryMonitor.AlertTriggered += OnAlertTriggered;
            _batteryMonitor.StatusChanged += OnStatusChanged;

            Log.Debug("BatteryWidget initialized (Battery: {HasBattery}, Percent: {Percent}%, Charging: {Charging})",
                _hasBattery, _percent, _isCharging);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to initialize BatteryWidget.");
        }
    }

    private void OnStatusChanged(object? sender, BatterySnapshot snapshot)
    {
        _dispatcher.InvokeAsync(() =>
        {
            Percent = snapshot.Percent;
            IsCharging = snapshot.IsCharging;
            HasBattery = snapshot.HasBattery;
        });
    }

    private void OnAlertTriggered(object? sender, BatteryAlertEventArgs e)
    {
        _dispatcher.InvokeAsync(() =>
        {
            Percent = e.Snapshot.Percent;
            IsCharging = e.Snapshot.IsCharging;
            HasBattery = e.Snapshot.HasBattery;
            AlertKind = e.AlertKind;

            TimeSpan duration;

            switch (e.AlertKind)
            {
                case BatteryAlertKind.ChargerConnected:
                    Title = "Cargador conectado";
                    Subtitle = $"{Percent}% cargando";
                    duration = TimeSpan.FromSeconds(_settings.BatteryChargerTransientDurationSeconds);
                    break;

                case BatteryAlertKind.ChargerDisconnected:
                    Title = "Cargador desconectado";
                    Subtitle = $"{Percent}% restante";
                    duration = TimeSpan.FromSeconds(_settings.BatteryChargerTransientDurationSeconds);
                    break;

                case BatteryAlertKind.LowBattery:
                    Title = "Batería baja";
                    Subtitle = $"{Percent}% restante";
                    duration = TimeSpan.FromSeconds(_settings.BatteryWarningTransientDurationSeconds);
                    break;

                case BatteryAlertKind.CriticalBattery:
                    Title = "Batería muy baja";
                    Subtitle = $"Conecta el cargador ({Percent}%)";
                    duration = TimeSpan.FromSeconds(_settings.BatteryWarningTransientDurationSeconds);
                    break;

                default:
                    Title = "Batería";
                    Subtitle = $"{Percent}%";
                    duration = TimeSpan.FromSeconds(3.0);
                    break;
            }

            Log.Information("BatteryWidget triggered transient alert: '{Title}' ({Subtitle}, Duration: {Duration}s)",
                Title, Subtitle, duration.TotalSeconds);

            // Activate transient notice with priority 90
            Activate(transientDuration: duration, priorityOverride: _settings.DefaultBatteryPriority);
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
            Log.Debug("BatteryWidget transient lifespan expired. Deactivating.");
            Deactivate();
        };
        _transientTimer.Start();
    }

    public override UserControl? CreateCompactView() => new BatteryCompactView { DataContext = this };

    public override UserControl? CreateExpandedView() => new BatteryExpandedView { DataContext = this };

    public override UserControl? CreateSplitView() => new BatterySplitView { DataContext = this };

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _transientTimer?.Stop();
            _transientTimer = null;
            _batteryMonitor.AlertTriggered -= OnAlertTriggered;
            _batteryMonitor.StatusChanged -= OnStatusChanged;
        }

        base.Dispose(disposing);
    }
}
