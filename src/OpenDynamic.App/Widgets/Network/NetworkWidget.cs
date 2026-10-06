using System.Windows.Controls;
using System.Windows.Threading;
using OpenDynamic.App.Services;
using OpenDynamic.App.Widgets.Network.Views;
using OpenDynamic.Core.Network;
using OpenDynamic.Core.Settings;
using Serilog;

namespace OpenDynamic.App.Widgets.Network;

/// <summary>
/// Transient widget displaying network connectivity transitions (Connected to network, Disconnected).
/// Registered with Priority 65 and auto-expires after 3 seconds.
/// Adheres strictly to Golden Rule 1 (0% CPU at rest, zero continuous timers).
/// </summary>
public sealed class NetworkWidget : IslandWidgetBase
{
    public const string WidgetId = "network";

    private readonly NetworkService _networkService;
    private readonly AppSettings _settings;
    private readonly Dispatcher _dispatcher;

    private DispatcherTimer? _transientTimer;

    // Observable UI properties
    private NetworkState _state = NetworkState.Disconnected;
    private NetworkType _type = NetworkType.Other;
    private string? _networkName;
    private string _title = "Sin conexión";
    private string _subtitle = "Red desconectada";

    public override string Id => WidgetId;

    public NetworkState State
    {
        get => _state;
        private set
        {
            if (SetProperty(ref _state, value))
            {
                OnPropertyChanged(nameof(IsConnected));
            }
        }
    }

    public NetworkType Type
    {
        get => _type;
        private set
        {
            if (SetProperty(ref _type, value))
            {
                OnPropertyChanged(nameof(IsWiFi));
                OnPropertyChanged(nameof(IsEthernet));
            }
        }
    }

    public string? NetworkName
    {
        get => _networkName;
        private set => SetProperty(ref _networkName, value);
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

    public bool IsConnected => State == NetworkState.Connected;
    public bool IsWiFi => Type == NetworkType.WiFi;
    public bool IsEthernet => Type == NetworkType.Ethernet;

    public NetworkWidget(
        NetworkService networkService,
        AppSettings settings,
        Dispatcher? dispatcher = null)
        : base(settings?.DefaultNetworkPriority ?? 65)
    {
        _networkService = networkService ?? throw new ArgumentNullException(nameof(networkService));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _dispatcher = dispatcher ?? (System.Windows.Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher);
    }

    public override void Initialize()
    {
        base.Initialize();

        try
        {
            var initial = _networkService.CurrentSnapshot;
            UpdatePropertiesFromSnapshot(initial);

            _networkService.NetworkAlertTriggered += OnNetworkAlertTriggered;

            Log.Debug("NetworkWidget initialized (State={State}, Type={Type})", State, Type);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to initialize NetworkWidget.");
        }
    }

    private void OnNetworkAlertTriggered(object? sender, NetworkSnapshot snapshot)
    {
        _dispatcher.InvokeAsync(() =>
        {
            UpdatePropertiesFromSnapshot(snapshot);

            var duration = TimeSpan.FromSeconds(_settings.NetworkTransientDurationSeconds > 0 ? _settings.NetworkTransientDurationSeconds : 3.0);

            // Privacy: do not log NetworkName/SSID
            Log.Information("NetworkWidget transient alert triggered: State={State}, Type={Type}, Duration={Duration}s",
                snapshot.State, snapshot.Type, duration.TotalSeconds);

            // Activate transient notice with priority 65
            Activate(transientDuration: duration, priorityOverride: _settings.DefaultNetworkPriority);
            ResetTransientTimer(duration);
        });
    }

    private void UpdatePropertiesFromSnapshot(NetworkSnapshot snapshot)
    {
        State = snapshot.State;
        Type = snapshot.Type;
        NetworkName = snapshot.NetworkName;

        if (snapshot.State == NetworkState.Connected)
        {
            string name = !string.IsNullOrWhiteSpace(snapshot.NetworkName) ? snapshot.NetworkName : "Red";
            Title = $"Conectado a {name}";
            Subtitle = snapshot.Type switch
            {
                NetworkType.WiFi => "Wi-Fi",
                NetworkType.Ethernet => "Ethernet",
                _ => "Conexión activa"
            };
        }
        else
        {
            Title = "Sin conexión";
            Subtitle = "Red desconectada";
        }
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
            Log.Debug("NetworkWidget transient lifespan expired. Deactivating.");
            Deactivate();
        };
        _transientTimer.Start();
    }

    public override UserControl? CreateCompactView() => new NetworkCompactView { DataContext = this };

    public override UserControl? CreateExpandedView() => new NetworkExpandedView { DataContext = this };

    public override UserControl? CreateSplitView() => new NetworkSplitView { DataContext = this };

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _transientTimer?.Stop();
            _transientTimer = null;
            _networkService.NetworkAlertTriggered -= OnNetworkAlertTriggered;
        }

        base.Dispose(disposing);
    }
}
