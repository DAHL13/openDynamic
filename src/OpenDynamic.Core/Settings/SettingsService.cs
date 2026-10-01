using System.Text.Json;

namespace OpenDynamic.Core.Settings;

/// <summary>
/// Service responsible for persisting and loading <see cref="AppSettings"/> with schema versioning,
/// fault tolerance against corrupt files (automatic .bak generation and default regeneration),
/// and debounced writing (500 ms) to avoid SSD wear.
/// </summary>
public sealed class SettingsService : ISettingsService
{
    private readonly object _syncLock = new();
    private readonly Action<string, Exception?>? _warningLogger;
    private readonly int _debounceMilliseconds;
    private readonly JsonSerializerOptions _jsonOptions;

    private System.Threading.Timer? _debounceTimer;
    private bool _isSavePending;
    private bool _isDisposed;

    /// <summary>
    /// Gets the current loaded application settings.
    /// </summary>
    public AppSettings CurrentSettings { get; private set; }

    /// <summary>
    /// Gets the target settings file path.
    /// </summary>
    public string SettingsFilePath { get; }

    /// <summary>
    /// Gets the backup file path used when corruption is detected.
    /// </summary>
    public string BackupFilePath { get; }

    /// <summary>
    /// Event fired whenever settings are reloaded, migrated, or updated.
    /// </summary>
    public event EventHandler<AppSettings>? SettingsChanged;

    /// <summary>
    /// Initializes a new instance of <see cref="SettingsService"/>.
    /// </summary>
    /// <param name="customFilePath">Optional custom file path for testing or override.</param>
    /// <param name="warningLogger">Optional logging callback for warnings and corruption notices.</param>
    /// <param name="debounceMilliseconds">Debounce interval in milliseconds. Defaults to 500 ms.</param>
    public SettingsService(
        string? customFilePath = null,
        Action<string, Exception?>? warningLogger = null,
        int debounceMilliseconds = 500)
    {
        _warningLogger = warningLogger;
        _debounceMilliseconds = Math.Max(10, debounceMilliseconds);

        if (!string.IsNullOrWhiteSpace(customFilePath))
        {
            SettingsFilePath = customFilePath;
        }
        else
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string folder = Path.Combine(appData, "openDynamic");
            SettingsFilePath = Path.Combine(folder, "settings.json");
        }

