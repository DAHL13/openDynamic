namespace OpenDynamic.Core.Devices;

/// <summary>
/// Broad categorization for peripheral devices to determine visual icon and grouping.
/// </summary>
public enum DeviceCategory
{
    /// <summary>
    /// Headphones, headsets, earbuds, or audio speakers.
    /// </summary>
    Audio,

    /// <summary>
    /// Keyboards and numeric keypads.
    /// </summary>
    Keyboard,

    /// <summary>
    /// Mice, trackpads, and pointing devices.
    /// </summary>
    Mouse,

    /// <summary>
    /// USB flash drives, memory cards, and external storage drives.
    /// </summary>
    Storage,

    /// <summary>
    /// Generic or uncategorized peripherals.
    /// </summary>
    Other
}
