using OpenDynamic.Core.Power;
using Xunit;

namespace OpenDynamic.Tests.Power;

public class BatteryThresholdTrackerTests
{
    [Fact]
    public void DesktopPc_WithNoBattery_NeverEmitsAlerts()
    {
        var tracker = new BatteryThresholdTracker(lowThresholdPercent: 20, criticalThresholdPercent: 10);
        var snapshot = BatterySnapshot.DesktopAcOnline; // HasBattery = false

        var alert1 = tracker.Evaluate(snapshot);
        var alert2 = tracker.Evaluate(snapshot with { Percent = 15 });

        Assert.Equal(BatteryAlertKind.None, alert1);
        Assert.Equal(BatteryAlertKind.None, alert2);
    }

    [Fact]
    public void InitialEvaluation_DoesNotSpamAlert()
    {
        var tracker = new BatteryThresholdTracker(lowThresholdPercent: 20, criticalThresholdPercent: 10);
        var initial = new BatterySnapshot(Percent: 18, IsCharging: false, HasBattery: true);

        var alert = tracker.Evaluate(initial);

        // First evaluation records baseline without firing spurious notice
        Assert.Equal(BatteryAlertKind.None, alert);
        Assert.True(tracker.HasEmittedLowAlert);
    }

    [Fact]
    public void ChargerConnectedAndDisconnected_EmitCorrectAlerts()
    {
        var tracker = new BatteryThresholdTracker();
        var baseline = new BatterySnapshot(Percent: 50, IsCharging: false, HasBattery: true);
        tracker.Evaluate(baseline);

        // Plug in AC
        var pluggedIn = tracker.Evaluate(new BatterySnapshot(Percent: 50, IsCharging: true, HasBattery: true));
        Assert.Equal(BatteryAlertKind.ChargerConnected, pluggedIn);

        // Unplug AC
        var unplugged = tracker.Evaluate(new BatterySnapshot(Percent: 50, IsCharging: false, HasBattery: true));
        Assert.Equal(BatteryAlertKind.ChargerDisconnected, unplugged);
    }

    [Fact]
    public void LowBatteryThreshold_EmitsExactlyOnce_NoRepeatsOnFluctuation()
    {
        var tracker = new BatteryThresholdTracker(lowThresholdPercent: 20, criticalThresholdPercent: 10, hysteresisPercent: 2);
        
        // Start discharging at 25%
        tracker.Evaluate(new BatterySnapshot(Percent: 25, IsCharging: false, HasBattery: true));

        // Drops to 20%: crossing threshold triggers LowBattery alert
        var alert20 = tracker.Evaluate(new BatterySnapshot(Percent: 20, IsCharging: false, HasBattery: true));
        Assert.Equal(BatteryAlertKind.LowBattery, alert20);
        Assert.True(tracker.HasEmittedLowAlert);

        // Drops to 19%: already emitted, must NOT repeat
        var alert19 = tracker.Evaluate(new BatterySnapshot(Percent: 19, IsCharging: false, HasBattery: true));
        Assert.Equal(BatteryAlertKind.None, alert19);

        // Voltage fluctuates up to 20% then down to 19%: still within hysteresis (threshold + 2 = 22%), no re-alert
        var alertFluctuateUp = tracker.Evaluate(new BatterySnapshot(Percent: 20, IsCharging: false, HasBattery: true));
        Assert.Equal(BatteryAlertKind.None, alertFluctuateUp);

        var alertFluctuateDown = tracker.Evaluate(new BatterySnapshot(Percent: 19, IsCharging: false, HasBattery: true));
        Assert.Equal(BatteryAlertKind.None, alertFluctuateDown);
    }

    [Fact]
    public void CriticalBatteryThreshold_EmitsExactlyOnce_NoRepeats()
    {
        var tracker = new BatteryThresholdTracker(lowThresholdPercent: 20, criticalThresholdPercent: 10);
        
        // Discharging at 15% (already in low battery range)
        tracker.Evaluate(new BatterySnapshot(Percent: 15, IsCharging: false, HasBattery: true));

        // Crosses critical threshold (10%)
        var alert10 = tracker.Evaluate(new BatterySnapshot(Percent: 10, IsCharging: false, HasBattery: true));
        Assert.Equal(BatteryAlertKind.CriticalBattery, alert10);
        Assert.True(tracker.HasEmittedCriticalAlert);

        // Drops to 9%: must not repeat
        var alert9 = tracker.Evaluate(new BatterySnapshot(Percent: 9, IsCharging: false, HasBattery: true));
        Assert.Equal(BatteryAlertKind.None, alert9);

        // Drops to 8%: must not repeat
        var alert8 = tracker.Evaluate(new BatterySnapshot(Percent: 8, IsCharging: false, HasBattery: true));
        Assert.Equal(BatteryAlertKind.None, alert8);
    }

    [Fact]
    public void PluggingInCharger_ResetsAlertsForNextDischargeCycle()
    {
        var tracker = new BatteryThresholdTracker(lowThresholdPercent: 20, criticalThresholdPercent: 10);
        
        // Discharging through 20%
        tracker.Evaluate(new BatterySnapshot(Percent: 25, IsCharging: false, HasBattery: true));
        var lowAlert = tracker.Evaluate(new BatterySnapshot(Percent: 20, IsCharging: false, HasBattery: true));
        Assert.Equal(BatteryAlertKind.LowBattery, lowAlert);

        // Charger connected: alerts reset
        var chargerAlert = tracker.Evaluate(new BatterySnapshot(Percent: 20, IsCharging: true, HasBattery: true));
        Assert.Equal(BatteryAlertKind.ChargerConnected, chargerAlert);
        Assert.False(tracker.HasEmittedLowAlert);

        // Charges up to 30%
        tracker.Evaluate(new BatterySnapshot(Percent: 30, IsCharging: true, HasBattery: true));

        // Unplugged at 30%
        var unplugged = tracker.Evaluate(new BatterySnapshot(Percent: 30, IsCharging: false, HasBattery: true));
        Assert.Equal(BatteryAlertKind.ChargerDisconnected, unplugged);

        // Drops back to 20%: fires low battery again for the new cycle!
        var secondLowAlert = tracker.Evaluate(new BatterySnapshot(Percent: 20, IsCharging: false, HasBattery: true));
        Assert.Equal(BatteryAlertKind.LowBattery, secondLowAlert);
    }
}
