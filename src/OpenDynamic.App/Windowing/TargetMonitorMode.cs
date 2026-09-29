namespace OpenDynamic.App.Windowing;

/// <summary>
/// Specifies how the target display monitor is selected for the island window.
/// </summary>
public enum TargetMonitorMode
{
    /// <summary>
    /// Always place the island on the primary monitor.
    /// </summary>
    Primary = 0,

    /// <summary>
    /// Place the island on the monitor currently containing the user's cursor.
    /// </summary>
    Cursor = 1
}
