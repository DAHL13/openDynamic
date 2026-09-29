namespace OpenDynamic.Core.Settings;

/// <summary>
/// Application settings for openDynamic.
/// Holds configuration values for multimedia, volume, battery monitoring, hardware monitoring, timer, and fullscreen behavior.
/// </summary>
public sealed class AppSettings
{
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
    /// Default priority value for volume transient activities.
    /// Default is 80.
    /// </summary>
    public int DefaultVolumePriority { get; set; } = 80;

    /// <summary>
    /// Lifespan in seconds for volume transient notices.
    /// Default is 2.0 seconds.
    /// </summary>
    public double VolumeTransientDurationSeconds { get; set; } = 2.0;

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

    /// <summary>
    /// Indicates whether the island should automatically hide when an application enters fullscreen mode.
    /// Default is true.
    /// </summary>
    public bool HideOnFullscreen { get; set; } = true;

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

    /// <summary>
    /// Flag to enable hardware monitoring widget.
    /// Default is false.
    /// </summary>
    public bool EnableHardwareMonitoring { get; set; } = false;

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
}
