using System.Text.Json.Serialization;
using OpenDynamic.Core.Animation;

namespace OpenDynamic.Core.Settings;

/// <summary>
/// Application settings for openDynamic.
/// Holds configuration values for window placement, widgets, audio, power, hardware monitoring, timer, hotkeys, and motion.
/// </summary>
public sealed class AppSettings
{
    /// <summary>
    /// Current configuration schema version for migration tracking.
    /// Version 3 introduces the MotionMode setting (Auto, Reduced, Full).
    /// Version 4 introduces Network and Device alert settings, priorities, and ignored devices.
    /// Version 5 introduces Stopwatch widget settings, priority (45), and configurable timer presets.
    /// Version 6 introduces Dynamic Album Art Color and Horizontal Media Gestures with sensitivity.
    /// </summary>
    public const int CurrentSchemaVersion = 6;

    /// <summary>
    /// Configuration schema version. Defaults to <see cref="CurrentSchemaVersion"/>.
    /// </summary>
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    #region Window and Placement Settings

    /// <summary>
    /// Index of the target monitor where the island is displayed (0 = Primary monitor).
    /// </summary>
    public int TargetMonitorIndex { get; set; } = 0;

    /// <summary>
    /// Horizontal offset in DIPs from the screen center.
    /// Default is 0.0.
    /// </summary>
    public double OffsetX { get; set; } = 0.0;

    /// <summary>
    /// Vertical offset in DIPs from the top of the monitor area.
    /// Default is 0.0 DIP to anchor the notch flush against the top display bezel.
    /// </summary>
    public double OffsetY { get; set; } = 0.0;

    /// <summary>
    /// Width of the notch in compact mode in DIPs.
    /// Default is 200.0.
    /// </summary>
    public double CapsuleWidth { get; set; } = 200.0;

    /// <summary>
    /// Height of the notch in compact mode in DIPs.
    /// Default is 36.0.
    /// </summary>
    public double CapsuleHeight { get; set; } = 36.0;

    /// <summary>
    /// Corner radius of the notch bottom corners in compact mode in DIPs.
    /// Default is 14.0. Top corners are always flat (0 DIP).
    /// </summary>
    public double CapsuleCornerRadius { get; set; } = 14.0;

    /// <summary>
    /// Custom scale factor multiplier applied to the island dimensions.
    /// Default is 1.0 (100%).
    /// </summary>
    public double ScaleFactor { get; set; } = 1.0;

    #endregion

    #region Motion and Animation Settings

    /// <summary>
    /// Motion mode preference for island animations.
    /// <see cref="MotionMode.Auto"/> follows Windows system animation preference (Default).
    /// <see cref="MotionMode.Reduced"/> suppresses bouncing and decorative animations.
    /// <see cref="MotionMode.Full"/> enables complete spring physics.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<MotionMode>))]
    public MotionMode MotionMode { get; set; } = MotionMode.Auto;

    #endregion

    #region Multimedia Settings

    /// <summary>
    /// Flag to enable or disable the multimedia widget.
    /// Default is true.
    /// </summary>
    public bool EnableMediaWidget { get; set; } = true;

    /// <summary>
    /// Grace period in seconds to keep the media widget active after playback is paused.
    /// Default is 10 seconds.
    /// </summary>
    public int MediaPauseGracePeriodSeconds { get; set; } = 10;

    /// <summary>
    /// Default priority value for media activities.
    /// Default is 30.
    /// </summary>
    public int DefaultMediaPriority { get; set; } = 30;

    /// <summary>
    /// Flag to enable dynamic accent color extraction from current album art.
    /// Default is true.
    /// </summary>
    public bool EnableDynamicMediaColor { get; set; } = true;

    /// <summary>
    /// Flag to enable horizontal gestures (wheel tilt and touch drag) to skip media tracks.
    /// Default is true.
    /// </summary>
    public bool EnableMediaGestures { get; set; } = true;

    /// <summary>
    /// Sensitivity threshold for horizontal gestures in delta units.
    /// Default is 120.0 (standard mouse wheel step).
    /// </summary>
    public double MediaGestureSensitivity { get; set; } = 120.0;

    #endregion

    #region Volume Settings

    /// <summary>
    /// Flag to enable or disable the volume widget.
    /// Default is true.
    /// </summary>
    public bool EnableVolumeWidget { get; set; } = true;

    /// <summary>
    /// Default priority value for volume transient activities.
    /// Default is 80.
    /// </summary>
    public int DefaultVolumePriority { get; set; } = 80;

