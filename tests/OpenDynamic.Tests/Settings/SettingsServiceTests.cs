using System.Text.Json;
using OpenDynamic.Core.Settings;
using Xunit;

namespace OpenDynamic.Tests.Settings;

public sealed class SettingsServiceTests : IDisposable
{
    private readonly string _testDirectory;

    public SettingsServiceTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), "OpenDynamic_Test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDirectory);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDirectory))
            {
                Directory.Delete(_testDirectory, recursive: true);
            }
        }
        catch
        {
            // Ignore test cleanup exceptions
        }
    }

    [Fact]
    public void Load_WhenFileDoesNotExist_CreatesDefaultsAndSavesFile()
    {
        string filePath = Path.Combine(_testDirectory, "settings.json");
        using var service = new SettingsService(filePath, debounceMilliseconds: 100);

        Assert.False(File.Exists(filePath));

        service.Load();

        Assert.True(File.Exists(filePath));
        Assert.NotNull(service.CurrentSettings);
        Assert.Equal(AppSettings.CurrentSchemaVersion, service.CurrentSettings.SchemaVersion);
        Assert.Equal(200.0, service.CurrentSettings.CapsuleWidth);
        Assert.Equal(36.0, service.CurrentSettings.CapsuleHeight);
        Assert.Equal(0.0, service.CurrentSettings.OffsetY);
        Assert.Equal("Win+Ctrl+I", service.CurrentSettings.ToggleIslandHotkey);
    }

    [Fact]
    public void Load_WhenFileIsValid_LoadsAllConfiguredPropertiesCorrectly()
    {
        string filePath = Path.Combine(_testDirectory, "settings.json");
        var custom = new AppSettings
        {
            SchemaVersion = 2,
            CapsuleWidth = 190.0,
            CapsuleHeight = 42.0,
            OffsetX = 15.0,
            OffsetY = 20.0,
            TargetMonitorIndex = 2,
            EnableHardwareMonitoring = true,
            EnableGpuMonitoring = true,
            DefaultHardwarePriority = 25,
            ToggleIslandHotkey = "Ctrl+Shift+D",
            StartWithWindows = true
        };

        File.WriteAllText(filePath, JsonSerializer.Serialize(custom));

        using var service = new SettingsService(filePath, debounceMilliseconds: 100);
        service.Load();

        Assert.Equal(190.0, service.CurrentSettings.CapsuleWidth);
        Assert.Equal(42.0, service.CurrentSettings.CapsuleHeight);
        Assert.Equal(15.0, service.CurrentSettings.OffsetX);
        Assert.Equal(20.0, service.CurrentSettings.OffsetY);
        Assert.Equal(2, service.CurrentSettings.TargetMonitorIndex);
        Assert.True(service.CurrentSettings.EnableHardwareMonitoring);
        Assert.True(service.CurrentSettings.EnableGpuMonitoring);
        Assert.Equal(25, service.CurrentSettings.DefaultHardwarePriority);
        Assert.Equal("Ctrl+Shift+D", service.CurrentSettings.ToggleIslandHotkey);
        Assert.True(service.CurrentSettings.StartWithWindows);
    }

    [Fact]
    public void Load_WhenJsonIsCorrupt_CreatesBakFile_LogsWarning_AndRegeneratesDefaultsWithoutCrashing()
    {
        string filePath = Path.Combine(_testDirectory, "settings.json");
        string bakPath = filePath + ".bak";
        const string corruptContent = "{ this is completely invalid json broken content: 1234, ";

        File.WriteAllText(filePath, corruptContent);

        string? loggedWarning = null;
        Exception? loggedEx = null;

        using var service = new SettingsService(
            filePath,
            warningLogger: (msg, ex) =>
            {
                loggedWarning = msg;
                loggedEx = ex;
            },
            debounceMilliseconds: 100);

        // Act - should not throw!
        service.Load();

        // Assert
        Assert.NotNull(loggedWarning);
        Assert.Contains("corrupt or unreadable", loggedWarning);
        Assert.NotNull(loggedEx);

        // .bak must have been created containing the original corrupt text
        Assert.True(File.Exists(bakPath));
        Assert.Equal(corruptContent, File.ReadAllText(bakPath));

        // settings.json must have been regenerated with valid defaults
        Assert.True(File.Exists(filePath));
        Assert.NotNull(service.CurrentSettings);
        Assert.Equal(AppSettings.CurrentSchemaVersion, service.CurrentSettings.SchemaVersion);
        Assert.Equal(200.0, service.CurrentSettings.CapsuleWidth);
        Assert.Equal(0.0, service.CurrentSettings.OffsetY);
    }

    [Fact]
    public void Load_WhenSchemaVersionIsOutdated_MigratesToCurrentSchemaAndSaves()
    {
        string filePath = Path.Combine(_testDirectory, "settings.json");
        // Legacy JSON schema with version 0 or missing
        const string legacyJson = """
        {
            "SchemaVersion": 0,
            "CapsuleWidth": 175.0,
            "DefaultMediaPriority": 45
        }
        """;

        File.WriteAllText(filePath, legacyJson);

        string? migrationNotice = null;
        using var service = new SettingsService(
            filePath,
            warningLogger: (msg, _) => migrationNotice = msg,
            debounceMilliseconds: 100);

        service.Load();

        Assert.NotNull(migrationNotice);
        Assert.Contains("Migrating settings schema", migrationNotice);
        Assert.Equal(AppSettings.CurrentSchemaVersion, service.CurrentSettings.SchemaVersion);
        Assert.Equal(175.0, service.CurrentSettings.CapsuleWidth);
        Assert.Equal(45, service.CurrentSettings.DefaultMediaPriority);
        Assert.Equal(OpenDynamic.Core.Animation.MotionMode.Auto, service.CurrentSettings.MotionMode);
        Assert.True(service.CurrentSettings.EnableNetworkAlerts);
        Assert.True(service.CurrentSettings.EnableDeviceAlerts);

        // File should now contain SchemaVersion = CurrentSchemaVersion
        string reloadedJson = File.ReadAllText(filePath);
        Assert.Contains($"\"SchemaVersion\": {AppSettings.CurrentSchemaVersion}", reloadedJson);
    }

    [Fact]
    public void Load_WhenSchemaVersionIs1_MigratesToSchemaVersion3_NormalizesNotchAndSetsMotionMode()
    {
        string filePath = Path.Combine(_testDirectory, "settings_v1.json");
        // Legacy JSON schema with version 1 (Phase 7 floating pill settings)
        const string v1Json = """
        {
            "SchemaVersion": 1,
            "OffsetY": 8.0,
            "CapsuleCornerRadius": 18.0,
            "CapsuleWidth": 160.0
        }
        """;

        File.WriteAllText(filePath, v1Json);

        using var service = new SettingsService(filePath, debounceMilliseconds: 100);
        service.Load();

        Assert.Equal(AppSettings.CurrentSchemaVersion, service.CurrentSettings.SchemaVersion);
        Assert.Equal(0.0, service.CurrentSettings.OffsetY);
        Assert.Equal(14.0, service.CurrentSettings.CapsuleCornerRadius);
        Assert.Equal(200.0, service.CurrentSettings.CapsuleWidth);
        Assert.Equal(OpenDynamic.Core.Animation.MotionMode.Auto, service.CurrentSettings.MotionMode);
        Assert.True(service.CurrentSettings.EnableNetworkAlerts);
        Assert.True(service.CurrentSettings.EnableDeviceAlerts);

        string reloadedJson = File.ReadAllText(filePath);
        Assert.Contains($"\"SchemaVersion\": {AppSettings.CurrentSchemaVersion}", reloadedJson);
        Assert.Contains("\"OffsetY\": 0", reloadedJson);
        Assert.Contains("\"CapsuleCornerRadius\": 14", reloadedJson);
    }

    [Fact]
    public void Load_WhenSchemaVersionIs2_MigratesToSchemaVersion4_SetsDefaultMotionModeAuto()
    {
        string filePath = Path.Combine(_testDirectory, "settings_v2.json");
        // Legacy JSON schema with version 2 (Phase 9 notch settings without MotionMode)
        const string v2Json = """
        {
            "SchemaVersion": 2,
            "OffsetY": 0.0,
            "CapsuleCornerRadius": 14.0,
            "CapsuleWidth": 200.0,
            "EnableMediaWidget": true
        }
        """;

        File.WriteAllText(filePath, v2Json);

        using var service = new SettingsService(filePath, debounceMilliseconds: 100);
        service.Load();

        Assert.Equal(AppSettings.CurrentSchemaVersion, service.CurrentSettings.SchemaVersion);
        Assert.Equal(OpenDynamic.Core.Animation.MotionMode.Auto, service.CurrentSettings.MotionMode);
        Assert.Equal(0.0, service.CurrentSettings.OffsetY);
        Assert.Equal(14.0, service.CurrentSettings.CapsuleCornerRadius);
        Assert.True(service.CurrentSettings.EnableMediaWidget);
        Assert.True(service.CurrentSettings.EnableNetworkAlerts);
        Assert.True(service.CurrentSettings.EnableDeviceAlerts);

        string reloadedJson = File.ReadAllText(filePath);
        Assert.Contains($"\"SchemaVersion\": {AppSettings.CurrentSchemaVersion}", reloadedJson);
        Assert.Contains("\"MotionMode\": \"Auto\"", reloadedJson);
    }

    [Fact]
    public void Load_WhenSchemaVersionIs3_MigratesToSchemaVersion5_SetsDefaultNetworkAndDeviceAlerts()
    {
        string filePath = Path.Combine(_testDirectory, "settings_v3.json");
        // Schema version 3 (Phase 10 with MotionMode but without Network and Device alerts)
        const string v3Json = """
        {
            "SchemaVersion": 3,
            "MotionMode": "Reduced",
            "CapsuleWidth": 220.0
        }
        """;

        File.WriteAllText(filePath, v3Json);

        using var service = new SettingsService(filePath, debounceMilliseconds: 100);
        service.Load();

        Assert.Equal(AppSettings.CurrentSchemaVersion, service.CurrentSettings.SchemaVersion);
        Assert.Equal(OpenDynamic.Core.Animation.MotionMode.Reduced, service.CurrentSettings.MotionMode);
        Assert.Equal(220.0, service.CurrentSettings.CapsuleWidth);
        Assert.True(service.CurrentSettings.EnableNetworkAlerts);
        Assert.Equal(65, service.CurrentSettings.DefaultNetworkPriority);
        Assert.Equal(3.0, service.CurrentSettings.NetworkTransientDurationSeconds);
        Assert.True(service.CurrentSettings.EnableDeviceAlerts);
        Assert.Equal(60, service.CurrentSettings.DefaultDevicePriority);
        Assert.Equal(3.0, service.CurrentSettings.DeviceTransientDurationSeconds);
        Assert.NotNull(service.CurrentSettings.IgnoredDeviceNames);
        Assert.Empty(service.CurrentSettings.IgnoredDeviceNames);

        string reloadedJson = File.ReadAllText(filePath);
        Assert.Contains($"\"SchemaVersion\": {AppSettings.CurrentSchemaVersion}", reloadedJson);
        Assert.Contains("\"EnableNetworkAlerts\": true", reloadedJson);
        Assert.Contains("\"EnableDeviceAlerts\": true", reloadedJson);
    }

    [Fact]
    public void Load_WhenSchemaVersionIs4_MigratesToCurrentSchema_SetsDefaultStopwatchAndPresets()
    {
        string filePath = Path.Combine(_testDirectory, "settings_v4.json");
        // Schema version 4 (Phase 11 with Network/Device alerts but without Stopwatch and TimerPresets)
        const string v4Json = """
        {
            "SchemaVersion": 4,
            "EnableNetworkAlerts": true,
            "DefaultNetworkPriority": 65,
            "CapsuleWidth": 210.0
        }
        """;

        File.WriteAllText(filePath, v4Json);

        using var service = new SettingsService(filePath, debounceMilliseconds: 100);
        service.Load();

        Assert.Equal(AppSettings.CurrentSchemaVersion, service.CurrentSettings.SchemaVersion);
        Assert.Equal(210.0, service.CurrentSettings.CapsuleWidth);
        Assert.True(service.CurrentSettings.EnableStopwatchWidget);
        Assert.Equal(45, service.CurrentSettings.DefaultStopwatchPriority);
        Assert.NotNull(service.CurrentSettings.TimerPresetsMinutes);
        Assert.Equal(new List<int> { 1, 5, 10, 15 }, service.CurrentSettings.TimerPresetsMinutes);
        Assert.True(service.CurrentSettings.EnableDynamicMediaColor);
        Assert.True(service.CurrentSettings.EnableMediaGestures);
        Assert.Equal(120.0, service.CurrentSettings.MediaGestureSensitivity);

        string reloadedJson = File.ReadAllText(filePath);
        Assert.Contains($"\"SchemaVersion\": {AppSettings.CurrentSchemaVersion}", reloadedJson);
        Assert.Contains("\"EnableStopwatchWidget\": true", reloadedJson);
        Assert.Contains("\"DefaultStopwatchPriority\": 45", reloadedJson);
    }

    [Fact]
    public void Load_WhenSchemaVersionIs5_MigratesToSchemaVersion6_SetsDefaultDynamicColorAndGestures()
    {
        string filePath = Path.Combine(_testDirectory, "settings_v5.json");
        // Schema version 5 (Phase 12 with Stopwatch but without Dynamic Color / Gestures)
        const string v5Json = """
        {
            "SchemaVersion": 5,
            "EnableMediaWidget": true,
            "EnableStopwatchWidget": true,
            "DefaultStopwatchPriority": 45,
            "CapsuleWidth": 205.0
        }
        """;

        File.WriteAllText(filePath, v5Json);

        using var service = new SettingsService(filePath, debounceMilliseconds: 100);
        service.Load();

        Assert.Equal(AppSettings.CurrentSchemaVersion, service.CurrentSettings.SchemaVersion);
        Assert.Equal(205.0, service.CurrentSettings.CapsuleWidth);
        Assert.True(service.CurrentSettings.EnableDynamicMediaColor);
        Assert.True(service.CurrentSettings.EnableMediaGestures);
        Assert.Equal(120.0, service.CurrentSettings.MediaGestureSensitivity);

        string reloadedJson = File.ReadAllText(filePath);
        Assert.Contains($"\"SchemaVersion\": {AppSettings.CurrentSchemaVersion}", reloadedJson);
        Assert.Contains("\"EnableDynamicMediaColor\": true", reloadedJson);
        Assert.Contains("\"EnableMediaGestures\": true", reloadedJson);
    }

    [Fact]
    public void Load_WhenMotionModeIsConfigured_PersistsAndDeserializesCorrectly()
    {
        string filePath = Path.Combine(_testDirectory, "settings_motion.json");
        var custom = new AppSettings
        {
            SchemaVersion = 3,
            MotionMode = OpenDynamic.Core.Animation.MotionMode.Reduced
        };

        File.WriteAllText(filePath, JsonSerializer.Serialize(custom));

        using var service = new SettingsService(filePath, debounceMilliseconds: 100);
        service.Load();

        Assert.Equal(OpenDynamic.Core.Animation.MotionMode.Reduced, service.CurrentSettings.MotionMode);
    }

    [Fact]
    public async Task SaveDebounced_CollapsesMultipleRapidCalls_WritesOnlyAfterDelay()
    {
        string filePath = Path.Combine(_testDirectory, "settings.json");
        using var service = new SettingsService(filePath, debounceMilliseconds: 120);

        service.Load();
        Assert.Equal(200.0, service.CurrentSettings.CapsuleWidth);

        // Rapid changes in slider simulation
        service.CurrentSettings.CapsuleWidth = 205.0;
        service.SaveDebounced();

        service.CurrentSettings.CapsuleWidth = 210.0;
        service.SaveDebounced();

        service.CurrentSettings.CapsuleWidth = 215.0;
        service.SaveDebounced();

        // Immediately after, disk file has not been written with 215 yet
        string immediatelyAfter = File.ReadAllText(filePath);
        Assert.DoesNotContain("215", immediatelyAfter);

        // Wait for debounce period (120ms + buffer)
        await Task.Delay(250);

        string afterDebounce = File.ReadAllText(filePath);
        Assert.Contains("215", afterDebounce);
    }

    [Fact]
    public void SaveImmediate_FlushesChangesInstantly()
    {
        string filePath = Path.Combine(_testDirectory, "settings.json");
        using var service = new SettingsService(filePath, debounceMilliseconds: 500);

        service.Load();
        service.CurrentSettings.CapsuleWidth = 220.0;
        service.SaveImmediate();

        string content = File.ReadAllText(filePath);
        Assert.Contains("220", content);
    }

    [Fact]
    public void Dispose_FlushesPendingChanges()
    {
        string filePath = Path.Combine(_testDirectory, "settings.json");
        {
            using var service = new SettingsService(filePath, debounceMilliseconds: 1000);
            service.Load();
            service.CurrentSettings.CapsuleWidth = 235.0;
            service.SaveDebounced(); // Pending in 1000ms
        } // Dispose called here

        string content = File.ReadAllText(filePath);
        Assert.Contains("235", content);
    }

    [Fact]
    public void AppSettings_CloneAndCopyFrom_ProperlyDuplicatesAllProperties()
    {
        var original = new AppSettings
        {
            SchemaVersion = 2,
            CapsuleWidth = 210.0,
            CapsuleHeight = 44.0,
            CapsuleCornerRadius = 22.0,
            OffsetX = 12.0,
            OffsetY = 16.0,
            TargetMonitorIndex = 1,
            ScaleFactor = 1.25,
            EnableMediaWidget = false,
            DefaultMediaPriority = 35,
            MediaPauseGracePeriodSeconds = 15,
            EnableVolumeWidget = false,
            DefaultVolumePriority = 85,
            VolumeTransientDurationSeconds = 2.5,
            EnableBatteryWidget = false,
            DefaultBatteryPriority = 95,
            BatteryChargerTransientDurationSeconds = 4.0,
            BatteryWarningTransientDurationSeconds = 4.0,
            BatteryLowThresholdPercent = 25,
            BatteryCriticalThresholdPercent = 12,
            HideOnFullscreen = false,
            EnableHardwareMonitoring = true,
            DefaultHardwarePriority = 15,
            HardwareSamplingIntervalSeconds = 3.0,
            EnableGpuMonitoring = true,
            EnableTimerWidget = false,
            DefaultTimerPriority = 55,
            DefaultTimerAlertPriority = 105,
            TimerAlertTransientDurationSeconds = 6.0,
            PomodoroWorkDurationMinutes = 30,
            PomodoroBreakDurationMinutes = 10,
            ToggleIslandHotkey = "Win+Alt+O",
            EnableGlobalHotkeys = false,
            StartWithWindows = true,
            MotionMode = OpenDynamic.Core.Animation.MotionMode.Reduced
        };

        var cloned = original.Clone();

        Assert.Equal(original.CapsuleWidth, cloned.CapsuleWidth);
        Assert.Equal(original.CapsuleHeight, cloned.CapsuleHeight);
        Assert.Equal(original.CapsuleCornerRadius, cloned.CapsuleCornerRadius);
        Assert.Equal(original.OffsetX, cloned.OffsetX);
        Assert.Equal(original.OffsetY, cloned.OffsetY);
        Assert.Equal(original.TargetMonitorIndex, cloned.TargetMonitorIndex);
        Assert.Equal(original.ScaleFactor, cloned.ScaleFactor);
        Assert.Equal(original.MotionMode, cloned.MotionMode);
        Assert.Equal(original.EnableMediaWidget, cloned.EnableMediaWidget);
        Assert.Equal(original.DefaultMediaPriority, cloned.DefaultMediaPriority);
        Assert.Equal(original.MediaPauseGracePeriodSeconds, cloned.MediaPauseGracePeriodSeconds);
        Assert.Equal(original.EnableDynamicMediaColor, cloned.EnableDynamicMediaColor);
        Assert.Equal(original.EnableMediaGestures, cloned.EnableMediaGestures);
        Assert.Equal(original.MediaGestureSensitivity, cloned.MediaGestureSensitivity);
        Assert.Equal(original.EnableVolumeWidget, cloned.EnableVolumeWidget);
        Assert.Equal(original.DefaultVolumePriority, cloned.DefaultVolumePriority);
        Assert.Equal(original.VolumeTransientDurationSeconds, cloned.VolumeTransientDurationSeconds);
        Assert.Equal(original.EnableBatteryWidget, cloned.EnableBatteryWidget);
        Assert.Equal(original.DefaultBatteryPriority, cloned.DefaultBatteryPriority);
        Assert.Equal(original.BatteryLowThresholdPercent, cloned.BatteryLowThresholdPercent);
        Assert.Equal(original.EnableHardwareMonitoring, cloned.EnableHardwareMonitoring);
        Assert.Equal(original.EnableGpuMonitoring, cloned.EnableGpuMonitoring);
        Assert.Equal(original.PomodoroWorkDurationMinutes, cloned.PomodoroWorkDurationMinutes);
        Assert.Equal(original.ToggleIslandHotkey, cloned.ToggleIslandHotkey);
        Assert.Equal(original.StartWithWindows, cloned.StartWithWindows);

        var destination = new AppSettings();
        destination.CopyFrom(original);

        Assert.Equal(original.CapsuleWidth, destination.CapsuleWidth);
        Assert.Equal(original.ToggleIslandHotkey, destination.ToggleIslandHotkey);
        Assert.Equal(original.StartWithWindows, destination.StartWithWindows);
        Assert.Equal(original.MotionMode, destination.MotionMode);
    }
}
