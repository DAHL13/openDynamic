using System.Runtime.InteropServices;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;
using OpenDynamic.App.Native;
using OpenDynamic.Core.Devices;
using OpenDynamic.Core.Settings;
using Serilog;

namespace OpenDynamic.App.Services;

/// <summary>
/// Monitors peripheral USB and Bluetooth device connections and disconnections.
/// Listens to Win32 WM_DEVICECHANGE via RegisterDeviceNotification and WinRT DeviceWatcher.
/// Adheres strictly to Golden Rule 1 (0% CPU at rest), Golden Rule 11 (complete watcher disposal on disable),
/// and the v1.1 Privacy Rule (device friendly names never emitted to logs, only category and event count).
/// </summary>
public sealed class DeviceService : IDisposable
{
    private readonly object _syncLock = new();
    private readonly AppSettings _settings;
    private readonly DeviceAlertPolicy _policy;

    private IntPtr _hwnd = IntPtr.Zero;
    private IntPtr _usbNotificationHandle = IntPtr.Zero;
    private DeviceWatcher? _bluetoothWatcher;
    private bool _isListening;
    private bool _isDisposed;

    // Cache of known Bluetooth device friendly names by ID to report readable names on removal
    private readonly Dictionary<string, (string Name, DeviceCategory Category)> _bluetoothDeviceCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Occurs when a consolidated device connection or disconnection alert warrants dynamic island presentation.
    /// </summary>
    public event EventHandler<DeviceEvent>? DeviceAlertTriggered;

    public DeviceAlertPolicy Policy => _policy;
    public bool IsListening => _isListening;

    public DeviceService(AppSettings settings, DeviceAlertPolicy? policy = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _policy = policy ?? new DeviceAlertPolicy(
            TimeProvider.System,
            coalesceDuration: TimeSpan.FromMilliseconds(800),
            cooldownDuration: TimeSpan.FromSeconds(3.0),
            suspendSuppressionDuration: TimeSpan.FromSeconds(10.0),
            initialIgnoredDevices: _settings.IgnoredDeviceNames);

        _policy.AlertTriggered += OnPolicyAlertTriggered;
    }

    /// <summary>
    /// Starts USB and Bluetooth watchers if device alerts are enabled.
    /// </summary>
    /// <param name="hwnd">Window handle for receiving Win32 WM_DEVICECHANGE notifications.</param>
    public void Start(IntPtr hwnd)
    {
        lock (_syncLock)
        {
            if (_isDisposed) return;
            _hwnd = hwnd;

            if (!_settings.EnableDeviceAlerts)
            {
                Log.Debug("DeviceService.Start skipped because EnableDeviceAlerts is disabled.");
                return;
            }

            if (_isListening) return;

            // Sync configured ignored devices
            _policy.SetIgnoredDevices(_settings.IgnoredDeviceNames);

            RegisterUsbNotifications(hwnd);
            StartBluetoothWatcher();

            _isListening = true;
            Log.Information("DeviceService started. USB and Bluetooth device watchers active.");
        }
    }

    /// <summary>
    /// Stops all device watchers and releases notification handles immediately (Golden Rule 11).
    /// </summary>
    public void Stop()
    {
        lock (_syncLock)
        {
            if (!_isListening && _bluetoothWatcher == null && _usbNotificationHandle == IntPtr.Zero) return;

            UnregisterUsbNotifications();
            StopBluetoothWatcher();

            _policy.NotifySuspended();
            _isListening = false;

            Log.Information("DeviceService stopped. Watchers and notification hooks released.");
        }
    }

    /// <summary>
    /// Notifies the policy of system suspension (cancels transient burst timers).
    /// </summary>
    public void NotifySuspended()
    {
        _policy.NotifySuspended();
    }

    /// <summary>
    /// Notifies the policy of system resume from sleep (suppresses flurry notices for 10s).
    /// </summary>
    public void NotifyResumed()
    {
        _policy.NotifyResumedFromSuspend();
    }

