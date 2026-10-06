using System.Text.Json.Serialization;
using OpenDynamic.Core.Animation;
using OpenDynamic.Core.Audio.Spectrum;
using OpenDynamic.Core.Clock;

namespace OpenDynamic.Core.Settings;

/// <summary>
/// Application settings for openDynamic.
/// Holds configuration values for window placement, widgets, audio, power, hardware monitoring, timer, hotkeys, motion, and ambient clock.
/// </summary>
public sealed class AppSettings
{
    /// <summary>
    /// Current configuration schema version for migration tracking.
    /// Version 3 introduces the MotionMode setting (Auto, Reduced, Full).
    /// Version 4 introduces Network and Device alert settings, priorities, and ignored devices.
    /// Version 5 introduces Stopwatch widget settings, priority (45), and configurable timer presets.
    /// Version 6 introduces Dynamic Album Art Color and Horizontal Media Gestures with sensitivity.
    /// Version 7 introduces Opt-In In-Memory Clipboard History, transient notices, preview toggle, capacity and expiration settings.
    /// Version 8 introduces Microphone & Camera indicators, privacy transient alerts, priority (85), and ignored privacy apps.
    /// Version 9 introduces Audio Spectrum Visualizer mode (Disabled, Simulated, Real).
    /// Version 10/11 maintained schema stability across integration retirements (Task R1).
    /// Version 12 introduces Ambient Clock settings (enabled by default, Auto format, priority 5).
    /// Version 13 introduces Energy Saver reactive alerts, priority (88), and efficient resource profile settings.
    /// Version 14 introduces Screenshot Preview widget settings, priority (75), thumbnail toggle, watched folder, temporal retention, and Recycle Bin toggle.
    /// </summary>
    public const int CurrentSchemaVersion = 14;

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
    [JsonConverter(typeof(LenientEnumConverter<MotionMode>))]
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

    /// <summary>
    /// Audio visualizer operational mode in the media widget notch.
    /// <see cref="AudioVisualizerMode.Disabled"/> hides equalizer bars.
    /// <see cref="AudioVisualizerMode.Simulated"/> renders procedural wave bars.
    /// <see cref="AudioVisualizerMode.Real"/> performs real-time WASAPI loopback capture and native FFT analysis.
    /// Default is <see cref="AudioVisualizerMode.Real"/>.
    /// </summary>
    [JsonConverter(typeof(LenientEnumConverter<AudioVisualizerMode>))]
    public AudioVisualizerMode VisualizerMode { get; set; } = AudioVisualizerMode.Real;

    /// <summary>
    /// Convenience helper indicating if real reactive loopback capture is enabled.
    /// </summary>
    [JsonIgnore]
    public bool IsReactiveVisualizerEnabled => VisualizerMode == AudioVisualizerMode.Real;

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

    #region Energy Saver Settings

    /// <summary>
    /// Flag to enable transient alerts when Windows Energy Saver state transitions.
    /// Default is true.
    /// </summary>
    public bool EnableEnergySaverAlerts { get; set; } = true;

    /// <summary>
    /// Default priority value for energy saver transient alerts.
    /// Default is 88.
    /// </summary>
    public int DefaultEnergySaverPriority { get; set; } = 88;

    /// <summary>
    /// Lifespan in seconds for energy saver transient notices.
    /// Default is 3.0 seconds.
    /// </summary>
    public double EnergySaverTransientDurationSeconds { get; set; } = 3.0;

    /// <summary>
    /// Flag to automatically activate openDynamic's efficient resource profile when Windows Energy Saver is active.
    /// Default is true.
    /// </summary>
    public bool EnableEnergySaverEfficientMode { get; set; } = true;

    /// <summary>
    /// Flag to reduce island spring animations (MotionProfile.Reduced) in efficient mode when MotionMode is Auto.
    /// Explicit user choices (Full or Reduced) are strictly preserved.
    /// Default is true.
    /// </summary>
    public bool EnergySaverReduceAnimations { get; set; } = true;

