namespace OpenDynamic.Core.Clock;

/// <summary>
/// Specifies the time display formatting preference.
/// </summary>
public enum ClockTimeFormat
{
    /// <summary>
    /// Follows the system culture's short time pattern (12-hour or 24-hour).
    /// </summary>
    Auto = 0,

    /// <summary>
    /// Forces 12-hour display with AM/PM indicator.
    /// </summary>
    TwelveHour = 1,

    /// <summary>
    /// Forces 24-hour display without AM/PM indicator.
    /// </summary>
    TwentyFourHour = 2
}