    /// <summary>
    /// Synchronizes the ignored devices list from current application settings.
    /// </summary>
    public void UpdateIgnoredDevices()
    {
        _policy.SetIgnoredDevices(_settings.IgnoredDeviceNames);
    }

    #region Win32 USB Notifications (WM_DEVICECHANGE)

    private void RegisterUsbNotifications(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;

        try
        {
            var dbi = new NativeMethods.DEV_BROADCAST_DEVICEINTERFACE
            {
                dbcc_size = Marshal.SizeOf<NativeMethods.DEV_BROADCAST_DEVICEINTERFACE>(),
                dbcc_devicetype = NativeMethods.DBT_DEVTYP_DEVICEINTERFACE,
                dbcc_reserved = 0,
                dbcc_classguid = Guid.Empty
            };

            _usbNotificationHandle = NativeMethods.RegisterDeviceNotification(
                hwnd,
                ref dbi,
                NativeMethods.DEVICE_NOTIFY_WINDOW_HANDLE | NativeMethods.DEVICE_NOTIFY_ALL_INTERFACE_CLASSES);

            Log.Debug("Registered USB RegisterDeviceNotification for HWND: {Hwnd}", hwnd);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to register USB device notifications.");
        }
    }

    private void UnregisterUsbNotifications()
    {
        if (_usbNotificationHandle != IntPtr.Zero)
        {
            try
            {
                NativeMethods.UnregisterDeviceNotification(_usbNotificationHandle);
                _usbNotificationHandle = IntPtr.Zero;
                Log.Debug("Unregistered USB RegisterDeviceNotification.");
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Error unregistering USB device notification handle.");
            }
        }
    }

    /// <summary>
    /// Handles Win32 WM_DEVICECHANGE messages forwarded from the main window procedure.
    /// </summary>
    public void HandleDeviceChange(IntPtr wParam, IntPtr lParam)
    {
        if (!_isListening || lParam == IntPtr.Zero) return;

        int eventCode = wParam.ToInt32();
        if (eventCode != NativeMethods.DBT_DEVICEARRIVAL && eventCode != NativeMethods.DBT_DEVICEREMOVECOMPLETE)
        {
            return;
        }

        try
        {
            var hdr = Marshal.PtrToStructure<NativeMethods.DEV_BROADCAST_HDR>(lParam);
            if (hdr.dbch_devicetype != NativeMethods.DBT_DEVTYP_DEVICEINTERFACE)
            {
                return;
            }

            var dbi = Marshal.PtrToStructure<NativeMethods.DEV_BROADCAST_DEVICEINTERFACE>(lParam);
            string? devicePath = dbi.dbcc_name;
            if (string.IsNullOrWhiteSpace(devicePath))
            {
                return;
            }

            var eventType = eventCode == NativeMethods.DBT_DEVICEARRIVAL
                ? DeviceEventType.Connected
                : DeviceEventType.Disconnected;

            // Resolve friendly name asynchronously via WinRT DeviceInformation
            _ = Task.Run(async () =>
            {
                await ProcessUsbDeviceEventAsync(devicePath, eventType);
            });
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Error parsing WM_DEVICECHANGE structure.");
        }
    }

    private async Task ProcessUsbDeviceEventAsync(string devicePath, DeviceEventType eventType)
    {
        string friendlyName = "Dispositivo USB";
        DeviceCategory category = DeviceCategory.Other;

        try
        {
            var devInfo = await DeviceInformation.CreateFromIdAsync(devicePath);
            if (devInfo != null && !string.IsNullOrWhiteSpace(devInfo.Name))
            {
                friendlyName = devInfo.Name;
            }
            else
            {
                friendlyName = ExtractFriendlyNameFromDevicePath(devicePath);
            }
        }
        catch
        {
            friendlyName = ExtractFriendlyNameFromDevicePath(devicePath);
        }

        category = DeviceCategoryClassifier.Classify(friendlyName);

        // Privacy rule: log only Category and Type, NEVER device friendly names
        Log.Debug("USB device event intercepted: Type={Type}, Category={Category}",
            eventType, category);

        var devEvent = new DeviceEvent(eventType, devicePath, friendlyName, category);
        _policy.ProcessDeviceEvent(devEvent);
    }