    /// <summary>
    /// Flag to cap audio spectrum visualizer to simulated procedural mode in efficient mode.
    /// Default is true.
    /// </summary>
    public bool EnergySaverCapAudioVisualizer { get; set; } = true;

    /// <summary>
    /// Flag to throttle hardware sampling interval from 2s to 5s in efficient mode.
    /// Default is true.
    /// </summary>
    public bool EnergySaverThrottleHardwareSampling { get; set; } = true;

    /// <summary>
    /// Sampling interval in seconds for hardware monitoring during energy saver mode.
    /// Default is 5.0 seconds.
    /// </summary>
    public double EnergySaverHardwareSamplingIntervalSeconds { get; set; } = 5.0;

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

    #region Clipboard Settings

    /// <summary>
    /// Flag to enable or disable the in-memory clipboard history widget.
    /// Strictly OFF by default for absolute privacy (Golden Rule 10).
    /// </summary>
    public bool EnableClipboardWidget { get; set; } = false;

    /// <summary>
    /// Default priority value for clipboard transient notices.
    /// Default is 55.
    /// </summary>
    public int DefaultClipboardPriority { get; set; } = 55;

    /// <summary>
    /// Lifespan in seconds for clipboard transient alerts.
    /// Default is 2.0 seconds.
    /// </summary>
    public double ClipboardTransientDurationSeconds { get; set; } = 2.0;

    /// <summary>
    /// Flag to show sanitized text preview in clipboard alerts.
    /// If false, only displays generic "Texto copiado".
    /// Default is true.
    /// </summary>
    public bool ShowClipboardPreview { get; set; } = true;

    /// <summary>
    /// Maximum number of recent items kept in memory (1 to 10).
    /// Default is 5.
    /// </summary>
    public int ClipboardHistoryCapacity { get; set; } = 5;

    /// <summary>
    /// Lifespan in minutes before an in-memory clipboard entry expires and is purged.
    /// Default is 10 minutes.
    /// </summary>
    public int ClipboardExpirationMinutes { get; set; } = 10;

    #endregion

    #region Privacy Indicator Settings

    /// <summary>
    /// Flag to enable or disable the microphone in-use indicator badge on the notch.
    /// Default is true.
    /// </summary>
    public bool EnableMicrophoneIndicator { get; set; } = true;

    /// <summary>
    /// Flag to enable or disable the camera in-use indicator badge on the notch.
    /// Default is true.
    /// </summary>
    public bool EnableCameraIndicator { get; set; } = true;

    /// <summary>
    /// Flag to enable or disable transient toast alerts when microphone or camera access begins or ceases.
    /// Default is true.
    /// </summary>
    public bool EnablePrivacyAlerts { get; set; } = true;

    /// <summary>
    /// Default priority value for privacy transient alerts.
    /// Default is 85.
    /// </summary>
    public int DefaultPrivacyPriority { get; set; } = 85;

    /// <summary>
    /// Lifespan in seconds for privacy transient notices.
    /// Default is 3.0 seconds.
    /// </summary>
    public double PrivacyTransientDurationSeconds { get; set; } = 3.0;

    /// <summary>
    /// List of friendly application names, process names, or package family names ignored from privacy alerts and indicators.
    /// </summary>
    public List<string> IgnoredPrivacyApps { get; set; } = new();

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

    #region Ambient Clock Settings

    /// <summary>
    /// Flag to enable or disable the ambient clock widget on hover in idle mode.
    /// Default is true.
    /// </summary>
    public bool EnableAmbientClock { get; set; } = true;

    /// <summary>
    /// Preferred time format for the clock (Auto, TwelveHour, TwentyFourHour).
    /// Default is Auto (follows system culture).
    /// </summary>
    [JsonConverter(typeof(LenientEnumConverter<ClockTimeFormat>))]
    public ClockTimeFormat ClockTimeFormat { get; set; } = ClockTimeFormat.Auto;