        BackupFilePath = SettingsFilePath + ".bak";
        CurrentSettings = new AppSettings();

        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };
    }

    /// <inheritdoc />
    public void Load()
    {
        lock (_syncLock)
        {
            EnsureDirectoryExists();

            if (!File.Exists(SettingsFilePath))
            {
                // First run: save defaults immediately
                CurrentSettings = new AppSettings();
                WriteSettingsToDisk(CurrentSettings);
                SettingsChanged?.Invoke(this, CurrentSettings);
                return;
            }

            try
            {
                string json = File.ReadAllText(SettingsFilePath);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json, _jsonOptions);

                if (loaded == null)
                {
                    throw new JsonException("Deserialized AppSettings instance was null.");
                }

                // Check schema version migration
                if (loaded.SchemaVersion < AppSettings.CurrentSchemaVersion)
                {
                    _warningLogger?.Invoke(
                        $"Migrating settings schema from v{loaded.SchemaVersion} to v{AppSettings.CurrentSchemaVersion}.",
                        null);

                    if (loaded.SchemaVersion < 2)
                    {
                        // Migration v1 -> v2: Transition from floating pill (8 DIP offset, 18 corner radius)
                        // to top rectangular notch flush against the bezel (0 DIP offset, 14 corner radius).
                        loaded.OffsetY = 0.0;
                        loaded.CapsuleCornerRadius = 14.0;
                        if (Math.Abs(loaded.CapsuleWidth - 160.0) < 0.001)
                        {
                            loaded.CapsuleWidth = 200.0;
                        }
                    }

                    if (loaded.SchemaVersion < 3)
                    {
                        // Migration v2 -> v3: Introduce MotionMode setting with Auto as default.
                        if (!Enum.IsDefined(typeof(Animation.MotionMode), loaded.MotionMode))
                        {
                            loaded.MotionMode = Animation.MotionMode.Auto;
                        }
                    }

                    if (loaded.SchemaVersion < 4)
                    {
                        // Migration v3 -> v4: Introduce Network & Device alert settings and ignore list.
                        loaded.EnableNetworkAlerts = true;
                        loaded.DefaultNetworkPriority = 65;
                        loaded.NetworkTransientDurationSeconds = 3.0;
                        loaded.EnableDeviceAlerts = true;
                        loaded.DefaultDevicePriority = 60;
                        loaded.DeviceTransientDurationSeconds = 3.0;
                        loaded.IgnoredDeviceNames ??= new List<string>();
                    }

                    if (loaded.SchemaVersion < 5)
                    {
                        // Migration v4 -> v5: Introduce Stopwatch widget settings, priority (45), and configurable timer presets.
                        loaded.EnableStopwatchWidget = true;
                        loaded.DefaultStopwatchPriority = 45;
                        if (loaded.TimerPresetsMinutes == null || loaded.TimerPresetsMinutes.Count == 0)
                        {
                            loaded.TimerPresetsMinutes = new List<int> { 1, 5, 10, 15 };
                        }
                    }

                    if (loaded.SchemaVersion < 6)
                    {
                        // Migration v5 -> v6: Introduce Dynamic Album Art Color and Horizontal Media Gestures.
                        loaded.EnableDynamicMediaColor = true;
                        loaded.EnableMediaGestures = true;
                        loaded.MediaGestureSensitivity = 120.0;
                    }

                    if (loaded.SchemaVersion < 7)
                    {
                        // Migration v6 -> v7: Introduce In-Memory Clipboard History (Strictly Opt-In, false by default).
                        loaded.EnableClipboardWidget = false;
                        loaded.DefaultClipboardPriority = 55;
                        loaded.ClipboardTransientDurationSeconds = 2.0;
                        loaded.ShowClipboardPreview = true;
                        loaded.ClipboardHistoryCapacity = 5;
                        loaded.ClipboardExpirationMinutes = 10;
                    }

                    if (loaded.SchemaVersion < 8)
                    {
                        // Migration v7 -> v8: Introduce Microphone & Camera indicators, privacy transient alerts, priority (85), and ignored privacy apps.
                        loaded.EnableMicrophoneIndicator = true;
                        loaded.EnableCameraIndicator = true;
                        loaded.EnablePrivacyAlerts = true;
                        loaded.DefaultPrivacyPriority = 85;
                        loaded.PrivacyTransientDurationSeconds = 3.0;
                        loaded.IgnoredPrivacyApps ??= new List<string>();
                    }

                    if (loaded.SchemaVersion < 9)
                    {
                        // Migration v8 -> v9: Introduce Audio Visualizer mode (Disabled, Simulated, Real).
                        // Defaults to Real.
                        if (!Enum.IsDefined(typeof(Audio.Spectrum.AudioVisualizerMode), loaded.VisualizerMode))
                        {
                            loaded.VisualizerMode = Audio.Spectrum.AudioVisualizerMode.Real;
                        }
                    }

                    if (loaded.SchemaVersion < 10)
                    {
                        // Migration v9 -> v10: Introduce Antigravity Approvals in notch (Strictly disabled by default).
                        loaded.EnableAgentApprovals = false;
                        loaded.AgentApprovalTimeoutSeconds = 90;
                        loaded.AgentApprovalGracePeriodMs = 600;
                        loaded.EnableAgentApprovalSound = true;
                        loaded.AgentApprovalPredefinedDenyReasons = new List<string>
                        {
                            "No: usa otro enfoque",
                            "No: pregúntame antes de ejecutar esto",
                            "No: no toques esos archivos"
                        };
                        loaded.AgentApprovalHighlightedOption = 1;
                        loaded.CustomAgentHookPath = null;
                    }

                    loaded.IgnoredPrivacyApps ??= new List<string>();
                    loaded.IgnoredDeviceNames ??= new List<string>();
                    loaded.AgentApprovalPredefinedDenyReasons ??= new List<string>
                    {
                        "No: usa otro enfoque",
                        "No: pregúntame antes de ejecutar esto",
                        "No: no toques esos archivos"
                    };
                    loaded.SchemaVersion = AppSettings.CurrentSchemaVersion;
                    CurrentSettings = loaded;
                    WriteSettingsToDisk(CurrentSettings);
                }
                else
                {
                    loaded.IgnoredPrivacyApps ??= new List<string>();
                    loaded.IgnoredDeviceNames ??= new List<string>();
                    loaded.AgentApprovalPredefinedDenyReasons ??= new List<string>
                    {
                        "No: usa otro enfoque",
                        "No: pregúntame antes de ejecutar esto",
                        "No: no toques esos archivos"
                    };
                    CurrentSettings = loaded;
                }

                SettingsChanged?.Invoke(this, CurrentSettings);
            }
            catch (Exception ex) when (ex is JsonException or FormatException or IOException)
            {
                _warningLogger?.Invoke(
                    $"Settings file at '{SettingsFilePath}' was corrupt or unreadable. Backing up to '{BackupFilePath}' and regenerating defaults.",
                    ex);

                BackupCorruptFile();

                // Recreate default configuration
                CurrentSettings = new AppSettings();
                WriteSettingsToDisk(CurrentSettings);
                SettingsChanged?.Invoke(this, CurrentSettings);
            }
        }
    }

    /// <inheritdoc />
    public Task LoadAsync()
    {
        return Task.Run(Load);
    }

    /// <inheritdoc />
    public void SaveDebounced()
    {
        lock (_syncLock)
        {
            if (_isDisposed) return;

            _isSavePending = true;

            if (_debounceTimer == null)
            {
                _debounceTimer = new System.Threading.Timer(OnDebounceTimerElapsed, null, _debounceMilliseconds, Timeout.Infinite);
            }
            else
            {
                _debounceTimer.Change(_debounceMilliseconds, Timeout.Infinite);
            }
        }
    }

    /// <inheritdoc />
    public void SaveImmediate()
    {
        lock (_syncLock)
        {
            if (_isDisposed) return;

            _debounceTimer?.Change(Timeout.Infinite, Timeout.Infinite);
            _isSavePending = false;

            WriteSettingsToDisk(CurrentSettings);
            SettingsChanged?.Invoke(this, CurrentSettings);
        }
    }

    /// <inheritdoc />
    public Task SaveImmediateAsync()
    {
        return Task.Run(SaveImmediate);
    }

    private void OnDebounceTimerElapsed(object? state)
    {
        lock (_syncLock)
        {
            if (_isDisposed || !_isSavePending) return;

            _isSavePending = false;
            WriteSettingsToDisk(CurrentSettings);
            SettingsChanged?.Invoke(this, CurrentSettings);
        }
    }

    private void WriteSettingsToDisk(AppSettings settings)
    {
        try
        {
            EnsureDirectoryExists();
            string json = JsonSerializer.Serialize(settings, _jsonOptions);

            string tempFile = SettingsFilePath + ".tmp";
            File.WriteAllText(tempFile, json);
            File.Move(tempFile, SettingsFilePath, overwrite: true);
        }
        catch (Exception ex)
        {
            _warningLogger?.Invoke($"Failed to write settings to '{SettingsFilePath}'.", ex);
        }
    }

    private void BackupCorruptFile()
    {
        try
        {
            if (File.Exists(SettingsFilePath))
            {
                File.Copy(SettingsFilePath, BackupFilePath, overwrite: true);
            }
        }
        catch (Exception ex)
        {
            _warningLogger?.Invoke($"Failed to create backup copy at '{BackupFilePath}'.", ex);
        }
    }

    private void EnsureDirectoryExists()
    {
        string? dir = Path.GetDirectoryName(SettingsFilePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_syncLock)
        {
            if (_isDisposed) return;
            _isDisposed = true;

            if (_isSavePending)
            {
                _isSavePending = false;
                WriteSettingsToDisk(CurrentSettings);
            }

            _debounceTimer?.Dispose();
            _debounceTimer = null;
        }
    }
}
