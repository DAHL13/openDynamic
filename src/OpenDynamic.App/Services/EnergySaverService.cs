using System.Windows.Threading;
using CommunityToolkit.Mvvm.Messaging;
using OpenDynamic.Core.EnergySaver;
using OpenDynamic.Core.Settings;
using Serilog;

namespace OpenDynamic.App.Services;

/// <summary>
/// Monitors Windows Energy Saver status using the reactive WinRT PowerManager.EnergySaverStatusChanged event.
/// Implements <see cref="IResourceProfileProvider"/> to compute and notify real-time resource profiles.
/// Strictly adheres to:
/// - Golden Rule 1: 0% CPU at rest (ZERO continuous polling timers, purely event-driven).
/// - Golden Rule 4: Isolated fault handling (all WinRT calls wrapped in try/catch with structured logging).
/// - Graceful fallback: Desktop PCs and unsupported hardware cleanly map to <see cref="EnergySaverState.NotSupported"/>.
/// </summary>
public sealed class EnergySaverService : IResourceProfileProvider, IDisposable
{
    private readonly object _syncLock = new();
    private readonly AppSettings _settings;
    private readonly ISettingsService? _settingsService;
    private readonly Dispatcher _dispatcher;
    private readonly EnergySaverAlertPolicy _alertPolicy;

    private EnergySaverState _currentState = EnergySaverState.Unknown;
    private ResourceProfile _currentProfile = ResourceProfile.Standard;
    private bool _isWinRtSubscribed;
    private bool _isDisposed;

    /// <summary>
    /// Gets the current energy saver state of the host operating system.
    /// </summary>
    public EnergySaverState CurrentState
    {
        get
        {
            lock (_syncLock) return _currentState;
        }
    }

    /// <inheritdoc />
    public ResourceProfile CurrentProfile
    {
        get
        {
            lock (_syncLock) return _currentProfile;
        }
    }

    /// <summary>
    /// Event triggered when an energy saver transition warrants a visual island notice.
    /// </summary>
    public event EventHandler<EnergySaverState>? AlertTriggered;

    /// <summary>
    /// Event raised whenever the underlying energy saver state changes.
    /// </summary>
    public event EventHandler<EnergySaverState>? StateChanged;

    /// <inheritdoc />
    public event EventHandler<ResourceProfile>? ResourceProfileChanged;

    public EnergySaverService(
        AppSettings settings,
        ISettingsService? settingsService = null,
        Dispatcher? dispatcher = null,
        TimeProvider? timeProvider = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _settingsService = settingsService;
        _dispatcher = dispatcher ?? (System.Windows.Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher);
        _alertPolicy = new EnergySaverAlertPolicy(timeProvider ?? TimeProvider.System);

        _alertPolicy.AlertTriggered += OnAlertPolicyTriggered;

        if (_settingsService != null)
        {
            _settingsService.SettingsChanged += OnSettingsChanged;
        }

        WeakReferenceMessenger.Default.Register<Widgets.Messages.EnergySaverStatusChangedMessage>(this, (_, msg) =>
        {
            if (_dispatcher.CheckAccess())
            {
                HandleStatusChanged(msg.State);
            }
            else
            {
                _dispatcher.InvokeAsync(() => HandleStatusChanged(msg.State));
            }
        });

        Initialize();
    }

    private void Initialize()
    {
        lock (_syncLock)
        {
            // Initial query: baseline setup without triggering startup alerts
            _currentState = QueryPlatformEnergySaverState();
            _alertPolicy.Initialize(_currentState);
            _currentProfile = ComputeCurrentProfile(_currentState);

            Log.Information("EnergySaverService initialized. Initial state: {State}, Efficient mode active: {IsEfficient}",
                _currentState, _currentProfile.IsEfficientModeActive);

            SubscribeWinRtEvents();
        }
    }

    private void SubscribeWinRtEvents()
    {
        try
        {
            Windows.System.Power.PowerManager.EnergySaverStatusChanged += OnWinRtEnergySaverStatusChanged;
            _isWinRtSubscribed = true;
            Log.Debug("EnergySaverService: Subscribed to Windows.System.Power.PowerManager.EnergySaverStatusChanged.");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to subscribe to WinRT PowerManager.EnergySaverStatusChanged. Energy saver monitoring disabled.");
            _currentState = EnergySaverState.NotSupported;
            _alertPolicy.ProcessState(EnergySaverState.NotSupported);
            _currentProfile = ComputeCurrentProfile(EnergySaverState.NotSupported);
        }
    }

    private void OnWinRtEnergySaverStatusChanged(object? sender, object e)
    {
        Log.Debug("EnergySaverStatusChanged WinRT event received.");

        // Dispatch state reading and policy processing
        if (_dispatcher.CheckAccess())
        {
            HandleStatusChanged();
        }
        else
        {
            _dispatcher.InvokeAsync(() => HandleStatusChanged());
        }
    }