    /// <summary>
    /// Lifespan in seconds for volume transient notices.
    /// Default is 2.0 seconds.
    /// </summary>
    public double VolumeTransientDurationSeconds { get; set; } = 2.0;

    #endregion

    #region Battery Settings

    /// <summary>
    /// Flag to enable or disable the battery widget.
    /// Default is true.
    /// </summary>
    public bool EnableBatteryWidget { get; set; } = true;

    /// <summary>
    /// Default priority value for battery and charger alerts.
    /// Default is 90.
    /// </summary>
    public int DefaultBatteryPriority { get; set; } = 90;

    /// <summary>
    /// Lifespan in seconds for charger connect / disconnect transient alerts.
    /// Default is 3.0 seconds.
    /// </summary>
    public double BatteryChargerTransientDurationSeconds { get; set; } = 3.0;

    /// <summary>
    /// Lifespan in seconds for low / critical battery transient alerts.
    /// Default is 3.0 seconds.
    /// </summary>
    public double BatteryWarningTransientDurationSeconds { get; set; } = 3.0;

    /// <summary>
    /// Battery percentage threshold to trigger low battery warning.
    /// Default is 20%.
    /// </summary>
    public int BatteryLowThresholdPercent { get; set; } = 20;

    /// <summary>
    /// Battery percentage threshold to trigger critical battery warning.
    /// Default is 10%.
    /// </summary>
    public int BatteryCriticalThresholdPercent { get; set; } = 10;

    #endregion

    #region Fullscreen Settings

    /// <summary>
    /// Indicates whether the island should automatically hide when an application enters fullscreen mode.
    /// Default is true.
    /// </summary>
    public bool HideOnFullscreen { get; set; } = true;

    #endregion

    #region Hardware Settings

    /// <summary>
    /// Flag to enable hardware monitoring widget.
    /// Default is false.
    /// </summary>
    public bool EnableHardwareMonitoring { get; set; } = false;

    /// <summary>
    /// Default priority value for the hardware monitor widget.
    /// Default is 10.
    /// </summary>
    public int DefaultHardwarePriority { get; set; } = 10;

    /// <summary>
    /// Hardware sampling interval in seconds when widget is visible.
    /// Default is 2.0 seconds.
    /// </summary>
    public double HardwareSamplingIntervalSeconds { get; set; } = 2.0;

    /// <summary>
    /// Flag to enable GPU monitoring via performance counters.
    /// Strictly OFF by default to guarantee low resource consumption.
    /// </summary>
    public bool EnableGpuMonitoring { get; set; } = false;

    #endregion

    #region Timer Settings

    /// <summary>
    /// Flag to enable or disable the timer widget.
    /// Default is true.
    /// </summary>
    public bool EnableTimerWidget { get; set; } = true;

    /// <summary>
    /// Default priority value for active running countdown timer.
    /// Default is 50.
    /// </summary>
    public int DefaultTimerPriority { get; set; } = 50;

    /// <summary>
    /// Default priority value for completed timer expiration transient alert.
    /// Default is 100.
    /// </summary>
    public int DefaultTimerAlertPriority { get; set; } = 100;

    /// <summary>
    /// Lifespan in seconds for completed timer expiration alert.
    /// Default is 5.0 seconds.
    /// </summary>
    public double TimerAlertTransientDurationSeconds { get; set; } = 5.0;

    /// <summary>
    /// Configured work duration for Pomodoro mode in minutes.
    /// Default is 25 minutes.
    /// </summary>
    public int PomodoroWorkDurationMinutes { get; set; } = 25;

    /// <summary>
    /// Configured break duration for Pomodoro mode in minutes.
    /// Default is 5 minutes.
    /// </summary>
    public int PomodoroBreakDurationMinutes { get; set; } = 5;

    /// <summary>
    /// Quick preset durations in minutes displayed in the expanded timer widget and settings.
    /// Default values: [1, 5, 10, 15].
    /// </summary>
    public List<int> TimerPresetsMinutes { get; set; } = new() { 1, 5, 10, 15 };

    #endregion

    #region Stopwatch Settings

    /// <summary>
    /// Flag to enable or disable the stopwatch widget.
    /// Default is true.
    /// </summary>
    public bool EnableStopwatchWidget { get; set; } = true;

    /// <summary>
    /// Default priority value for active running stopwatch activity.
    /// Default is 45.
    /// </summary>
    public int DefaultStopwatchPriority { get; set; } = 45;

    #endregion

    #region Network Settings

    /// <summary>
    /// Flag to enable or disable network connectivity alerts.
    /// Default is true.
    /// </summary>
    public bool EnableNetworkAlerts { get; set; } = true;

