namespace OpenDynamic.Core.Power;

/// <summary>
/// Event arguments for power and battery alerts.
/// </summary>
public sealed class BatteryAlertEventArgs : EventArgs
{
    public BatteryAlertKind AlertKind { get; }
    public BatterySnapshot Snapshot { get; }

    public BatteryAlertEventArgs(BatteryAlertKind alertKind, BatterySnapshot snapshot)
    {
        AlertKind = alertKind;
        Snapshot = snapshot;
    }
}
