using System.Windows.Controls;
using System.Windows.Threading;
using OpenDynamic.App.Services;
using OpenDynamic.App.Widgets.EnergySaver.Views;
using OpenDynamic.Core.EnergySaver;
using OpenDynamic.Core.Settings;
using OpenDynamic.Core.Widgets;
using Serilog;

namespace OpenDynamic.App.Widgets.EnergySaver;

/// <summary>
/// Transient widget displaying Windows Energy Saver state transitions (Priority 88, 3s duration).
/// Strictly adheres to:
/// - Golden Rule 1: Zero continuous polling timers (active strictly when an alert is fired).
/// - Golden Rule 8: Transient auto-expiring activity.
/// </summary>
public sealed class EnergySaverWidget : IslandWidgetBase
{
    public const string WidgetId = "energy_saver";

    private readonly EnergySaverService _energySaverService;
    private readonly AppSettings _settings;
    private readonly Dispatcher _dispatcher;

    private DispatcherTimer? _transientTimer;

    private string _title = "Ahorro de energía";
    private string _subtitle = "Optimizando consumo";
    private bool _isEnergySaverOn;

    public override string Id => WidgetId;

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

    public bool IsEnergySaverOn
    {
        get => _isEnergySaverOn;
        private set => SetProperty(ref _isEnergySaverOn, value);
    }

    public EnergySaverWidget(
        EnergySaverService energySaverService,
        AppSettings settings,
        Dispatcher? dispatcher = null)
        : base(settings?.DefaultEnergySaverPriority ?? ActivityPriority.EnergySaver)
    {
        _energySaverService = energySaverService ?? throw new ArgumentNullException(nameof(energySaverService));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _dispatcher = dispatcher ?? (System.Windows.Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher);
    }

    public override void Initialize()
    {
        base.Initialize();

        try
        {
            _energySaverService.AlertTriggered += OnEnergySaverAlertTriggered;
            Log.Debug("EnergySaverWidget initialized and subscribed to EnergySaverService alerts.");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to initialize EnergySaverWidget.");
        }
    }

    private void OnEnergySaverAlertTriggered(object? sender, EnergySaverState state)
    {
        _dispatcher.InvokeAsync(() =>
        {
            if (!_settings.EnableEnergySaverAlerts)
            {
                Log.Debug("EnergySaverWidget: Alert suppressed by user settings (EnableEnergySaverAlerts=false).");
                return;
            }

            if (state == EnergySaverState.On)
            {
                Title = "Ahorro de energía activado";
                Subtitle = "Modo eficiente en ejecución";
                IsEnergySaverOn = true;
            }
            else if (state == EnergySaverState.Off)
            {
                Title = "Ahorro de energía desactivado";
                Subtitle = "Rendimiento estándar restaurado";
                IsEnergySaverOn = false;
            }
            else
            {
                return;
            }

            double durationSecs = _settings.EnergySaverTransientDurationSeconds > 0
                ? _settings.EnergySaverTransientDurationSeconds
                : 3.0;
            TimeSpan duration = TimeSpan.FromSeconds(durationSecs);

            Log.Information("EnergySaverWidget: Displaying transient alert '{Title}' for {Duration}s (Priority: {Priority})",
                Title, duration.TotalSeconds, _settings.DefaultEnergySaverPriority);

            Activate(transientDuration: duration, priorityOverride: _settings.DefaultEnergySaverPriority);
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
            Log.Debug("EnergySaverWidget: Transient lifespan expired. Deactivating.");
            Deactivate();
        };
        _transientTimer.Start();
    }

    public override UserControl? CreateCompactView() => new EnergySaverCompactView { DataContext = this };

    public override UserControl? CreateExpandedView() => new EnergySaverExpandedView { DataContext = this };

    public override UserControl? CreateSplitView() => new EnergySaverSplitView { DataContext = this };

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _transientTimer?.Stop();
            _transientTimer = null;
            _energySaverService.AlertTriggered -= OnEnergySaverAlertTriggered;
        }

        base.Dispose(disposing);
    }
}
