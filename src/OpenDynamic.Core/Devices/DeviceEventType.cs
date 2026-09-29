namespace OpenDynamic.Core.Devices;

/// <summary>
/// Type of device connection state transition.
/// </summary>
public enum DeviceEventType
{
    /// <summary>
    /// Device was connected or attached to the system.
    /// </summary>
    Connected,

    /// <summary>
    /// Device was disconnected or removed from the system.
    /// </summary>
    Disconnected
}