    /// <summary>
    /// Flag to show or hide seconds in the time display.
    /// Default is false (aligns to minute boundary for 0% CPU).
    /// </summary>
    public bool ClockShowSeconds { get; set; } = false;

    /// <summary>
    /// Flag to show or hide the date display in expanded mode.
    /// Default is true.
    /// </summary>
    public bool ClockShowDate { get; set; } = true;

    /// <summary>
    /// Flag to show or hide the ISO week number in expanded mode.
    /// Default is false.
    /// </summary>
    public bool ClockShowWeekNumber { get; set; } = false;

    /// <summary>
    /// Default priority for the ambient clock widget.
    /// Default is 5 (ActivityPriority.AmbientClock).
    /// </summary>
    public int DefaultAmbientClockPriority { get; set; } = 5;

    #endregion

    #region Screenshot Preview Settings

    /// <summary>
    /// Flag to enable or disable the reactive screenshot preview widget and FileSystemWatcher.
    /// Default is true.
    /// </summary>
    public bool EnableScreenshotWidget { get; set; } = true;

    /// <summary>
    /// Flag to show the decoded screenshot thumbnail image in the compact and expanded notch views.
    /// If false, displays only the notification notice without rendering the image.
    /// Default is true.
    /// </summary>
    public bool ShowScreenshotThumbnail { get; set; } = true;

    /// <summary>
    /// Default priority value for screenshot transient notices.
    /// Default is 75 (ActivityPriority.Screenshot).
    /// </summary>
    public int DefaultScreenshotPriority { get; set; } = 75;

    /// <summary>
    /// Lifespan in seconds for screenshot transient notices in compact mode (paused while cursor hovers).
    /// Default is 6.0 seconds.
    /// </summary>
    public double ScreenshotTransientDurationSeconds { get; set; } = 6.0;

    /// <summary>
    /// Maximum number of recent screenshot paths kept in volatile RAM history.
    /// Default is 5.
    /// </summary>
    public int ScreenshotHistoryCapacity { get; set; } = 5;

    /// <summary>
    /// Temporal retention in minutes before an in-memory screenshot history entry expires.
    /// Default is 30 minutes.
    /// </summary>
    public int ScreenshotHistoryRetentionMinutes { get; set; } = 30;

    /// <summary>
    /// Flag to enable or disable the Recycle Bin action (with double confirmation) in the expanded screenshot view.
    /// Default is true.
    /// </summary>
    public bool EnableScreenshotTrashAction { get; set; } = true;

