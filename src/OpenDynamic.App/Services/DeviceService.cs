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
/// Listens to Win32 WM_DEVICECHANGE via RegisterDeviceNotification (USB + HID interfaces)
/// and WinRT DeviceWatcher with IsConnected, MajorDeviceClass, and BatteryLevel property updates.
/// Adheres strictly to Golden Rule 1 (0% CPU at rest), Golden Rule 11 (complete watcher disposal on disable),
/// and the v1.1 Privacy Rule (device friendly names never emitted to logs, only category and event count).
/// </summary>
public sealed class DeviceService : IDisposable
{
    private record CachedBluetoothDevice(string Name, DeviceCategory Category, bool? IsConnected, uint? MajorClass, int? BatteryPercent);

    private readonly object _syncLock = new();
    private readonly AppSettings _settings;
    private readonly DeviceAlertPolicy _policy;

    private IntPtr _hwnd = IntPtr.Zero;
    private IntPtr _usbNotificationHandle = IntPtr.Zero;
    private IntPtr _hidNotificationHandle = IntPtr.Zero;
    private DeviceWatcher? _bluetoothWatcher;
    private bool _isListening;
    private bool _isDisposed;

    // Cache of known Bluetooth device details by ID to track transitions and report readable names on removal
    private readonly Dictionary<string, CachedBluetoothDevice> _bluetoothDeviceCache = new(StringComparer.OrdinalIgnoreCase);

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
    /// Starts USB, HID, and Bluetooth watchers if device alerts are enabled.
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

            RegisterDeviceNotifications(hwnd);
            StartBluetoothWatcher();