    private static string ExtractFriendlyNameFromDevicePath(string devicePath)
    {
        // Example devicePath: \\?\USB#VID_0781&PID_5581#0101...#{a5dcbf10-6530-11d2-901f-00c04fb951ed}
        try
        {
            var parts = devicePath.Split('#');
            if (parts.Length >= 2)
            {
                string hardwareId = parts[1];
                if (devicePath.Contains("STORAGE", StringComparison.OrdinalIgnoreCase) ||
                    hardwareId.Contains("STOR", StringComparison.OrdinalIgnoreCase))
                {
                    return "Almacenamiento USB";
                }
                if (devicePath.Contains("HID", StringComparison.OrdinalIgnoreCase))
                {
                    return "Periférico HID";
                }
                return "Dispositivo USB";
            }
        }
        catch
        {
            // Fallback
        }

        return "Dispositivo USB";
    }

    #endregion

    #region WinRT Bluetooth DeviceWatcher

    private void StartBluetoothWatcher()
    {
        try
        {
            // AQS filter for Bluetooth Association Endpoints
            string aqs = BluetoothDevice.GetDeviceSelector();
            _bluetoothWatcher = DeviceInformation.CreateWatcher(
                aqs,
                new[] { "System.Devices.Aep.IsConnected", "System.Devices.BatteryLevel" },
                DeviceInformationKind.AssociationEndpoint);

            _bluetoothWatcher.Added += OnBluetoothDeviceAdded;
            _bluetoothWatcher.Updated += OnBluetoothDeviceUpdated;
            _bluetoothWatcher.Removed += OnBluetoothDeviceRemoved;
            _bluetoothWatcher.EnumerationCompleted += OnBluetoothEnumerationCompleted;
            _bluetoothWatcher.Stopped += OnBluetoothWatcherStopped;

            _bluetoothWatcher.Start();
            Log.Debug("WinRT Bluetooth DeviceWatcher started.");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to start WinRT Bluetooth DeviceWatcher.");
        }
    }

    private void StopBluetoothWatcher()
    {
        if (_bluetoothWatcher != null)
        {
            try
            {
                _bluetoothWatcher.Added -= OnBluetoothDeviceAdded;
                _bluetoothWatcher.Updated -= OnBluetoothDeviceUpdated;
                _bluetoothWatcher.Removed -= OnBluetoothDeviceRemoved;
                _bluetoothWatcher.EnumerationCompleted -= OnBluetoothEnumerationCompleted;
                _bluetoothWatcher.Stopped -= OnBluetoothWatcherStopped;

                if (_bluetoothWatcher.Status == DeviceWatcherStatus.Started ||
                    _bluetoothWatcher.Status == DeviceWatcherStatus.EnumerationCompleted)
                {
                    _bluetoothWatcher.Stop();
                }

                _bluetoothWatcher = null;
                Log.Debug("WinRT Bluetooth DeviceWatcher stopped and disposed.");
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Error stopping Bluetooth DeviceWatcher.");
            }
        }
    }

    private void OnBluetoothEnumerationCompleted(DeviceWatcher sender, object args)
    {
        _policy.NotifyEnumerationCompleted();
        Log.Information("Bluetooth device enumeration completed. Initial connected devices cached; live alerts active.");
    }

