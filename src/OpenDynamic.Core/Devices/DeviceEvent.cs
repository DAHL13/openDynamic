namespace OpenDynamic.Core.Devices;

/// <summary>
/// Domain model representing a device connection or disconnection notification.
/// Adheres strictly to Golden Rule 5 (isolated in Core, zero UI or platform dependencies).
/// </summary>
public sealed record DeviceEvent(
    DeviceEventType Type,
    string DeviceId,
    string DeviceName,
    DeviceCategory Category,
    int? BatteryPercent = null);
