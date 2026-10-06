using System.Windows.Threading;
using CommunityToolkit.Mvvm.Messaging;
using OpenDynamic.App.Native;
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
    private IntPtr _wnfOverrideSubscription = IntPtr.Zero;
    private IntPtr _wnfStateSubscription = IntPtr.Zero;
    private NativeMethods.WnfCallback? _wnfCallback;
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
        _alertPolicy = new EnergySaverAlertPolicy(timeProvider ?? TimeProvider.System, cooldownDuration: TimeSpan.FromSeconds(5));

        _alertPolicy.AlertTriggered += OnAlertPolicyTriggered;

        if (_settingsService != null)
        {
            _settingsService.SettingsChanged += OnSettingsChanged;
        }

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

        SubscribeWnfEvents();
    }

    private void SubscribeWnfEvents()
    {
        try
        {
            _wnfCallback = OnWnfStateChanged;

            int r1 = NativeMethods.RtlSubscribeWnfStateChangeNotification(
                out _wnfOverrideSubscription,
                NativeMethods.WNF_PO_ENERGY_SAVER_OVERRIDE,
                0,
                _wnfCallback,
                IntPtr.Zero,
                IntPtr.Zero,
                0,
                0);

            int r2 = NativeMethods.RtlSubscribeWnfStateChangeNotification(
                out _wnfStateSubscription,
                NativeMethods.WNF_PO_ENERGY_SAVER_STATE,
                0,
                _wnfCallback,
                IntPtr.Zero,
                IntPtr.Zero,
                0,
                0);

            Log.Information("EnergySaverService: Subscribed to Windows Notification Facility (WNF Override: 0x{R1:X8}, WNF State: 0x{R2:X8}).", r1, r2);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to subscribe to WNF energy saver notifications.");
        }
    }

    private uint OnWnfStateChanged(
        ulong stateName,
        uint changeStamp,
        IntPtr typeId,
        IntPtr callbackContext,
        IntPtr buffer,
        uint bufferSize)
    {
        Log.Debug("EnergySaverService: WNF state change received (StateName: 0x{StateName:X16}, Stamp: {Stamp})", stateName, changeStamp);

        if (_dispatcher.CheckAccess())
        {
            HandleStatusChanged();
        }
        else
        {
            _dispatcher.InvokeAsync(() => HandleStatusChanged());
        }

        return 0;
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
            Log.Warning(ex, "Failed to subscribe to WinRT PowerManager.EnergySaverStatusChanged. Relying on Win32 power notifications.");
            if (!CheckHasSystemBattery())
            {
                _currentState = EnergySaverState.NotSupported;
                _alertPolicy.ProcessState(EnergySaverState.NotSupported);
                _currentProfile = ComputeCurrentProfile(EnergySaverState.NotSupported);
            }
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
            WeakReferenceMessenger.Default.Send(new Widgets.Messages.EnergySaverStatusChangedMessage(newState));
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
    /// Checks whether the system possesses a physical battery using native Win32 GetSystemPowerStatus.
    /// BatteryFlag 128 (0x80) indicates no system battery (desktop PC).
    /// BatteryLifePercent 255 indicates unknown/no battery.
    /// </summary>
    public static bool CheckHasSystemBattery()
    {
        try
        {
            if (NativeMethods.GetSystemPowerStatus(out var rawStatus))
            {
                return (rawStatus.BatteryFlag & 128) == 0 && rawStatus.BatteryLifePercent != 255;
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to query GetSystemPowerStatus for battery presence.");
        }

        return false;
    }

    /// <summary>
    /// Live query of Windows energy saver (battery saver) status via WNF (Windows 11) and Win32 GetSystemPowerStatus (Windows 10/11).
    /// WNF_PO_ENERGY_SAVER_OVERRIDE == 1 indicates user explicitly activated Energy Saver via Quick Settings / Settings.
    /// WNF_PO_ENERGY_SAVER_OVERRIDE == 2 indicates user explicitly turned Energy Saver off.
    /// WNF_PO_ENERGY_SAVER_STATE == 2 or SystemStatusFlag == 1 indicates automatic Battery Saver engagement.
    /// </summary>
    public static EnergySaverState QueryLiveEnergySaverState()
    {
        if (!CheckHasSystemBattery()) return EnergySaverState.NotSupported;

        int? wnfOverride = null;
        int? wnfState = null;
        byte systemStatusFlag = 0;

        try
        {
            ulong overrideName = NativeMethods.WNF_PO_ENERGY_SAVER_OVERRIDE;
            byte[] buf = new byte[8];
            uint size = (uint)buf.Length;
            if (NativeMethods.NtQueryWnfStateData(ref overrideName, IntPtr.Zero, IntPtr.Zero, out _, buf, ref size) == 0 && size >= 4)
            {
                wnfOverride = BitConverter.ToInt32(buf, 0);
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Could not query WNF_PO_ENERGY_SAVER_OVERRIDE");
        }

        try
        {
            ulong stateName = NativeMethods.WNF_PO_ENERGY_SAVER_STATE;
            byte[] buf = new byte[8];
            uint size = (uint)buf.Length;
            if (NativeMethods.NtQueryWnfStateData(ref stateName, IntPtr.Zero, IntPtr.Zero, out _, buf, ref size) == 0 && size >= 4)
            {
                wnfState = BitConverter.ToInt32(buf, 0);
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Could not query WNF_PO_ENERGY_SAVER_STATE");
        }

        try
        {
            if (NativeMethods.GetSystemPowerStatus(out var status))
            {
                systemStatusFlag = status.SystemStatusFlag;
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Could not query GetSystemPowerStatus");
        }

        var resolved = EnergySaverStateMapper.FromWnf(wnfOverride, wnfState, systemStatusFlag, hasBattery: true);
        Log.Debug("QueryLiveEnergySaverState: WNF_Override={Override}, WNF_State={State}, SystemStatusFlag={Flag} => {Resolved}",
            wnfOverride, wnfState, systemStatusFlag, resolved);

        return resolved;
    }

    /// <summary>
    /// Queries current energy saver status, cross-referencing with physical battery presence.
    /// Safe against exceptions and desktop hardware without battery.
    /// </summary>
    public static EnergySaverState QueryPlatformEnergySaverState()
    {
        return QueryLiveEnergySaverState();
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

            if (_wnfOverrideSubscription != IntPtr.Zero)
            {
                try
                {
                    NativeMethods.RtlUnsubscribeWnfStateChangeNotification(_wnfOverrideSubscription);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Error unsubscribing WNF_PO_ENERGY_SAVER_OVERRIDE.");
                }
                _wnfOverrideSubscription = IntPtr.Zero;
            }

            if (_wnfStateSubscription != IntPtr.Zero)
            {
                try
                {
                    NativeMethods.RtlUnsubscribeWnfStateChangeNotification(_wnfStateSubscription);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Error unsubscribing WNF_PO_ENERGY_SAVER_STATE.");
                }
                _wnfStateSubscription = IntPtr.Zero;
            }

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
