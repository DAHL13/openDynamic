using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;
using OpenDynamic.App.Native;
using OpenDynamic.Core.Privacy;
using OpenDynamic.Core.Settings;
using Serilog;

namespace OpenDynamic.App.Services;

/// <summary>
/// Passive monitor observing Windows ConsentStore registry keys for microphone and webcam access.
/// Uses native kernel events via RegNotifyChangeKeyValue (Regla de Oro 1: zero polling).
/// Strictly passive: NEVER opens, initializes, or captures physical audio/video devices (Regla de Oro 10).
/// </summary>
public sealed class PrivacyAccessMonitor : IPrivacyAccessMonitor
{
    private const string MicrophoneSubKey = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\microphone";
    private const string WebcamSubKey = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\webcam";

    private readonly IPrivacyAccessAggregator _aggregator;
    private readonly AppSettings _settings;
    private readonly object _lifecycleLock = new();
    private readonly ConcurrentDictionary<string, string> _friendlyNameCache = new(StringComparer.OrdinalIgnoreCase);

    private Thread? _workerThread;
    private ManualResetEvent? _stopEvent;
    private AutoResetEvent? _micEvent;
    private AutoResetEvent? _camEvent;
    private RegistryKey? _micKey;
    private RegistryKey? _camKey;

    private bool _isRunning;
    private bool _isDisposed;

    public PrivacyAccessState CurrentState => _aggregator.CurrentState;

    public IPrivacyAccessAggregator Aggregator => _aggregator;

    public event EventHandler<PrivacyAccessState>? StateChanged
    {
        add => _aggregator.StateChanged += value;
        remove => _aggregator.StateChanged -= value;
    }

    public event EventHandler<PrivacyAccessChange>? AccessAlertTriggered
    {
        add => _aggregator.AccessAlertTriggered += value;
        remove => _aggregator.AccessAlertTriggered -= value;
    }

    public PrivacyAccessMonitor(
        AppSettings settings,
        IPrivacyAccessAggregator? aggregator = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _aggregator = aggregator ?? new PrivacyAccessAggregator(settings.IgnoredPrivacyApps);
    }

    /// <inheritdoc />
    public void Start()
    {
        lock (_lifecycleLock)
        {
            if (_isRunning || _isDisposed)
            {
                return;
            }

            try
            {
                _micKey = Registry.CurrentUser.OpenSubKey(MicrophoneSubKey, RegistryKeyPermissionCheck.ReadSubTree);
                _camKey = Registry.CurrentUser.OpenSubKey(WebcamSubKey, RegistryKeyPermissionCheck.ReadSubTree);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[Privacy] Failed to open Windows ConsentStore registry keys. Monitoring unavailable.");
            }

            if (_micKey == null && _camKey == null)
            {
                Log.Warning("[Privacy] Neither ConsentStore\\microphone nor ConsentStore\\webcam registry keys exist on this system. Passive privacy monitoring is disabled.");
                return;
            }

            _stopEvent = new ManualResetEvent(false);
            _micEvent = _micKey != null ? new AutoResetEvent(false) : null;
            _camEvent = _camKey != null ? new AutoResetEvent(false) : null;

            _workerThread = new Thread(WorkerLoop)
            {
                Name = "PrivacyConsentStoreWatcher",
                IsBackground = true
            };

            _isRunning = true;
            _workerThread.Start();

            Log.Information("[Privacy] Passive ConsentStore monitor started successfully (Zero polling, RegNotifyChangeKeyValue).");
        }
    }