    /// <summary>
    /// Default priority value for network transient notices.
    /// Default is 65.
    /// </summary>
    public int DefaultNetworkPriority { get; set; } = 65;

    /// <summary>
    /// Lifespan in seconds for network transient alerts.
    /// Default is 3.0 seconds.
    /// </summary>
    public double NetworkTransientDurationSeconds { get; set; } = 3.0;

    #endregion

    #region Device Settings

    /// <summary>
    /// Flag to enable or disable peripheral device connection and disconnection alerts.
    /// Default is true.
    /// </summary>
    public bool EnableDeviceAlerts { get; set; } = true;

    /// <summary>
    /// Default priority value for device transient notices.
    /// Default is 60.
    /// </summary>
    public int DefaultDevicePriority { get; set; } = 60;

    /// <summary>
    /// Lifespan in seconds for device transient alerts.
    /// Default is 3.0 seconds.
    /// </summary>
    public double DeviceTransientDurationSeconds { get; set; } = 3.0;

    /// <summary>
    /// List of friendly device names or device IDs ignored from transient alert notifications.
    /// </summary>
    public List<string> IgnoredDeviceNames { get; set; } = new();

    #endregion

    #region Hotkeys and Autostart

    /// <summary>
    /// Key combination to toggle island visibility.
    /// Default is "Win+Ctrl+I".
    /// </summary>
    public string ToggleIslandHotkey { get; set; } = "Win+Ctrl+I";

    /// <summary>
    /// Flag to enable or disable global hotkeys.
    /// Default is true.
    /// </summary>
    public bool EnableGlobalHotkeys { get; set; } = true;

    /// <summary>
    /// Flag indicating whether openDynamic should start with Windows.
    /// Default is false.
    /// </summary>
    public bool StartWithWindows { get; set; } = false;

    #endregion

    /// <summary>
    /// Creates a deep copy of the current settings instance.
    /// </summary>
    public AppSettings Clone()
    {
        return new AppSettings
        {
            SchemaVersion = this.SchemaVersion,
            TargetMonitorIndex = this.TargetMonitorIndex,
            OffsetX = this.OffsetX,
            OffsetY = this.OffsetY,
            CapsuleWidth = this.CapsuleWidth,
            CapsuleHeight = this.CapsuleHeight,
            CapsuleCornerRadius = this.CapsuleCornerRadius,
            ScaleFactor = this.ScaleFactor,
            EnableMediaWidget = this.EnableMediaWidget,
            MediaPauseGracePeriodSeconds = this.MediaPauseGracePeriodSeconds,
            DefaultMediaPriority = this.DefaultMediaPriority,
            EnableDynamicMediaColor = this.EnableDynamicMediaColor,
            EnableMediaGestures = this.EnableMediaGestures,
            MediaGestureSensitivity = this.MediaGestureSensitivity,
            EnableVolumeWidget = this.EnableVolumeWidget,
            DefaultVolumePriority = this.DefaultVolumePriority,
            VolumeTransientDurationSeconds = this.VolumeTransientDurationSeconds,
            EnableBatteryWidget = this.EnableBatteryWidget,
            DefaultBatteryPriority = this.DefaultBatteryPriority,
            BatteryChargerTransientDurationSeconds = this.BatteryChargerTransientDurationSeconds,
            BatteryWarningTransientDurationSeconds = this.BatteryWarningTransientDurationSeconds,
            BatteryLowThresholdPercent = this.BatteryLowThresholdPercent,
            BatteryCriticalThresholdPercent = this.BatteryCriticalThresholdPercent,
            HideOnFullscreen = this.HideOnFullscreen,
            EnableHardwareMonitoring = this.EnableHardwareMonitoring,
            DefaultHardwarePriority = this.DefaultHardwarePriority,
            HardwareSamplingIntervalSeconds = this.HardwareSamplingIntervalSeconds,
            EnableGpuMonitoring = this.EnableGpuMonitoring,
            EnableTimerWidget = this.EnableTimerWidget,
            DefaultTimerPriority = this.DefaultTimerPriority,
            DefaultTimerAlertPriority = this.DefaultTimerAlertPriority,
            TimerAlertTransientDurationSeconds = this.TimerAlertTransientDurationSeconds,
            PomodoroWorkDurationMinutes = this.PomodoroWorkDurationMinutes,
            PomodoroBreakDurationMinutes = this.PomodoroBreakDurationMinutes,
            TimerPresetsMinutes = new List<int>(this.TimerPresetsMinutes ?? new List<int> { 1, 5, 10, 15 }),
            EnableStopwatchWidget = this.EnableStopwatchWidget,
            DefaultStopwatchPriority = this.DefaultStopwatchPriority,
            ToggleIslandHotkey = this.ToggleIslandHotkey,
            EnableGlobalHotkeys = this.EnableGlobalHotkeys,
            StartWithWindows = this.StartWithWindows,
            MotionMode = this.MotionMode,
            EnableNetworkAlerts = this.EnableNetworkAlerts,
            DefaultNetworkPriority = this.DefaultNetworkPriority,
            NetworkTransientDurationSeconds = this.NetworkTransientDurationSeconds,
            EnableDeviceAlerts = this.EnableDeviceAlerts,
            DefaultDevicePriority = this.DefaultDevicePriority,
            DeviceTransientDurationSeconds = this.DeviceTransientDurationSeconds,
            IgnoredDeviceNames = new List<string>(this.IgnoredDeviceNames)
        };
    }