    /// <summary>
    /// Evaluates current platform energy saver status and updates active profiles.
    /// Accepts an explicit state from Win32 WM_POWERBROADCAST notifications,
    /// falling back to querying platform status if not provided.
    /// </summary>
    public void HandleStatusChanged(EnergySaverState? explicitState = null)
    {
        EnergySaverState newState;
        ResourceProfile newProfile;
        bool stateChanged = false;
        bool profileChanged = false;

        lock (_syncLock)
        {
            if (_isDisposed) return;

            newState = explicitState ?? QueryPlatformEnergySaverState();
            if (newState != _currentState)
            {
                _currentState = newState;
                stateChanged = true;
            }

            _alertPolicy.ProcessState(newState);

            newProfile = ComputeCurrentProfile(newState);
            if (!newProfile.Equals(_currentProfile))
            {
                _currentProfile = newProfile;
                profileChanged = true;
            }
        }

        if (stateChanged)
        {
            Log.Information("EnergySaverService: Operating system energy saver state changed to {NewState}", newState);
            StateChanged?.Invoke(this, newState);
        }

        if (profileChanged)
        {
            Log.Information("EnergySaverService: ResourceProfile changed (Efficient: {IsEfficient}, Motion: {Motion}, Visualizer: {Visualizer}, Hardware: {Interval}s)",
                newProfile.IsEfficientModeActive, newProfile.MotionProfile.Stiffness, newProfile.VisualizerMode, newProfile.HardwareSamplingInterval.TotalSeconds);
            ResourceProfileChanged?.Invoke(this, newProfile);
        }
    }

    /// <summary>
    /// Explicitly updates the energy saver state from a native notification (e.g. Win32 WM_POWERBROADCAST).
    /// </summary>
    public void UpdateEnergySaverState(EnergySaverState newState)
    {
        HandleStatusChanged(newState);
    }

    private void OnAlertPolicyTriggered(object? sender, EnergySaverState state)
    {
        Log.Information("EnergySaverService: Emitting energy saver transition alert for state {State}", state);
        AlertTriggered?.Invoke(this, state);
    }

    private void OnSettingsChanged(object? sender, AppSettings newSettings)
    {
        ReevaluateProfile();
    }

    /// <summary>
    /// Explicitly recalculates the active resource profile based on current settings and system energy state.
    /// </summary>
    public void ReevaluateProfile()
    {
        ResourceProfile newProfile;
        bool profileChanged = false;

        lock (_syncLock)
        {
            if (_isDisposed) return;

            newProfile = ComputeCurrentProfile(_currentState);
            if (!newProfile.Equals(_currentProfile))
            {
                _currentProfile = newProfile;
                profileChanged = true;
            }
        }

        if (profileChanged)
        {
            Log.Information("EnergySaverService: ResourceProfile updated following settings change.");
            ResourceProfileChanged?.Invoke(this, newProfile);
        }
    }

    /// <summary>
    /// Notifies that the host system is suspending (sleep/hibernation).
    /// </summary>
    public void NotifySuspended()
    {
        lock (_syncLock)
        {
            _alertPolicy.NotifySuspended();
        }
    }

    /// <summary>
    /// Notifies that the host system resumed from sleep.
    /// Suppresses energy saver alerts for 10 seconds to avoid wakeup burst notifications.
    /// </summary>
    public void NotifyResumed()
    {
        lock (_syncLock)
        {
            _alertPolicy.NotifyResumedFromSuspend();
        }

        // Re-read status quietly after wakeup
        HandleStatusChanged();
    }

    /// <summary>
    /// Queries the native WinRT PowerManager for current energy saver status.
    /// Safe against exceptions and desktop hardware without battery.
    /// </summary>
    private static EnergySaverState QueryPlatformEnergySaverState()
    {
        try
        {
            var status = Windows.System.Power.PowerManager.EnergySaverStatus;
            return EnergySaverStateMapper.FromWinRt((int)status);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to query PowerManager.EnergySaverStatus. Defaulting to NotSupported.");
            return EnergySaverState.NotSupported;
        }
    }

    private ResourceProfile ComputeCurrentProfile(EnergySaverState state)
    {
        bool systemAnimations = true;
        try
        {
            systemAnimations = System.Windows.SystemParameters.ClientAreaAnimation;
        }
        catch
        {
            systemAnimations = true;
        }

        return ResourceProfilePolicy.Resolve(
            energySaverState: state,
            enableEfficientMode: _settings.EnableEnergySaverEfficientMode,
            configuredMotionMode: _settings.MotionMode,
            systemAnimationsEnabled: systemAnimations,
            configuredVisualizerMode: _settings.VisualizerMode,
            configuredHardwareInterval: TimeSpan.FromSeconds(_settings.HardwareSamplingIntervalSeconds),
            reduceAnimationsEnabled: _settings.EnergySaverReduceAnimations,
            capAudioVisualizerEnabled: _settings.EnergySaverCapAudioVisualizer,
            throttleHardwareEnabled: _settings.EnergySaverThrottleHardwareSampling,
            efficientHardwareInterval: TimeSpan.FromSeconds(_settings.EnergySaverHardwareSamplingIntervalSeconds));
    }

    public void Dispose()
    {
        lock (_syncLock)
        {
            if (_isDisposed) return;
            _isDisposed = true;

            if (_isWinRtSubscribed)
            {
                try
                {
                    Windows.System.Power.PowerManager.EnergySaverStatusChanged -= OnWinRtEnergySaverStatusChanged;
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Error unregistering PowerManager.EnergySaverStatusChanged.");
                }
                _isWinRtSubscribed = false;
            }

            _alertPolicy.AlertTriggered -= OnAlertPolicyTriggered;

            WeakReferenceMessenger.Default.Unregister<Widgets.Messages.EnergySaverStatusChangedMessage>(this);

            if (_settingsService != null)
            {
                _settingsService.SettingsChanged -= OnSettingsChanged;
            }

            AlertTriggered = null;
            StateChanged = null;
            ResourceProfileChanged = null;
        }
    }
}
