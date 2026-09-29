using System.Runtime.InteropServices;
using OpenDynamic.App.Native;
using OpenDynamic.Core.Power;
using OpenDynamic.Core.Settings;
using Serilog;

namespace OpenDynamic.App.Services;

/// <summary>
/// Monitors system power state, AC connection, and battery levels using Windows Power Broadcasts.
/// Strictly adheres to Golden Rule 1 (0% CPU at rest, ZERO polling timers).
/// Listens to WM_POWERBROADCAST and RegisterPowerSettingNotification.
/// </summary>
public sealed class PowerService : IBatteryMonitor, IDisposable
{
    private readonly object _syncLock = new();
    private readonly AppSettings _settings;
    private readonly BatteryThresholdTracker _tracker;

    private IntPtr _acdcNotificationHandle = IntPtr.Zero;
    private IntPtr _batteryNotificationHandle = IntPtr.Zero;
    private BatterySnapshot _currentStatus = BatterySnapshot.DesktopAcOnline;
    private bool _isDisposed;

    public BatterySnapshot CurrentStatus
    {
        get
        {
            lock (_syncLock)
            {
                return _currentStatus;
            }
        }
    }

    public event EventHandler<BatteryAlertEventArgs>? AlertTriggered;
    public event EventHandler<BatterySnapshot>? StatusChanged;

    public PowerService(AppSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _tracker = new BatteryThresholdTracker(
            lowThresholdPercent: _settings.BatteryLowThresholdPercent,
            criticalThresholdPercent: _settings.BatteryCriticalThresholdPercent);

        // Fetch initial status on startup without triggering spurious alerts
        RefreshPowerStatus(isInitial: true);
    }

    /// <summary>
    /// Registers for window-based power setting notifications via Win32 API.
    /// Call from IslandWindow after HWND initialization.
    /// </summary>
    public void RegisterWindowNotifications(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;

        lock (_syncLock)
        {
            try
            {
                Guid acdcGuid = NativeMethods.GUID_ACDC_POWER_SOURCE;
                _acdcNotificationHandle = NativeMethods.RegisterPowerSettingNotification(
                    hwnd,
                    ref acdcGuid,
                    NativeMethods.DEVICE_NOTIFY_WINDOW_HANDLE);

                Guid batteryGuid = NativeMethods.GUID_BATTERY_PERCENTAGE_REMAINING;
                _batteryNotificationHandle = NativeMethods.RegisterPowerSettingNotification(
                    hwnd,
                    ref batteryGuid,
                    NativeMethods.DEVICE_NOTIFY_WINDOW_HANDLE);

                Log.Debug("PowerService registered power setting notifications for HWND: {Hwnd}", hwnd);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to register power setting notifications.");
            }
        }
    }

    /// <summary>
    /// Processes WM_POWERBROADCAST messages received in the window procedure.
    /// Zero polling: evaluation runs solely when Windows notifies a power event.
    /// </summary>
    public void HandlePowerBroadcast(IntPtr wParam, IntPtr lParam)
    {
        int eventCode = wParam.ToInt32();
        Log.Debug("WM_POWERBROADCAST received (wParam: 0x{WParam:X4})", eventCode);

        RefreshPowerStatus(isInitial: false);
    }

    /// <summary>
    /// Queries the native system power status via GetSystemPowerStatus and evaluates alerts.
    /// </summary>
    public void RefreshPowerStatus(bool isInitial = false)
    {
        BatterySnapshot newStatus;
        BatteryAlertKind alertToEmit = BatteryAlertKind.None;

        lock (_syncLock)
        {
            try
            {
                if (NativeMethods.GetSystemPowerStatus(out var rawStatus))
                {
                    // BatteryFlag 128 indicates no system battery (desktop PC)
                    bool hasBattery = (rawStatus.BatteryFlag & 128) == 0 && rawStatus.BatteryLifePercent != 255;
                    bool isCharging = rawStatus.ACLineStatus == 1;
                    int percent = rawStatus.BatteryLifePercent <= 100 ? rawStatus.BatteryLifePercent : 100;

                    newStatus = new BatterySnapshot(
                        Percent: percent,
                        IsCharging: isCharging,
                        HasBattery: hasBattery);

                    _currentStatus = newStatus;
                    alertToEmit = _tracker.Evaluate(newStatus);

                    Log.Debug("GetSystemPowerStatus: Battery={HasBattery}, Percent={Percent}%, Charging={Charging}, Alert={Alert}",
                        hasBattery, percent, isCharging, alertToEmit);
                }
                else
                {
                    int error = Marshal.GetLastWin32Error();
                    Log.Warning("GetSystemPowerStatus failed with Win32 error code: {ErrorCode}", error);
                    return;
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Unexpected error refreshing power status.");
                return;
            }
        }

        StatusChanged?.Invoke(this, newStatus);

        if (!isInitial && alertToEmit != BatteryAlertKind.None)
        {
            Log.Information("PowerService alert emitted: {AlertKind} (Percent: {Percent}%, Charging: {Charging})",
                alertToEmit, newStatus.Percent, newStatus.IsCharging);
            AlertTriggered?.Invoke(this, new BatteryAlertEventArgs(alertToEmit, newStatus));
        }
    }

    public void Dispose()
    {
        lock (_syncLock)
        {
            if (_isDisposed) return;
            _isDisposed = true;

            try
            {
                if (_acdcNotificationHandle != IntPtr.Zero)
                {
                    NativeMethods.UnregisterPowerSettingNotification(_acdcNotificationHandle);
                    _acdcNotificationHandle = IntPtr.Zero;
                }

                if (_batteryNotificationHandle != IntPtr.Zero)
                {
                    NativeMethods.UnregisterPowerSettingNotification(_batteryNotificationHandle);
                    _batteryNotificationHandle = IntPtr.Zero;
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Error unregistering power setting notifications.");
            }

            AlertTriggered = null;
            StatusChanged = null;
        }
    }
}
