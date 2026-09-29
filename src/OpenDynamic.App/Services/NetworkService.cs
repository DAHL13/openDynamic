using Windows.Networking.Connectivity;
using OpenDynamic.Core.Network;
using OpenDynamic.Core.Settings;
using Serilog;

namespace OpenDynamic.App.Services;

/// <summary>
/// Service that monitors network status changes using WinRT <see cref="NetworkInformation"/> events (zero polling).
/// Feeds debounced events into pure <see cref="NetworkAlertPolicy"/> and fires alerts for user consumption.
/// Strictly adheres to Golden Rule 1 (0% CPU at rest) and Golden Rule 11 (releases all listeners when disabled).
/// </summary>
public sealed class NetworkService : IDisposable
{
    private readonly object _syncLock = new();
    private readonly AppSettings _settings;
    private readonly NetworkAlertPolicy _policy;

    private bool _isListening;
    private bool _isDisposed;

    /// <summary>
    /// Occurs when a consolidated network alert warrants user presentation.
    /// </summary>
    public event EventHandler<NetworkSnapshot>? NetworkAlertTriggered;

    public NetworkSnapshot CurrentSnapshot => _policy.CurrentSnapshot;
    public bool IsListening => _isListening;
    public NetworkAlertPolicy Policy => _policy;

    public NetworkService(AppSettings settings, NetworkAlertPolicy? policy = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _policy = policy ?? new NetworkAlertPolicy(
            TimeProvider.System,
            debounceDuration: TimeSpan.FromSeconds(1.0),
            cooldownDuration: TimeSpan.FromSeconds(5.0),
            suspendSuppressionDuration: TimeSpan.FromSeconds(10.0));

        _policy.AlertTriggered += OnPolicyAlertTriggered;
    }

    /// <summary>
    /// Starts network monitoring if enabled in settings. Initializes baseline snapshot quietly.
    /// </summary>
    public void Start()
    {
        lock (_syncLock)
        {
            if (_isDisposed || _isListening) return;

            if (!_settings.EnableNetworkAlerts)
            {
                Log.Debug("NetworkService.Start skipped because EnableNetworkAlerts is disabled.");
                return;
            }

            try
            {
                var initialSnapshot = QueryCurrentSnapshot();
                _policy.Initialize(initialSnapshot);

                NetworkInformation.NetworkStatusChanged += OnNetworkStatusChanged;
                _isListening = true;

                Log.Information("NetworkService started. Event listeners registered (State={State}, Type={Type}).",
                    initialSnapshot.State, initialSnapshot.Type);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to initialize NetworkInformation.NetworkStatusChanged listener.");
            }
        }
    }

    /// <summary>
    /// Stops network monitoring and releases event listeners immediately (Golden Rule 11).
    /// </summary>
    public void Stop()
    {
        lock (_syncLock)
        {
            if (!_isListening) return;

            try
            {
                NetworkInformation.NetworkStatusChanged -= OnNetworkStatusChanged;
                _isListening = false;
                _policy.NotifySuspended();

                Log.Information("NetworkService stopped. Event listeners unregistered.");
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Error unregistering NetworkInformation.NetworkStatusChanged listener.");
            }
        }
    }

    /// <summary>
    /// Notifies that the host system is suspending (sleep/hibernation).
    /// </summary>
    public void NotifySuspended()
    {
        _policy.NotifySuspended();
    }

    /// <summary>
    /// Notifies that the host system resumed from sleep.
    /// Suppresses network alerts for 10 seconds to avoid wakeup flurry notices.
    /// </summary>
    public void NotifyResumed()
    {
        _policy.NotifyResumedFromSuspend();

        // Refresh baseline in background without alert
        _ = Task.Run(() =>
        {
            try
            {
                var snapshot = QueryCurrentSnapshot();
                _policy.ProcessSnapshot(snapshot);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Error refreshing network snapshot after system resume.");
            }
        });
    }

    private void OnNetworkStatusChanged(object? sender)
    {
        try
        {
            var snapshot = QueryCurrentSnapshot();
            Log.Debug("WinRT NetworkStatusChanged event received: State={State}, Type={Type}",
                snapshot.State, snapshot.Type);

            _policy.ProcessSnapshot(snapshot);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Unexpected error processing WinRT NetworkStatusChanged event.");
        }
    }

    private void OnPolicyAlertTriggered(object? sender, NetworkSnapshot snapshot)
    {
        Log.Information("NetworkService emitting transient alert: State={State}, Type={Type}",
            snapshot.State, snapshot.Type);

        NetworkAlertTriggered?.Invoke(this, snapshot);
    }

    /// <summary>
    /// Queries the current network state using WinRT NetworkInformation.
    /// </summary>
    public NetworkSnapshot QueryCurrentSnapshot()
    {
        try
        {
            var profile = NetworkInformation.GetInternetConnectionProfile();
            if (profile == null)
            {
                return NetworkSnapshot.Disconnected;
            }

            var connectivity = profile.GetNetworkConnectivityLevel();
            if (connectivity != NetworkConnectivityLevel.InternetAccess &&
                connectivity != NetworkConnectivityLevel.ConstrainedInternetAccess &&
                connectivity != NetworkConnectivityLevel.LocalAccess)
            {
                return NetworkSnapshot.Disconnected;
            }

            string profileName = profile.ProfileName ?? "Red";
            NetworkType type = NetworkType.Other;

            if (profile.IsWlanConnectionProfile)
            {
                type = NetworkType.WiFi;
            }
            else if (profile.NetworkAdapter != null)
            {
                // IanaInterfaceType: 6 = Ethernet, 71 = 802.11 WiFi
                uint ianaType = profile.NetworkAdapter.IanaInterfaceType;
                if (ianaType == 6)
                {
                    type = NetworkType.Ethernet;
                }
                else if (ianaType == 71)
                {
                    type = NetworkType.WiFi;
                }
            }

            return new NetworkSnapshot(NetworkState.Connected, profileName, type);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to query WinRT NetworkInformation. Falling back to Disconnected.");
            return NetworkSnapshot.Disconnected;
        }
    }

    public void Dispose()
    {
        lock (_syncLock)
        {
            if (_isDisposed) return;
            _isDisposed = true;

            Stop();
            _policy.AlertTriggered -= OnPolicyAlertTriggered;
            _policy.Dispose();
            NetworkAlertTriggered = null;
        }
    }
}