            _isListening = true;
            Log.Information("DeviceService started. Win32 USB/HID and WinRT Bluetooth device watchers active.");
        }
    }

    /// <summary>
    /// Stops all device watchers and releases notification handles immediately (Golden Rule 11).
    /// </summary>
    public void Stop()
    {
        lock (_syncLock)
        {
            if (!_isListening && _bluetoothWatcher == null && _usbNotificationHandle == IntPtr.Zero && _hidNotificationHandle == IntPtr.Zero) return;

            UnregisterDeviceNotifications();
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

    #region Win32 USB & HID Notifications (WM_DEVICECHANGE)

    private void RegisterDeviceNotifications(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;

        try
        {
            // 1. Register USB device interface notifications (storage, hubs, serial, etc.)
            var usbFilter = new NativeMethods.DEV_BROADCAST_DEVICEINTERFACE
            {
                dbcc_size = Marshal.SizeOf<NativeMethods.DEV_BROADCAST_DEVICEINTERFACE>(),
                dbcc_devicetype = NativeMethods.DBT_DEVTYP_DEVICEINTERFACE,
                dbcc_reserved = 0,
                dbcc_classguid = NativeMethods.GUID_DEVINTERFACE_USB_DEVICE
            };

            _usbNotificationHandle = NativeMethods.RegisterDeviceNotification(
                hwnd,
                ref usbFilter,
                NativeMethods.DEVICE_NOTIFY_WINDOW_HANDLE);

            // 2. Register HID device interface notifications (mice, keyboards, game controllers)
            var hidFilter = new NativeMethods.DEV_BROADCAST_DEVICEINTERFACE
            {
                dbcc_size = Marshal.SizeOf<NativeMethods.DEV_BROADCAST_DEVICEINTERFACE>(),
                dbcc_devicetype = NativeMethods.DBT_DEVTYP_DEVICEINTERFACE,
                dbcc_reserved = 0,
                dbcc_classguid = NativeMethods.GUID_DEVINTERFACE_HID
            };

            _hidNotificationHandle = NativeMethods.RegisterDeviceNotification(
                hwnd,
                ref hidFilter,
                NativeMethods.DEVICE_NOTIFY_WINDOW_HANDLE);

            Log.Information("Registered Win32 device notifications: USB (Handle: {UsbHandle}), HID (Handle: {HidHandle})",
                _usbNotificationHandle, _hidNotificationHandle);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to register Win32 USB/HID device notifications.");
        }
    }

    private void UnregisterDeviceNotifications()
    {
        if (_usbNotificationHandle != IntPtr.Zero)
        {
            try
            {
                NativeMethods.UnregisterDeviceNotification(_usbNotificationHandle);
                Log.Debug("Unregistered USB RegisterDeviceNotification.");
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Error unregistering USB device notification handle.");
            }
            finally
            {
                _usbNotificationHandle = IntPtr.Zero;
            }
        }

        if (_hidNotificationHandle != IntPtr.Zero)
        {
            try
            {
                NativeMethods.UnregisterDeviceNotification(_hidNotificationHandle);
                Log.Debug("Unregistered HID RegisterDeviceNotification.");
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Error unregistering HID device notification handle.");
            }
            finally
            {
                _hidNotificationHandle = IntPtr.Zero;
            }
        }
    }

    /// <summary>
    /// Handles Win32 WM_DEVICECHANGE messages forwarded from the main window procedure.
    /// </summary>
    public void HandleDeviceChange(IntPtr wParam, IntPtr lParam)
    {
        int eventCode = wParam.ToInt32();
        Log.Information("DeviceService processing WM_DEVICECHANGE: wParam=0x{EventCode:X4}, lParam=0x{LParam:X16}, IsListening={IsListening}",
            eventCode, lParam.ToInt64(), _isListening);

        if (!_isListening || lParam == IntPtr.Zero) return;

        if (eventCode != NativeMethods.DBT_DEVICEARRIVAL && eventCode != NativeMethods.DBT_DEVICEREMOVECOMPLETE)
        {
            return;
        }

        try
        {
            var hdr = Marshal.PtrToStructure<NativeMethods.DEV_BROADCAST_HDR>(lParam);
            if (hdr.dbch_devicetype != NativeMethods.DBT_DEVTYP_DEVICEINTERFACE)
            {
                Log.Debug("WM_DEVICECHANGE ignored non-interface devicetype: 0x{Type:X4}", hdr.dbch_devicetype);
                return;
            }

            // Safely read dbcc_classguid at offset 12 on x64
            Guid classGuid = Marshal.PtrToStructure<Guid>(IntPtr.Add(lParam, 12));

            // Safely read null-terminated Unicode dbcc_name string starting at offset 28 on x64
            string? devicePath = null;
            if (hdr.dbch_size > 28)
            {
                devicePath = Marshal.PtrToStringUni(IntPtr.Add(lParam, 28));
            }

            if (string.IsNullOrWhiteSpace(devicePath))
            {
                Log.Warning("WM_DEVICECHANGE received DBT_DEVTYP_DEVICEINTERFACE with empty device path.");
                return;
            }

            var eventType = eventCode == NativeMethods.DBT_DEVICEARRIVAL
                ? DeviceEventType.Connected
                : DeviceEventType.Disconnected;

            Log.Information("WM_DEVICECHANGE parsed: EventType={EventType}, ClassGuid={Guid}",
                eventType, classGuid);

            // Resolve friendly name asynchronously via WinRT DeviceInformation
            _ = Task.Run(async () =>
            {
                await ProcessUsbDeviceEventAsync(devicePath, eventType, classGuid);
            });
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Error parsing WM_DEVICECHANGE structure.");
        }
    }

    private async Task ProcessUsbDeviceEventAsync(string devicePath, DeviceEventType eventType, Guid classGuid)
    {
        string friendlyName = classGuid == NativeMethods.GUID_DEVINTERFACE_HID
            ? "Periférico HID"
            : "Dispositivo USB";

        try
        {
            if (eventType == DeviceEventType.Connected)
            {
                // Brief pause to allow Windows driver stack to publish the device name
                await Task.Delay(100);
            }

            var devInfo = await DeviceInformation.CreateFromIdAsync(devicePath);
            if (devInfo != null && !string.IsNullOrWhiteSpace(devInfo.Name))
            {
                friendlyName = devInfo.Name;
            }
            else
            {
                friendlyName = ExtractFriendlyNameFromDevicePath(devicePath, classGuid);
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Could not resolve friendly name via WinRT for device path, falling back to path heuristics.");
            friendlyName = ExtractFriendlyNameFromDevicePath(devicePath, classGuid);
        }

        var category = DeviceCategoryClassifier.Classify(friendlyName, bluetoothMajorClass: null, devicePath: devicePath);

        // Privacy rule: log only Category, Type, and sanitized interface GUID, NEVER device friendly names
        Log.Information("USB/HID device event intercepted: Type={Type}, Category={Category}, InterfaceGuid={Guid}",
            eventType, category, classGuid);

        var devEvent = new DeviceEvent(eventType, devicePath, friendlyName, category);
        _policy.ProcessDeviceEvent(devEvent);
    }

    private static string ExtractFriendlyNameFromDevicePath(string devicePath, Guid classGuid)
    {
        try
        {
            string upper = devicePath.ToUpperInvariant();
            if (upper.Contains("MOUSE") || upper.Contains("POINT"))
            {
                return "Ratón USB";
            }
            if (upper.Contains("KBD") || upper.Contains("KEYBOARD"))
            {
                return "Teclado USB";
            }
            if (upper.Contains("STORAGE") || upper.Contains("STOR") || upper.Contains("DISK"))
            {
                return "Almacenamiento USB";
            }
            if (classGuid == NativeMethods.GUID_DEVINTERFACE_HID)
            {
                return "Periférico HID";
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
            // AQS filter for Bluetooth Association Endpoints (Protocol ID: {e0cbf06c-cdb8-4d60-bb43-dd344be4706f})
            string aqs = "System.Devices.Aep.ProtocolId:=\"{e0cbf06c-cdb8-4d60-bb43-dd344be4706f}\"";
            _bluetoothWatcher = DeviceInformation.CreateWatcher(
                aqs,
                new[]
                {
                    "System.Devices.Aep.IsConnected",
                    "System.Devices.Aep.Bluetooth.Cod.MajorDeviceClass",
                    "System.Devices.BatteryLevel"
                },
                DeviceInformationKind.AssociationEndpoint);

            _bluetoothWatcher.Added += OnBluetoothDeviceAdded;
            _bluetoothWatcher.Updated += OnBluetoothDeviceUpdated;
            _bluetoothWatcher.Removed += OnBluetoothDeviceRemoved;
            _bluetoothWatcher.EnumerationCompleted += OnBluetoothEnumerationCompleted;
            _bluetoothWatcher.Stopped += OnBluetoothWatcherStopped;

            _bluetoothWatcher.Start();
            Log.Information("WinRT Bluetooth DeviceWatcher started for Bluetooth AEP (ProtocolId: {{e0cbf06c-cdb8-4d60-bb43-dd344be4706f}}). Status: {Status}", _bluetoothWatcher.Status);

            // Safety fallback timer to complete enumeration baseline even if Bluetooth enumeration delays or is unavailable
            _ = Task.Delay(5000).ContinueWith(_ =>
            {
                if (!_policy.IsEnumerationCompleted)
                {
                    Log.Information("DeviceService fallback timer elapsed: completing initial enumeration baseline.");
                    _policy.NotifyEnumerationCompleted();
                }
            }, TaskScheduler.Default);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to start WinRT Bluetooth DeviceWatcher.");
            _policy.NotifyEnumerationCompleted();
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
                Log.Information("WinRT Bluetooth DeviceWatcher stopped and disposed.");
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
        Log.Information("Bluetooth device enumeration completed (Status: {Status}). Initial connected devices cached; live alerts active. Total cached: {Count}",
            sender.Status, _bluetoothDeviceCache.Count);
    }

    private void OnBluetoothDeviceAdded(DeviceWatcher sender, DeviceInformation deviceInfo)
    {
        try
        {
            bool isConnected = false;
            if (deviceInfo.Properties.TryGetValue("System.Devices.Aep.IsConnected", out var isConnectedVal) &&
                isConnectedVal != null)
            {
                isConnected = Convert.ToBoolean(isConnectedVal);
            }

            string name = !string.IsNullOrWhiteSpace(deviceInfo.Name) ? deviceInfo.Name : "Dispositivo Bluetooth";
            uint? majorClass = TryExtractMajorDeviceClass(deviceInfo.Properties);
            var category = DeviceCategoryClassifier.Classify(name, majorClass);
            int? batteryPercent = TryExtractBattery(deviceInfo.Properties);

            lock (_syncLock)
            {
                _bluetoothDeviceCache[deviceInfo.Id] = new CachedBluetoothDevice(name, category, isConnected, majorClass, batteryPercent);
            }

            Log.Information("Bluetooth DeviceAdded: ID={DeviceId}, Connected={IsConnected}, Category={Category}, MajorClass={MajorClass}, HasBattery={HasBattery}",
                SanitizeId(deviceInfo.Id), isConnected, category, majorClass, batteryPercent.HasValue);

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
            Log.Information("Bluetooth DeviceUpdated: ID={DeviceId}, UpdatedPropCount={Count}",
                SanitizeId(update.Id), update.Properties.Count);

            // Inspect if System.Devices.Aep.IsConnected changed
            if (!update.Properties.TryGetValue("System.Devices.Aep.IsConnected", out var isConnectedVal) ||
                isConnectedVal == null)
            {
                // IsConnected did not change in this update; might be a battery level update
                if (update.Properties.ContainsKey("System.Devices.BatteryLevel"))
                {
                    int? newBattery = TryExtractBattery(update.Properties);
                    lock (_syncLock)
                    {
                        if (_bluetoothDeviceCache.TryGetValue(update.Id, out var existing))
                        {
                            _bluetoothDeviceCache[update.Id] = existing with { BatteryPercent = newBattery };
                        }
                    }
                }
                return;
            }

            bool isConnected = Convert.ToBoolean(isConnectedVal);

            string name = "Dispositivo Bluetooth";
            DeviceCategory category = DeviceCategory.Other;
            bool? previousConnectionState = null;
            uint? majorClass = null;
            int? batteryPercent = TryExtractBattery(update.Properties);

            lock (_syncLock)
            {
                if (_bluetoothDeviceCache.TryGetValue(update.Id, out var cached))
                {
                    name = cached.Name;
                    category = cached.Category;
                    previousConnectionState = cached.IsConnected;
                    majorClass = cached.MajorClass;
                    if (!batteryPercent.HasValue) batteryPercent = cached.BatteryPercent;

                    // Update cache entry with new state
                    _bluetoothDeviceCache[update.Id] = cached with { IsConnected = isConnected, BatteryPercent = batteryPercent };
                }
                else
                {
                    // Newly observed device during update
                    majorClass = TryExtractMajorDeviceClass(update.Properties);
                    category = DeviceCategoryClassifier.Classify(name, majorClass);
                    _bluetoothDeviceCache[update.Id] = new CachedBluetoothDevice(name, category, isConnected, majorClass, batteryPercent);
                }
            }

            // Check if connection state actually changed
            if (previousConnectionState.HasValue && previousConnectionState.Value == isConnected)
            {
                Log.Debug("Bluetooth device connection state unchanged ({IsConnected}) for ID={DeviceId}; ignoring redundant update.",
                    isConnected, SanitizeId(update.Id));
                return;
            }

            var eventType = isConnected ? DeviceEventType.Connected : DeviceEventType.Disconnected;

            // Privacy rule compliant: log Category, EventType, and Battery status, NEVER friendly names
            Log.Information("Bluetooth device connection transition: Category={Category}, Event={EventType}, HasBattery={HasBattery}, ID={DeviceId}",
                category, eventType, batteryPercent.HasValue, SanitizeId(update.Id));

            var devEvent = new DeviceEvent(eventType, update.Id, name, category, batteryPercent);
            _policy.ProcessDeviceEvent(devEvent);
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

            Log.Information("Bluetooth device removed/unpaired: ID={DeviceId}, Category={Category}",
                SanitizeId(update.Id), category);

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
        Log.Information("Bluetooth DeviceWatcher transitioned to Stopped state (Status: {Status}).", sender.Status);
        if (sender.Status is DeviceWatcherStatus.Stopped or DeviceWatcherStatus.Aborted)
        {
            _policy.NotifyEnumerationCompleted();
        }
    }

    private static uint? TryExtractMajorDeviceClass(IReadOnlyDictionary<string, object> properties)
    {
        try
        {
            if (properties.TryGetValue("System.Devices.Aep.Bluetooth.Cod.MajorDeviceClass", out var val) && val != null)
            {
                return Convert.ToUInt32(val);
            }
        }
        catch
        {
            // Omit class if not convertable
        }
        return null;
    }

    private static int? TryExtractBattery(IReadOnlyDictionary<string, object> properties)
    {
        try
        {
            if (properties.TryGetValue("System.Devices.BatteryLevel", out var batVal) && batVal != null)
            {
                int level = Convert.ToInt32(batVal);
                if (level >= 0 && level <= 100)
                {
                    return level;
                }
            }
        }
        catch
        {
            // Omit battery if not reliably readable
        }
        return null;
    }

    private static string SanitizeId(string? id)
    {
        if (string.IsNullOrEmpty(id)) return "Unknown";
        return id.Length > 16 ? $"{id[..8]}...{id[^6..]}" : id;
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
