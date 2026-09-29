namespace OpenDynamic.Core.Power;

/// <summary>
/// Immutable snapshot representing the system's power and battery state.
/// Pure domain model adhering to Golden Rule 5.
/// </summary>
/// <param name="Percent">Current battery life percentage [0, 100].</param>
/// <param name="IsCharging">True if AC line is connected and supplying power.</param>
/// <param name="HasBattery">True if the machine has a battery (false on desktop PCs).</param>
public sealed record BatterySnapshot(
    int Percent,
    bool IsCharging,
    bool HasBattery)
{
    /// <summary>
    /// Fallback snapshot indicating AC power on desktop systems with no battery.
    /// </summary>
    public static readonly BatterySnapshot DesktopAcOnline = new(
        Percent: 100,
        IsCharging: true,
        HasBattery: false);

    /// <summary>
    /// Indicates whether the battery level is critically depleted (<= 10%).
    /// </summary>
    public bool IsCritical => HasBattery && !IsCharging && Percent <= 10;

    /// <summary>
    /// Indicates whether the battery level is low (<= 20%).
    /// </summary>
    public bool IsLow => HasBattery && !IsCharging && Percent <= 20;
}
