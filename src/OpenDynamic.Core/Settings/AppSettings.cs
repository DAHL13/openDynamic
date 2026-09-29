namespace OpenDynamic.Core.Settings;

/// <summary>
/// Application settings for openDynamic.
/// Holds configuration values for multimedia, volume, battery monitoring, and fullscreen behavior.
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
}