    /// <summary>
    /// Optional additional folder path watched for screenshots (e.g., custom Snipping Tool folder).
    /// Default is empty string.
    /// </summary>
    public string AdditionalScreenshotFolder { get; set; } = string.Empty;

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
            VisualizerMode = this.VisualizerMode,
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
            ToggleIslandHotkey = this.ToggleIslandHotkey ?? "Win+Ctrl+I",
            EnableGlobalHotkeys = this.EnableGlobalHotkeys,
            StartWithWindows = this.StartWithWindows,
            MotionMode = this.MotionMode,
            EnableNetworkAlerts = this.EnableNetworkAlerts,
            DefaultNetworkPriority = this.DefaultNetworkPriority,
            NetworkTransientDurationSeconds = this.NetworkTransientDurationSeconds,
            EnableDeviceAlerts = this.EnableDeviceAlerts,
            DefaultDevicePriority = this.DefaultDevicePriority,
            DeviceTransientDurationSeconds = this.DeviceTransientDurationSeconds,
            IgnoredDeviceNames = new List<string>(this.IgnoredDeviceNames ?? Enumerable.Empty<string>()),
            EnableClipboardWidget = this.EnableClipboardWidget,
            DefaultClipboardPriority = this.DefaultClipboardPriority,
            ClipboardTransientDurationSeconds = this.ClipboardTransientDurationSeconds,
            ShowClipboardPreview = this.ShowClipboardPreview,
            ClipboardHistoryCapacity = this.ClipboardHistoryCapacity,
            ClipboardExpirationMinutes = this.ClipboardExpirationMinutes,
            EnableMicrophoneIndicator = this.EnableMicrophoneIndicator,
            EnableCameraIndicator = this.EnableCameraIndicator,
            EnablePrivacyAlerts = this.EnablePrivacyAlerts,
            DefaultPrivacyPriority = this.DefaultPrivacyPriority,
            PrivacyTransientDurationSeconds = this.PrivacyTransientDurationSeconds,
            IgnoredPrivacyApps = new List<string>(this.IgnoredPrivacyApps ?? Enumerable.Empty<string>()),
            EnableAmbientClock = this.EnableAmbientClock,
            ClockTimeFormat = this.ClockTimeFormat,
            ClockShowSeconds = this.ClockShowSeconds,
            ClockShowDate = this.ClockShowDate,
            ClockShowWeekNumber = this.ClockShowWeekNumber,
            DefaultAmbientClockPriority = this.DefaultAmbientClockPriority,
            EnableEnergySaverAlerts = this.EnableEnergySaverAlerts,
            DefaultEnergySaverPriority = this.DefaultEnergySaverPriority,
            EnergySaverTransientDurationSeconds = this.EnergySaverTransientDurationSeconds,
            EnableEnergySaverEfficientMode = this.EnableEnergySaverEfficientMode,
            EnergySaverReduceAnimations = this.EnergySaverReduceAnimations,
            EnergySaverCapAudioVisualizer = this.EnergySaverCapAudioVisualizer,
            EnergySaverThrottleHardwareSampling = this.EnergySaverThrottleHardwareSampling,
            EnergySaverHardwareSamplingIntervalSeconds = this.EnergySaverHardwareSamplingIntervalSeconds,
            EnableScreenshotWidget = this.EnableScreenshotWidget,
            ShowScreenshotThumbnail = this.ShowScreenshotThumbnail,
            DefaultScreenshotPriority = this.DefaultScreenshotPriority,
            ScreenshotTransientDurationSeconds = this.ScreenshotTransientDurationSeconds,
            ScreenshotHistoryCapacity = this.ScreenshotHistoryCapacity,
            ScreenshotHistoryRetentionMinutes = this.ScreenshotHistoryRetentionMinutes,
            EnableScreenshotTrashAction = this.EnableScreenshotTrashAction,
            AdditionalScreenshotFolder = this.AdditionalScreenshotFolder ?? string.Empty
        };
    }

    /// <summary>
    /// Sanitizes and clamps all numeric ranges, enum values, strings, and collections to valid domain bounds.
    /// </summary>
    public void SanitizeAndClamp()
    {
        TargetMonitorIndex = Math.Max(0, TargetMonitorIndex);
        OffsetX = double.IsFinite(OffsetX) ? Math.Clamp(OffsetX, -2000.0, 2000.0) : 0.0;
        OffsetY = double.IsFinite(OffsetY) ? Math.Clamp(OffsetY, 0.0, 500.0) : 0.0;
        CapsuleWidth = double.IsFinite(CapsuleWidth) ? Math.Clamp(CapsuleWidth, 120.0, 500.0) : 200.0;
        CapsuleHeight = double.IsFinite(CapsuleHeight) ? Math.Clamp(CapsuleHeight, 24.0, 120.0) : 36.0;
        CapsuleCornerRadius = double.IsFinite(CapsuleCornerRadius) ? Math.Clamp(CapsuleCornerRadius, 0.0, CapsuleHeight) : 14.0;
        ScaleFactor = double.IsFinite(ScaleFactor) ? Math.Clamp(ScaleFactor, 0.5, 2.5) : 1.0;

        if (!Enum.IsDefined(typeof(MotionMode), MotionMode))
        {
            MotionMode = MotionMode.Auto;
        }

        if (!Enum.IsDefined(typeof(AudioVisualizerMode), VisualizerMode))
        {
            VisualizerMode = AudioVisualizerMode.Real;
        }

        if (!Enum.IsDefined(typeof(ClockTimeFormat), ClockTimeFormat))
        {
            ClockTimeFormat = ClockTimeFormat.Auto;
        }

        MediaPauseGracePeriodSeconds = Math.Clamp(MediaPauseGracePeriodSeconds, 1, 300);
        MediaGestureSensitivity = double.IsFinite(MediaGestureSensitivity) ? Math.Clamp(MediaGestureSensitivity, 20.0, 500.0) : 120.0;

        DefaultMediaPriority = Math.Clamp(DefaultMediaPriority, 1, 100);
        DefaultVolumePriority = Math.Clamp(DefaultVolumePriority, 1, 100);
        DefaultBatteryPriority = Math.Clamp(DefaultBatteryPriority, 1, 100);
        DefaultEnergySaverPriority = Math.Clamp(DefaultEnergySaverPriority, 1, 100);
        DefaultHardwarePriority = Math.Clamp(DefaultHardwarePriority, 1, 100);
        DefaultTimerPriority = Math.Clamp(DefaultTimerPriority, 1, 100);
        DefaultTimerAlertPriority = Math.Clamp(DefaultTimerAlertPriority, 1, 100);
        DefaultStopwatchPriority = Math.Clamp(DefaultStopwatchPriority, 1, 100);
        DefaultNetworkPriority = Math.Clamp(DefaultNetworkPriority, 1, 100);
        DefaultDevicePriority = Math.Clamp(DefaultDevicePriority, 1, 100);
        DefaultClipboardPriority = Math.Clamp(DefaultClipboardPriority, 1, 100);
        DefaultPrivacyPriority = Math.Clamp(DefaultPrivacyPriority, 1, 100);
        DefaultAmbientClockPriority = Math.Clamp(DefaultAmbientClockPriority, 1, 100);
        DefaultScreenshotPriority = Math.Clamp(DefaultScreenshotPriority, 1, 100);

        VolumeTransientDurationSeconds = double.IsFinite(VolumeTransientDurationSeconds) ? Math.Clamp(VolumeTransientDurationSeconds, 0.5, 30.0) : 2.0;
        BatteryChargerTransientDurationSeconds = double.IsFinite(BatteryChargerTransientDurationSeconds) ? Math.Clamp(BatteryChargerTransientDurationSeconds, 0.5, 30.0) : 3.0;
        BatteryWarningTransientDurationSeconds = double.IsFinite(BatteryWarningTransientDurationSeconds) ? Math.Clamp(BatteryWarningTransientDurationSeconds, 0.5, 30.0) : 3.0;
        EnergySaverTransientDurationSeconds = double.IsFinite(EnergySaverTransientDurationSeconds) ? Math.Clamp(EnergySaverTransientDurationSeconds, 0.5, 30.0) : 3.0;
        TimerAlertTransientDurationSeconds = double.IsFinite(TimerAlertTransientDurationSeconds) ? Math.Clamp(TimerAlertTransientDurationSeconds, 0.5, 30.0) : 5.0;
        NetworkTransientDurationSeconds = double.IsFinite(NetworkTransientDurationSeconds) ? Math.Clamp(NetworkTransientDurationSeconds, 0.5, 30.0) : 3.0;
        DeviceTransientDurationSeconds = double.IsFinite(DeviceTransientDurationSeconds) ? Math.Clamp(DeviceTransientDurationSeconds, 0.5, 30.0) : 3.0;
        ClipboardTransientDurationSeconds = double.IsFinite(ClipboardTransientDurationSeconds) ? Math.Clamp(ClipboardTransientDurationSeconds, 0.5, 30.0) : 2.0;
        PrivacyTransientDurationSeconds = double.IsFinite(PrivacyTransientDurationSeconds) ? Math.Clamp(PrivacyTransientDurationSeconds, 0.5, 30.0) : 3.0;
        ScreenshotTransientDurationSeconds = double.IsFinite(ScreenshotTransientDurationSeconds) ? Math.Clamp(ScreenshotTransientDurationSeconds, 0.5, 30.0) : 6.0;

        BatteryCriticalThresholdPercent = Math.Clamp(BatteryCriticalThresholdPercent, 1, 50);
        BatteryLowThresholdPercent = Math.Clamp(BatteryLowThresholdPercent, BatteryCriticalThresholdPercent + 1, 90);

        HardwareSamplingIntervalSeconds = double.IsFinite(HardwareSamplingIntervalSeconds) ? Math.Clamp(HardwareSamplingIntervalSeconds, 0.5, 60.0) : 2.0;
        EnergySaverHardwareSamplingIntervalSeconds = double.IsFinite(EnergySaverHardwareSamplingIntervalSeconds) ? Math.Clamp(EnergySaverHardwareSamplingIntervalSeconds, 1.0, 60.0) : 5.0;

        PomodoroWorkDurationMinutes = Math.Clamp(PomodoroWorkDurationMinutes, 1, 180);
        PomodoroBreakDurationMinutes = Math.Clamp(PomodoroBreakDurationMinutes, 1, 60);

        if (TimerPresetsMinutes == null || TimerPresetsMinutes.Count == 0)
        {
            TimerPresetsMinutes = new List<int> { 1, 5, 10, 15 };
        }
        else
        {
            var sanitizedPresets = TimerPresetsMinutes
                .Select(m => Math.Clamp(m, 1, 180))
                .Distinct()
                .Take(8)
                .ToList();

            TimerPresetsMinutes = sanitizedPresets.Count > 0 ? sanitizedPresets : new List<int> { 1, 5, 10, 15 };
        }

        ClipboardHistoryCapacity = Math.Clamp(ClipboardHistoryCapacity, 1, 10);
        ClipboardExpirationMinutes = Math.Clamp(ClipboardExpirationMinutes, 1, 1440);
        ScreenshotHistoryCapacity = Math.Clamp(ScreenshotHistoryCapacity, 1, 20);
        ScreenshotHistoryRetentionMinutes = Math.Clamp(ScreenshotHistoryRetentionMinutes, 1, 1440);

        IgnoredDeviceNames = (IgnoredDeviceNames ?? new List<string>())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        IgnoredPrivacyApps = (IgnoredPrivacyApps ?? new List<string>())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        AdditionalScreenshotFolder ??= string.Empty;
        ToggleIslandHotkey = string.IsNullOrWhiteSpace(ToggleIslandHotkey) ? "Win+Ctrl+I" : ToggleIslandHotkey.Trim();
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
        VisualizerMode = other.VisualizerMode;
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
        ToggleIslandHotkey = other.ToggleIslandHotkey ?? "Win+Ctrl+I";
        EnableGlobalHotkeys = other.EnableGlobalHotkeys;
        StartWithWindows = other.StartWithWindows;
        EnableNetworkAlerts = other.EnableNetworkAlerts;
        DefaultNetworkPriority = other.DefaultNetworkPriority;
        NetworkTransientDurationSeconds = other.NetworkTransientDurationSeconds;
        EnableDeviceAlerts = other.EnableDeviceAlerts;
        DefaultDevicePriority = other.DefaultDevicePriority;
        DeviceTransientDurationSeconds = other.DeviceTransientDurationSeconds;
        IgnoredDeviceNames = new List<string>(other.IgnoredDeviceNames ?? Enumerable.Empty<string>());
        EnableClipboardWidget = other.EnableClipboardWidget;
        DefaultClipboardPriority = other.DefaultClipboardPriority;
        ClipboardTransientDurationSeconds = other.ClipboardTransientDurationSeconds;
        ShowClipboardPreview = other.ShowClipboardPreview;
        ClipboardHistoryCapacity = other.ClipboardHistoryCapacity;
        ClipboardExpirationMinutes = other.ClipboardExpirationMinutes;
        EnableMicrophoneIndicator = other.EnableMicrophoneIndicator;
        EnableCameraIndicator = other.EnableCameraIndicator;
        EnablePrivacyAlerts = other.EnablePrivacyAlerts;
        DefaultPrivacyPriority = other.DefaultPrivacyPriority;
        PrivacyTransientDurationSeconds = other.PrivacyTransientDurationSeconds;
        IgnoredPrivacyApps = new List<string>(other.IgnoredPrivacyApps ?? Enumerable.Empty<string>());
        EnableAmbientClock = other.EnableAmbientClock;
        ClockTimeFormat = other.ClockTimeFormat;
        ClockShowSeconds = other.ClockShowSeconds;
        ClockShowDate = other.ClockShowDate;
        ClockShowWeekNumber = other.ClockShowWeekNumber;
        DefaultAmbientClockPriority = other.DefaultAmbientClockPriority;
        EnableEnergySaverAlerts = other.EnableEnergySaverAlerts;
        DefaultEnergySaverPriority = other.DefaultEnergySaverPriority;
        EnergySaverTransientDurationSeconds = other.EnergySaverTransientDurationSeconds;
        EnableEnergySaverEfficientMode = other.EnableEnergySaverEfficientMode;
        EnergySaverReduceAnimations = other.EnergySaverReduceAnimations;
        EnergySaverCapAudioVisualizer = other.EnergySaverCapAudioVisualizer;
        EnergySaverThrottleHardwareSampling = other.EnergySaverThrottleHardwareSampling;
        EnergySaverHardwareSamplingIntervalSeconds = other.EnergySaverHardwareSamplingIntervalSeconds;
        EnableScreenshotWidget = other.EnableScreenshotWidget;
        ShowScreenshotThumbnail = other.ShowScreenshotThumbnail;
        DefaultScreenshotPriority = other.DefaultScreenshotPriority;
        ScreenshotTransientDurationSeconds = other.ScreenshotTransientDurationSeconds;
        ScreenshotHistoryCapacity = other.ScreenshotHistoryCapacity;
        ScreenshotHistoryRetentionMinutes = other.ScreenshotHistoryRetentionMinutes;
        EnableScreenshotTrashAction = other.EnableScreenshotTrashAction;
        AdditionalScreenshotFolder = other.AdditionalScreenshotFolder ?? string.Empty;
    }
}

