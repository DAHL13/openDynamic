namespace OpenDynamic.Core.Power;

/// <summary>
/// Specific category of battery and power alerts.
/// </summary>
public enum BatteryAlertKind
{
    None,
    ChargerConnected,
    ChargerDisconnected,
    LowBattery,
    CriticalBattery
}
