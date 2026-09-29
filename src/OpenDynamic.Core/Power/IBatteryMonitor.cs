namespace OpenDynamic.Core.Power;

/// <summary>
/// Domain contract for observing system power and battery status.
/// Zero-polling architecture (Golden Rule 1 & Golden Rule 5).
/// </summary>
public interface IBatteryMonitor
{
    /// <summary>
    /// Current snapshot of battery and charger status.
    /// </summary>
    BatterySnapshot CurrentStatus { get; }

    /// <summary>
    /// Occurs when a threshold crossing or charger transition triggers an alert.
    /// </summary>
    event EventHandler<BatteryAlertEventArgs>? AlertTriggered;

    /// <summary>
    /// Occurs whenever power status changes.
    /// </summary>
    event EventHandler<BatterySnapshot>? StatusChanged;
}