    private void OnBluetoothDeviceAdded(DeviceWatcher sender, DeviceInformation deviceInfo)
    {
        try
        {
            bool isConnected = false;
            if (deviceInfo.Properties.TryGetValue("System.Devices.Aep.IsConnected", out var isConnectedVal) &&
                isConnectedVal is bool b)
            {
                isConnected = b;
            }

            string name = !string.IsNullOrWhiteSpace(deviceInfo.Name) ? deviceInfo.Name : "Dispositivo Bluetooth";
            var category = DeviceCategoryClassifier.Classify(name);

            lock (_syncLock)
            {
                _bluetoothDeviceCache[deviceInfo.Id] = (name, category);
            }

            int? batteryPercent = TryExtractBattery(deviceInfo.Properties);

            // Feed to policy: if pre-enumeration, it will quietly seed inventory without alerting
            var eventType = isConnected ? DeviceEventType.Connected : DeviceEventType.Disconnected;
            var devEvent = new DeviceEvent(eventType, deviceInfo.Id, name, category, batteryPercent);
            _policy.ProcessDeviceEvent(devEvent);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Error processing Bluetooth DeviceAdded event.");
        }
    }

    private void OnBluetoothDeviceUpdated(DeviceWatcher sender, DeviceInformationUpdate update)
    {
        try
        {
            if (update.Properties.TryGetValue("System.Devices.Aep.IsConnected", out var isConnectedVal) &&
                isConnectedVal is bool isConnected)
            {
                string name = "Dispositivo Bluetooth";
                DeviceCategory category = DeviceCategory.Other;

                lock (_syncLock)
                {
                    if (_bluetoothDeviceCache.TryGetValue(update.Id, out var cached))
                    {
                        name = cached.Name;
                        category = cached.Category;
                    }
                }

                int? batteryPercent = TryExtractBattery(update.Properties);

                var eventType = isConnected ? DeviceEventType.Connected : DeviceEventType.Disconnected;
                var devEvent = new DeviceEvent(eventType, update.Id, name, category, batteryPercent);
                _policy.ProcessDeviceEvent(devEvent);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Error processing Bluetooth DeviceUpdated event.");
        }
    }

    private void OnBluetoothDeviceRemoved(DeviceWatcher sender, DeviceInformationUpdate update)
    {
        try
        {
            string name = "Dispositivo Bluetooth";
            DeviceCategory category = DeviceCategory.Other;

            lock (_syncLock)
            {
                if (_bluetoothDeviceCache.TryGetValue(update.Id, out var cached))
                {
                    name = cached.Name;
                    category = cached.Category;
                    _bluetoothDeviceCache.Remove(update.Id);
                }
            }

            var devEvent = new DeviceEvent(DeviceEventType.Disconnected, update.Id, name, category);
            _policy.ProcessDeviceEvent(devEvent);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Error processing Bluetooth DeviceRemoved event.");
        }
    }

    private void OnBluetoothWatcherStopped(DeviceWatcher sender, object args)
    {
        Log.Debug("Bluetooth DeviceWatcher transitioned to Stopped state.");
    }

    private static int? TryExtractBattery(IReadOnlyDictionary<string, object> properties)
    {
        try
        {
            if (properties.TryGetValue("System.Devices.BatteryLevel", out var batVal) && batVal != null)
            {
                if (batVal is byte b && b <= 100) return b;
                if (batVal is int i && i is >= 0 and <= 100) return i;
            }
        }
        catch
        {
            // Omit battery if not reliably readable
        }
        return null;
    }

    #endregion

    private void OnPolicyAlertTriggered(object? sender, DeviceEvent devEvent)
    {
        // Privacy rule: log only Category and Type, NEVER device friendly names
        Log.Information("DeviceService emitting transient alert: Type={Type}, Category={Category}, HasBattery={HasBattery}",
            devEvent.Type, devEvent.Category, devEvent.BatteryPercent.HasValue);

        DeviceAlertTriggered?.Invoke(this, devEvent);
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
            DeviceAlertTriggered = null;
        }
    }
}