    /// <inheritdoc />
    public void Stop()
    {
        lock (_lifecycleLock)
        {
            if (!_isRunning)
            {
                return;
            }

            _isRunning = false;

            try
            {
                _stopEvent?.Set();
                if (_workerThread != null && _workerThread.IsAlive)
                {
                    _workerThread.Join(TimeSpan.FromSeconds(2));
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[Privacy] Error during monitor worker thread shutdown.");
            }
            finally
            {
                _micKey?.Dispose();
                _micKey = null;

                _camKey?.Dispose();
                _camKey = null;

                _stopEvent?.Dispose();
                _stopEvent = null;

                _micEvent?.Dispose();
                _micEvent = null;

                _camEvent?.Dispose();
                _camEvent = null;

                _workerThread = null;
                _aggregator.Reset();

                Log.Information("[Privacy] Passive ConsentStore monitor stopped and OS handles released.");
            }
        }
    }

    private void WorkerLoop()
    {
        // Initial registration for asynchronous kernel notifications
        RegisterKeyNotification(_micKey, _micEvent);
        RegisterKeyNotification(_camKey, _camEvent);

        // Immediate snapshot evaluation on startup (establish baseline state without firing transient toast alerts)
        ReadAndProcessConsentStore(suppressAlerts: true);

        var waitList = new List<WaitHandle>();
        if (_stopEvent != null) waitList.Add(_stopEvent);
        if (_micEvent != null) waitList.Add(_micEvent);
        if (_camEvent != null) waitList.Add(_camEvent);

        var waitArray = waitList.ToArray();

        while (!_isDisposed && _isRunning)
        {
            int signaledIndex = WaitHandle.WaitAny(waitArray);

            if (_stopEvent != null && signaledIndex == 0)
            {
                break; // Stop signal received
            }

            var signaledHandle = waitArray[signaledIndex];

            // Re-arm one-shot RegNotifyChangeKeyValue for the signaled registry key
            if (_micEvent != null && ReferenceEquals(signaledHandle, _micEvent))
            {
                RegisterKeyNotification(_micKey, _micEvent);
            }
            else if (_camEvent != null && ReferenceEquals(signaledHandle, _camEvent))
            {
                RegisterKeyNotification(_camKey, _camEvent);
            }

            // Passive registry read triggered strictly by kernel event
            ReadAndProcessConsentStore(suppressAlerts: false);
        }
    }

    private static void RegisterKeyNotification(RegistryKey? key, AutoResetEvent? evt)
    {
        if (key == null || evt == null) return;

        try
        {
            int result = NativeMethods.RegNotifyChangeKeyValue(
                key.Handle,
                bWatchSubtree: true,
                NativeMethods.RegNotifyFilter.ChangeName | NativeMethods.RegNotifyFilter.ChangeLastSet,
                evt.SafeWaitHandle,
                fAsynchronous: true);

            if (result != 0)
            {
                Log.Warning("[Privacy] RegNotifyChangeKeyValue returned Win32 error code {Code}", result);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[Privacy] Failed to arm RegNotifyChangeKeyValue notification.");
        }
    }

    private void ReadAndProcessConsentStore(bool suppressAlerts = false)
    {
        var entries = new List<PrivacyAccessEntry>();

        try
        {
            if (_micKey != null && (_settings.EnableMicrophoneIndicator || _settings.EnablePrivacyAlerts))
            {
                ReadResourceEntries(_micKey, PrivacyResourceType.Microphone, entries);
            }

            if (_camKey != null && (_settings.EnableCameraIndicator || _settings.EnablePrivacyAlerts))
            {
                ReadResourceEntries(_camKey, PrivacyResourceType.Camera, entries);
            }

            _aggregator.ProcessEntries(entries, suppressAlerts);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[Privacy] Error reading Windows ConsentStore registry entries.");
        }
    }

    private void ReadResourceEntries(RegistryKey rootKey, PrivacyResourceType resource, List<PrivacyAccessEntry> destination)
    {
        string[] subKeyNames;
        try
        {
            subKeyNames = rootKey.GetSubKeyNames();
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[Privacy] Unable to enumerate subkeys for resource {Resource}", resource);
            return;
        }

        foreach (var subKeyName in subKeyNames)
        {
            if (string.Equals(subKeyName, "NonPackaged", StringComparison.OrdinalIgnoreCase))
            {
                ReadNonPackagedEntries(rootKey, resource, destination);
            }
            else
            {
                ReadPackagedEntry(rootKey, subKeyName, resource, destination);
            }
        }
    }

    private void ReadNonPackagedEntries(RegistryKey rootKey, PrivacyResourceType resource, List<PrivacyAccessEntry> destination)
    {
        using var nonPackagedKey = rootKey.OpenSubKey("NonPackaged");
        if (nonPackagedKey == null) return;

        string[] appKeys;
        try
        {
            appKeys = nonPackagedKey.GetSubKeyNames();
        }
        catch
        {
            return;
        }

        foreach (var appKeyName in appKeys)
        {
            using var appKey = nonPackagedKey.OpenSubKey(appKeyName);
            if (appKey == null) continue;

            long start = ReadInt64Value(appKey, "LastUsedTimeStart");
            long stop = ReadInt64Value(appKey, "LastUsedTimeStop");

            string decodedPath = PrivacyConsentStoreParser.DecodeExecutablePath(appKeyName);
            string friendlyName = GetFriendlyProcessName(decodedPath);

            destination.Add(new PrivacyAccessEntry
            {
                Resource = resource,
                AppId = appKeyName,
                DisplayName = friendlyName,
                LastUsedTimeStart = start,
                LastUsedTimeStop = stop
            });
        }
    }

    private void ReadPackagedEntry(RegistryKey rootKey, string packageSubKeyName, PrivacyResourceType resource, List<PrivacyAccessEntry> destination)
    {
        using var packageKey = rootKey.OpenSubKey(packageSubKeyName);
        if (packageKey == null) return;

        long start = ReadInt64Value(packageKey, "LastUsedTimeStart");
        long stop = ReadInt64Value(packageKey, "LastUsedTimeStop");

        string friendlyName = PrivacyConsentStoreParser.FormatPackageName(packageSubKeyName);

        destination.Add(new PrivacyAccessEntry
        {
            Resource = resource,
            AppId = packageSubKeyName,
            DisplayName = friendlyName,
            LastUsedTimeStart = start,
            LastUsedTimeStop = stop
        });
    }

    private static long ReadInt64Value(RegistryKey key, string valueName)
    {
        try
        {
            object? val = key.GetValue(valueName);
            if (val is long l) return l;
            if (val is int i) return i;
            if (val != null) return Convert.ToInt64(val);
        }
        catch
        {
            // Ignore format issues
        }
        return 0L;
    }

    private string GetFriendlyProcessName(string executablePath)
    {
        return _friendlyNameCache.GetOrAdd(executablePath, path =>
        {
            try
            {
                if (File.Exists(path))
                {
                    var info = FileVersionInfo.GetVersionInfo(path);
                    if (!string.IsNullOrWhiteSpace(info.ProductName))
                    {
                        return info.ProductName.Trim();
                    }
                    if (!string.IsNullOrWhiteSpace(info.FileDescription))
                    {
                        return info.FileDescription.Trim();
                    }
                }
            }
            catch
            {
                // Fall back if file access is restricted
            }

            try
            {
                string fileName = Path.GetFileNameWithoutExtension(path);
                if (!string.IsNullOrWhiteSpace(fileName))
                {
                    return fileName;
                }
            }
            catch
            {
                // Fall back
            }

            return path;
        });
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        Stop();
    }
}
