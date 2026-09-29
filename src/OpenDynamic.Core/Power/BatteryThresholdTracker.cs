namespace OpenDynamic.Core.Power;

/// <summary>
/// Pure stateful domain tracker that evaluates battery level transitions and charger connections.
/// Guarantees that low/critical threshold alerts fire EXACTLY ONCE per crossing without spurious
/// re-notifications caused by battery voltage fluctuations (Golden Rule 1 & Golden Rule 5).
/// </summary>
public sealed class BatteryThresholdTracker
{
    private readonly int _lowThresholdPercent;
    private readonly int _criticalThresholdPercent;
    private readonly int _hysteresisPercent;

    private bool _isInitialized;
    private bool _lastChargingState;
    private bool _hasEmittedLowAlert;
    private bool _hasEmittedCriticalAlert;

    public int LowThresholdPercent => _lowThresholdPercent;
    public int CriticalThresholdPercent => _criticalThresholdPercent;
    public int HysteresisPercent => _hysteresisPercent;

    public bool HasEmittedLowAlert => _hasEmittedLowAlert;
    public bool HasEmittedCriticalAlert => _hasEmittedCriticalAlert;

    public BatteryThresholdTracker(
        int lowThresholdPercent = 20,
        int criticalThresholdPercent = 10,
        int hysteresisPercent = 2)
    {
        if (criticalThresholdPercent >= lowThresholdPercent)
        {
            throw new ArgumentException("Critical threshold must be strictly lower than low threshold.", nameof(criticalThresholdPercent));
        }

        _lowThresholdPercent = lowThresholdPercent;
        _criticalThresholdPercent = criticalThresholdPercent;
        _hysteresisPercent = Math.Max(0, hysteresisPercent);
    }

    /// <summary>
    /// Evaluates the new power snapshot and determines if an alert should fire.
    /// </summary>
    /// <param name="snapshot">The latest battery snapshot.</param>
    /// <returns>The triggered <see cref="BatteryAlertKind"/>, or <see cref="BatteryAlertKind.None"/>.</returns>
    public BatteryAlertKind Evaluate(BatterySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (!snapshot.HasBattery)
        {
            return BatteryAlertKind.None;
        }

        if (!_isInitialized)
        {
            _isInitialized = true;
            _lastChargingState = snapshot.IsCharging;

            if (!snapshot.IsCharging)
            {
                if (snapshot.Percent <= _criticalThresholdPercent)
                {
                    _hasEmittedCriticalAlert = true;
                    _hasEmittedLowAlert = true;
                }
                else if (snapshot.Percent <= _lowThresholdPercent)
                {
                    _hasEmittedLowAlert = true;
                }
            }

            return BatteryAlertKind.None;
        }

        // 1. Detect Charger Connection / Disconnection
        if (snapshot.IsCharging != _lastChargingState)
        {
            _lastChargingState = snapshot.IsCharging;

            if (snapshot.IsCharging)
            {
                // When connected to AC, reset depletion alert flags
                _hasEmittedLowAlert = false;
                _hasEmittedCriticalAlert = false;
                return BatteryAlertKind.ChargerConnected;
            }

            return BatteryAlertKind.ChargerDisconnected;
        }

        // 2. While Discharging: Detect Low and Critical Threshold crossings
        if (!snapshot.IsCharging)
        {
            if (snapshot.Percent <= _criticalThresholdPercent)
            {
                if (!_hasEmittedCriticalAlert)
                {
                    _hasEmittedCriticalAlert = true;
                    _hasEmittedLowAlert = true;
                    return BatteryAlertKind.CriticalBattery;
                }
            }
            else if (snapshot.Percent <= _lowThresholdPercent)
            {
                if (!_hasEmittedLowAlert)
                {
                    _hasEmittedLowAlert = true;
                    return BatteryAlertKind.LowBattery;
                }
            }
            else
            {
                // Hysteresis recovery if battery voltage rebounds without charging
                if (snapshot.Percent >= _lowThresholdPercent + _hysteresisPercent)
                {
                    _hasEmittedLowAlert = false;
                }

                if (snapshot.Percent >= _criticalThresholdPercent + _hysteresisPercent)
                {
                    _hasEmittedCriticalAlert = false;
                }
            }
        }
        else
        {
            // While charging, if level exceeds threshold + hysteresis, ensure flags reset
            if (snapshot.Percent >= _lowThresholdPercent + _hysteresisPercent)
            {
                _hasEmittedLowAlert = false;
            }

            if (snapshot.Percent >= _criticalThresholdPercent + _hysteresisPercent)
            {
                _hasEmittedCriticalAlert = false;
            }
        }

        return BatteryAlertKind.None;
    }

    /// <summary>
    /// Resets all tracker state.
    /// </summary>
    public void Reset()
    {
        _isInitialized = false;
        _lastChargingState = false;
        _hasEmittedLowAlert = false;
        _hasEmittedCriticalAlert = false;
    }
}
