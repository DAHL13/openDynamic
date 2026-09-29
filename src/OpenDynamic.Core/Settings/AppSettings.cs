namespace OpenDynamic.Core.Settings;

/// <summary>
/// Application settings for openDynamic.
/// Holds configuration values such as media pause grace periods and priority defaults.
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
}