/// <summary>
/// Lenient JSON enum converter that falls back to the domain default when an unrecognized string or integer is encountered.
/// </summary>
public sealed class LenientEnumConverter<TEnum> : System.Text.Json.Serialization.JsonConverter<TEnum>
    where TEnum : struct, Enum
{
    public override TEnum Read(ref System.Text.Json.Utf8JsonReader reader, Type typeToConvert, System.Text.Json.JsonSerializerOptions options)
    {
        if (reader.TokenType == System.Text.Json.JsonTokenType.String)
        {
            string? text = reader.GetString();
            if (!string.IsNullOrWhiteSpace(text) &&
                Enum.TryParse<TEnum>(text, ignoreCase: true, out var parsed) &&
                Enum.IsDefined(typeof(TEnum), parsed))
            {
                return parsed;
            }
        }
        else if (reader.TokenType == System.Text.Json.JsonTokenType.Number && reader.TryGetInt32(out int num))
        {
            var value = (TEnum)Enum.ToObject(typeof(TEnum), num);
            if (Enum.IsDefined(typeof(TEnum), value))
            {
                return value;
            }
        }
        else
        {
            reader.Skip();
        }

        if (typeof(TEnum) == typeof(AudioVisualizerMode))
        {
            return (TEnum)(object)AudioVisualizerMode.Real;
        }

        return default;
    }

    public override void Write(System.Text.Json.Utf8JsonWriter writer, TEnum value, System.Text.Json.JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}