    /// <summary>
    /// Copies all values from another <see cref="AppSettings"/> instance into this instance.
    /// </summary>
    public void CopyFrom(AppSettings other)
    {
        ArgumentNullException.ThrowIfNull(other);

        SchemaVersion = other.SchemaVersion;
        TargetMonitorIndex = other.TargetMonitorIndex;
        OffsetX = other.OffsetX;
        OffsetY = other.OffsetY;
        CapsuleWidth = other.CapsuleWidth;
        CapsuleHeight = other.CapsuleHeight;
        CapsuleCornerRadius = other.CapsuleCornerRadius;
        ScaleFactor = other.ScaleFactor;
        MotionMode = other.MotionMode;
        EnableMediaWidget = other.EnableMediaWidget;
        MediaPauseGracePeriodSeconds = other.MediaPauseGracePeriodSeconds;
        DefaultMediaPriority = other.DefaultMediaPriority;
        EnableDynamicMediaColor = other.EnableDynamicMediaColor;
        EnableMediaGestures = other.EnableMediaGestures;
        MediaGestureSensitivity = other.MediaGestureSensitivity;
        EnableVolumeWidget = other.EnableVolumeWidget;
        DefaultVolumePriority = other.DefaultVolumePriority;
        VolumeTransientDurationSeconds = other.VolumeTransientDurationSeconds;
        EnableBatteryWidget = other.EnableBatteryWidget;
        DefaultBatteryPriority = other.DefaultBatteryPriority;
        BatteryChargerTransientDurationSeconds = other.BatteryChargerTransientDurationSeconds;
        BatteryWarningTransientDurationSeconds = other.BatteryWarningTransientDurationSeconds;
        BatteryLowThresholdPercent = other.BatteryLowThresholdPercent;
        BatteryCriticalThresholdPercent = other.BatteryCriticalThresholdPercent;
        HideOnFullscreen = other.HideOnFullscreen;
        EnableHardwareMonitoring = other.EnableHardwareMonitoring;
        DefaultHardwarePriority = other.DefaultHardwarePriority;
        HardwareSamplingIntervalSeconds = other.HardwareSamplingIntervalSeconds;
        EnableGpuMonitoring = other.EnableGpuMonitoring;
        EnableTimerWidget = other.EnableTimerWidget;
        DefaultTimerPriority = other.DefaultTimerPriority;
        DefaultTimerAlertPriority = other.DefaultTimerAlertPriority;
        TimerAlertTransientDurationSeconds = other.TimerAlertTransientDurationSeconds;
        PomodoroWorkDurationMinutes = other.PomodoroWorkDurationMinutes;
        PomodoroBreakDurationMinutes = other.PomodoroBreakDurationMinutes;
        TimerPresetsMinutes = new List<int>(other.TimerPresetsMinutes ?? new List<int> { 1, 5, 10, 15 });
        EnableStopwatchWidget = other.EnableStopwatchWidget;
        DefaultStopwatchPriority = other.DefaultStopwatchPriority;
        ToggleIslandHotkey = other.ToggleIslandHotkey;
        EnableGlobalHotkeys = other.EnableGlobalHotkeys;
        StartWithWindows = other.StartWithWindows;
        EnableNetworkAlerts = other.EnableNetworkAlerts;
        DefaultNetworkPriority = other.DefaultNetworkPriority;
        NetworkTransientDurationSeconds = other.NetworkTransientDurationSeconds;
        EnableDeviceAlerts = other.EnableDeviceAlerts;
        DefaultDevicePriority = other.DefaultDevicePriority;
        DeviceTransientDurationSeconds = other.DeviceTransientDurationSeconds;
        IgnoredDeviceNames = new List<string>(other.IgnoredDeviceNames ?? Enumerable.Empty<string>());
    }
}
