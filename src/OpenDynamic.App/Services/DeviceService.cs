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
    private IntPtr _audioNotificationHandle = IntPtr.Zero;
    private DeviceWatcher? _bluetoothWatcher;
    private DeviceWatcher? _audioWatcher;
    private bool _isListening;
    private bool _isDisposed;
    private bool _isBluetoothEnumerationCompleted;
    private bool _isAudioEnumerationCompleted;

    // Cache of known Bluetooth device details by ID to track transitions and report readable names on removal
    private readonly Dictionary<string, CachedBluetoothDevice> _bluetoothDeviceCache = new(StringComparer.OrdinalIgnoreCase);

    // Cache of known AudioRender endpoints by ID
    private readonly Dictionary<string, (string Name, bool IsEnabled)> _audioDeviceCache = new(StringComparer.OrdinalIgnoreCase);

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
    /// Starts USB, HID, Bluetooth, and AudioRender watchers if device alerts are enabled.
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

            _isBluetoothEnumerationCompleted = false;
            _isAudioEnumerationCompleted = false;

            // Sync configured ignored devices
            _policy.SetIgnoredDevices(_settings.IgnoredDeviceNames);

            RegisterDeviceNotifications(hwnd);
            StartBluetoothWatcher();
            StartAudioRenderWatcher();

            _isListening = true;
            Log.Information("DeviceService started. Win32 USB/HID/Audio and WinRT Bluetooth/Audio watchers active.");
        }
    }

    /// <summary>
    /// Stops all device watchers and releases notification handles immediately (Golden Rule 11).
    /// </summary>
    public void Stop()
    {
        lock (_syncLock)
        {
            if (!_isListening && _bluetoothWatcher == null && _audioWatcher == null &&
                _usbNotificationHandle == IntPtr.Zero && _hidNotificationHandle == IntPtr.Zero && _audioNotificationHandle == IntPtr.Zero) return;

            UnregisterDeviceNotifications();
            StopBluetoothWatcher();
            StopAudioRenderWatcher();

            _isBluetoothEnumerationCompleted = false;
            _isAudioEnumerationCompleted = false;
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

            // 3. Register Audio Render device interface notifications (Bluetooth/USB headphones and audio endpoints)
            var audioFilter = new NativeMethods.DEV_BROADCAST_DEVICEINTERFACE
            {
                dbcc_size = Marshal.SizeOf<NativeMethods.DEV_BROADCAST_DEVICEINTERFACE>(),
                dbcc_devicetype = NativeMethods.DBT_DEVTYP_DEVICEINTERFACE,
                dbcc_reserved = 0,
                dbcc_classguid = NativeMethods.GUID_DEVINTERFACE_AUDIO_RENDER
            };

            _audioNotificationHandle = NativeMethods.RegisterDeviceNotification(
                hwnd,
                ref audioFilter,
                NativeMethods.DEVICE_NOTIFY_WINDOW_HANDLE);

            Log.Information("Registered Win32 device notifications: USB (Handle: {UsbHandle}), HID (Handle: {HidHandle}), Audio (Handle: {AudioHandle})",
                _usbNotificationHandle, _hidNotificationHandle, _audioNotificationHandle);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to register Win32 USB/HID/Audio device notifications.");
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

        if (_audioNotificationHandle != IntPtr.Zero)
        {
            try
            {
                NativeMethods.UnregisterDeviceNotification(_audioNotificationHandle);
                Log.Debug("Unregistered Audio Render RegisterDeviceNotification.");
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Error unregistering Audio Render device notification handle.");
            }
            finally
            {
                _audioNotificationHandle = IntPtr.Zero;
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
            : (classGuid == NativeMethods.GUID_DEVINTERFACE_AUDIO_RENDER ? "Dispositivo de Audio" : "Dispositivo USB");

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

        var category = classGuid == NativeMethods.GUID_DEVINTERFACE_AUDIO_RENDER
            ? DeviceCategory.Audio
            : DeviceCategoryClassifier.Classify(friendlyName, bluetoothMajorClass: null, devicePath: devicePath);

        // Privacy rule: log only Category, Type, and sanitized interface GUID, NEVER device friendly names
        Log.Information("Hardware device event intercepted: Type={Type}, Category={Category}, InterfaceGuid={Guid}",
            eventType, category, classGuid);

        var devEvent = new DeviceEvent(eventType, devicePath, friendlyName, category);
        _policy.ProcessDeviceEvent(devEvent);
    }

    private static string ExtractFriendlyNameFromDevicePath(string devicePath, Guid classGuid)
    {
        try
        {
            if (classGuid == NativeMethods.GUID_DEVINTERFACE_AUDIO_RENDER)
            {
                return "Audífonos / Audio";
            }

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

    private void TryCompleteInitialEnumeration()
    {
        if (_isBluetoothEnumerationCompleted && _isAudioEnumerationCompleted && !_policy.IsEnumerationCompleted)
        {
            _policy.NotifyEnumerationCompleted();
            Log.Information("DeviceService: Initial Bluetooth + AudioRender enumeration baseline completed.");
        }
    }

    private void StartBluetoothWatcher()
    {
        try
        {
            // AQS filter for Bluetooth Association Endpoints (Protocol ID: {e0cbf06c-cdb8-4d60-bb43-dd344be4706f})
            // AUD-003: Only pass valid canonical AEP property keys supported by AssociationEndpoint watchers on Windows 10/11.
            string aqs = "System.Devices.Aep.ProtocolId:=\"{e0cbf06c-cdb8-4d60-bb43-dd344be4706f}\"";
            try
            {
                _bluetoothWatcher = DeviceInformation.CreateWatcher(
                    aqs,
                    new[]
                    {
                        "System.Devices.Aep.IsConnected",
                        "System.Devices.Aep.Category"
                    },
                    DeviceInformationKind.AssociationEndpoint);
            }
            catch (ArgumentException)
            {
                _bluetoothWatcher = DeviceInformation.CreateWatcher(
                    aqs,
                    new[] { "System.Devices.Aep.IsConnected" },
                    DeviceInformationKind.AssociationEndpoint);
            }

            _bluetoothWatcher.Added += OnBluetoothDeviceAdded;
            _bluetoothWatcher.Updated += OnBluetoothDeviceUpdated;
            _bluetoothWatcher.Removed += OnBluetoothDeviceRemoved;
            _bluetoothWatcher.EnumerationCompleted += OnBluetoothEnumerationCompleted;
            _bluetoothWatcher.Stopped += OnBluetoothWatcherStopped;

            _bluetoothWatcher.Start();
            Log.Information("WinRT Bluetooth DeviceWatcher started for Bluetooth AEP (ProtocolId: {{e0cbf06c-cdb8-4d60-bb43-dd344be4706f}}). Status: {Status}", _bluetoothWatcher.Status);

            // Safety fallback timer to complete enumeration baseline even if Bluetooth/Audio enumeration delays or is unavailable
            _ = Task.Delay(5000).ContinueWith(_ =>
            {
                if (!_policy.IsEnumerationCompleted)
                {
                    Log.Information("DeviceService fallback timer elapsed: completing initial enumeration baseline.");
                    _isBluetoothEnumerationCompleted = true;
                    _isAudioEnumerationCompleted = true;
                    _policy.NotifyEnumerationCompleted();
                }
            }, TaskScheduler.Default);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to start WinRT Bluetooth DeviceWatcher.");
            _isBluetoothEnumerationCompleted = true;
            TryCompleteInitialEnumeration();
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
        _isBluetoothEnumerationCompleted = true;
        TryCompleteInitialEnumeration();
        Log.Information("Bluetooth device enumeration completed (Status: {Status}). Total cached: {Count}",
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
            _isBluetoothEnumerationCompleted = true;
            TryCompleteInitialEnumeration();
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

    #region WinRT AudioRender DeviceWatcher

    private void StartAudioRenderWatcher()
    {
        try
        {
            _audioWatcher = DeviceInformation.CreateWatcher(DeviceClass.AudioRender);
            _audioWatcher.Added += OnAudioDeviceAdded;
            _audioWatcher.Updated += OnAudioDeviceUpdated;
            _audioWatcher.Removed += OnAudioDeviceRemoved;
            _audioWatcher.EnumerationCompleted += OnAudioEnumerationCompleted;
            _audioWatcher.Stopped += OnAudioWatcherStopped;

            _audioWatcher.Start();
            Log.Information("WinRT AudioRender DeviceWatcher started for audio rendering endpoints (Headphones/Speakers).");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to start WinRT AudioRender DeviceWatcher.");
            _isAudioEnumerationCompleted = true;
            TryCompleteInitialEnumeration();
        }
    }

    private void StopAudioRenderWatcher()
    {
        if (_audioWatcher != null)
        {
            try
            {
                _audioWatcher.Added -= OnAudioDeviceAdded;
                _audioWatcher.Updated -= OnAudioDeviceUpdated;
                _audioWatcher.Removed -= OnAudioDeviceRemoved;
                _audioWatcher.EnumerationCompleted -= OnAudioEnumerationCompleted;
                _audioWatcher.Stopped -= OnAudioWatcherStopped;

                if (_audioWatcher.Status == DeviceWatcherStatus.Started ||
                    _audioWatcher.Status == DeviceWatcherStatus.EnumerationCompleted)
                {
                    _audioWatcher.Stop();
                }

                _audioWatcher = null;
                Log.Information("WinRT AudioRender DeviceWatcher stopped and disposed.");
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Error stopping AudioRender DeviceWatcher.");
            }
        }
    }

    private void OnAudioEnumerationCompleted(DeviceWatcher sender, object args)
    {
        _isAudioEnumerationCompleted = true;
        TryCompleteInitialEnumeration();
        Log.Information("AudioRender device enumeration completed (Status: {Status}). Total cached: {Count}",
            sender.Status, _audioDeviceCache.Count);
    }

    private void OnAudioWatcherStopped(DeviceWatcher sender, object args)
    {
        Log.Information("AudioRender DeviceWatcher transitioned to Stopped state (Status: {Status}).", sender.Status);
        if (sender.Status is DeviceWatcherStatus.Stopped or DeviceWatcherStatus.Aborted)
        {
            _isAudioEnumerationCompleted = true;
            TryCompleteInitialEnumeration();
        }
    }

    private void OnAudioDeviceAdded(DeviceWatcher sender, DeviceInformation deviceInfo)
    {
        try
        {
            string name = !string.IsNullOrWhiteSpace(deviceInfo.Name) ? deviceInfo.Name : "Dispositivo de Audio";
            bool isEnabled = deviceInfo.IsEnabled;

            lock (_syncLock)
            {
                _audioDeviceCache[deviceInfo.Id] = (name, isEnabled);
            }

            Log.Information("AudioRender DeviceAdded: ID={DeviceId}, Enabled={IsEnabled}",
                SanitizeId(deviceInfo.Id), isEnabled);

            // If initial enumeration is still in progress, seed policy without alerting
            if (!_isAudioEnumerationCompleted)
            {
                if (isEnabled && IsBluetoothOrWirelessAudio(name, deviceInfo.Id))
                {
                    var devEvent = new DeviceEvent(DeviceEventType.Connected, deviceInfo.Id, name, DeviceCategory.Audio);
                    _policy.ProcessDeviceEvent(devEvent);
                }
                return;
            }

            // Live addition after enumeration
            if (isEnabled && IsBluetoothOrWirelessAudio(name, deviceInfo.Id))
            {
                Log.Information("AudioRender live device connected: Category=Audio (Headphones), ID={DeviceId}",
                    SanitizeId(deviceInfo.Id));
                var devEvent = new DeviceEvent(DeviceEventType.Connected, deviceInfo.Id, name, DeviceCategory.Audio);
                _policy.ProcessDeviceEvent(devEvent);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Error processing AudioRender DeviceAdded event.");
        }
    }

    private void OnAudioDeviceUpdated(DeviceWatcher sender, DeviceInformationUpdate update)
    {
        try
        {
            Log.Information("AudioRender DeviceUpdated: ID={DeviceId}, UpdatedPropCount={Count}",
                SanitizeId(update.Id), update.Properties.Count);

            string name = "Dispositivo de Audio";
            bool wasEnabled = false;

            lock (_syncLock)
            {
                if (_audioDeviceCache.TryGetValue(update.Id, out var cached))
                {
                    name = cached.Name;
                    wasEnabled = cached.IsEnabled;
                }
            }

            bool? isEnabled = null;
            if (update.Properties.TryGetValue("System.Devices.InterfaceEnabled", out var enabledObj) && enabledObj != null)
            {
                isEnabled = Convert.ToBoolean(enabledObj);
            }

            if (update.Properties.TryGetValue("System.Devices.AudioDevice.DeviceState", out var stateObj) && stateObj != null)
            {
                int state = Convert.ToInt32(stateObj);
                if (state == 1) // Active
                {
                    isEnabled = true;
                }
                else if (state is 2 or 4 or 8)
                {
                    isEnabled = false;
                }
            }

            if (isEnabled.HasValue)
            {
                lock (_syncLock)
                {
                    _audioDeviceCache[update.Id] = (name, isEnabled.Value);
                }

                if (wasEnabled == isEnabled.Value)
                {
                    return; // Connection state unchanged
                }

                var eventType = isEnabled.Value ? DeviceEventType.Connected : DeviceEventType.Disconnected;

                if (IsBluetoothOrWirelessAudio(name, update.Id))
                {
                    Log.Information("AudioRender device connection transition: Event={EventType}, Category=Audio (Headphones), ID={DeviceId}",
                        eventType, SanitizeId(update.Id));
                    var devEvent = new DeviceEvent(eventType, update.Id, name, DeviceCategory.Audio);
                    _policy.ProcessDeviceEvent(devEvent);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Error processing AudioRender DeviceUpdated event.");
        }
    }

    private void OnAudioDeviceRemoved(DeviceWatcher sender, DeviceInformationUpdate update)
    {
        try
        {
            string name = "Dispositivo de Audio";
            lock (_syncLock)
            {
                if (_audioDeviceCache.TryGetValue(update.Id, out var cached))
                {
                    name = cached.Name;
                    _audioDeviceCache.Remove(update.Id);
                }
            }

            Log.Information("AudioRender DeviceRemoved: ID={DeviceId}", SanitizeId(update.Id));

            if (IsBluetoothOrWirelessAudio(name, update.Id))
            {
                var devEvent = new DeviceEvent(DeviceEventType.Disconnected, update.Id, name, DeviceCategory.Audio);
                _policy.ProcessDeviceEvent(devEvent);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Error processing AudioRender DeviceRemoved event.");
        }
    }

    private static bool IsBluetoothOrWirelessAudio(string name, string id)
    {
        string upper = string.Concat(name, " ", id).ToUpperInvariant();
        return upper.Contains("BTHENUM") ||
               upper.Contains("BLUETOOTH") ||
               upper.Contains("BTHHFENUM") ||
               upper.Contains("WIRELESS") ||
               upper.Contains("HEADPHONE") ||
               upper.Contains("HEADSET") ||
               upper.Contains("EARBUDS") ||
               upper.Contains("EARBUD") ||
               upper.Contains("EARPHONE") ||
               upper.Contains("AIRPODS") ||
               upper.Contains("BUDS") ||
               upper.Contains("AURICULAR") ||
               upper.Contains("AURICULARES") ||
               upper.Contains("WH-1000") ||
               upper.Contains("WF-1000") ||
               upper.Contains("BOSE") ||
               upper.Contains("JBL") ||
               DeviceCategoryClassifier.Classify(name) == DeviceCategory.Audio;
    }

    #endregion

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
