using OpenDynamic.Core.Network;
using OpenDynamic.Tests.Timer;
using Xunit;

namespace OpenDynamic.Tests.Network;

public sealed class NetworkAlertPolicyTests
{
    [Fact]
    public void Startup_SuppressesAlert_OnFirstSnapshot()
    {
        var clock = new FakeTimeProvider();
        using var policy = new NetworkAlertPolicy(clock);

        var alerts = new List<NetworkSnapshot>();
        policy.AlertTriggered += (_, snap) => alerts.Add(snap);

        var initial = new NetworkSnapshot(NetworkState.Connected, "Home_WiFi", NetworkType.WiFi);
        policy.ProcessSnapshot(initial);

        // Advance beyond debounce window
        clock.Advance(TimeSpan.FromSeconds(2));

        Assert.True(policy.IsInitialized);
        Assert.Equal(initial, policy.CurrentSnapshot);
        Assert.Empty(alerts);
    }

    [Fact]
    public void Debounce_CoalescesBurstEvents_IntoSingleAlert()
    {
        var clock = new FakeTimeProvider();
        using var policy = new NetworkAlertPolicy(clock);

        var alerts = new List<NetworkSnapshot>();
        policy.AlertTriggered += (_, snap) => alerts.Add(snap);

        // Initialize with baseline
        policy.ProcessSnapshot(new NetworkSnapshot(NetworkState.Connected, "Network_A", NetworkType.WiFi));
        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Empty(alerts);

        // Rapid burst: Disconnect, then connect to Network_B within 500ms
        policy.ProcessSnapshot(new NetworkSnapshot(NetworkState.Disconnected, null, NetworkType.Other));
        clock.Advance(TimeSpan.FromMilliseconds(400));
        Assert.Empty(alerts);

        var finalSnap = new NetworkSnapshot(NetworkState.Connected, "Network_B", NetworkType.WiFi);
        policy.ProcessSnapshot(finalSnap);

        // Advance 0.5s (still in debounce from second event)
        clock.Advance(TimeSpan.FromMilliseconds(500));
        Assert.Empty(alerts);

        // Complete debounce window (1s from second event)
        clock.Advance(TimeSpan.FromMilliseconds(600));

        Assert.Single(alerts);
        Assert.Equal(finalSnap, alerts[0]);
    }

    [Fact]
    public void Debounce_CancelsIfStateRestoresToLastEmitted()
    {
        var clock = new FakeTimeProvider();
        using var policy = new NetworkAlertPolicy(clock);

        var alerts = new List<NetworkSnapshot>();
        policy.AlertTriggered += (_, snap) => alerts.Add(snap);

        var baseline = new NetworkSnapshot(NetworkState.Connected, "Fiber_LAN", NetworkType.Ethernet);
        policy.ProcessSnapshot(baseline);
        clock.Advance(TimeSpan.FromSeconds(2));

        // Transient disconnection
        policy.ProcessSnapshot(new NetworkSnapshot(NetworkState.Disconnected, null, NetworkType.Ethernet));
        clock.Advance(TimeSpan.FromMilliseconds(400));

        // Reconnects to same network before 1s debounce expires
        policy.ProcessSnapshot(baseline);

        // Advance well past debounce
        clock.Advance(TimeSpan.FromSeconds(2));

        Assert.Empty(alerts);
    }

    [Fact]
    public void Cooldown_SuppressesIdenticalAlerts_WithinFiveSeconds()
    {
        var clock = new FakeTimeProvider();
        using var policy = new NetworkAlertPolicy(clock, cooldownDuration: TimeSpan.FromSeconds(5.0));

        var alerts = new List<NetworkSnapshot>();
        policy.AlertTriggered += (_, snap) => alerts.Add(snap);

        // Baseline: Connected to Home_WiFi at t=0
        policy.ProcessSnapshot(new NetworkSnapshot(NetworkState.Connected, "Home_WiFi", NetworkType.WiFi));
        clock.Advance(TimeSpan.FromSeconds(1)); // t = 1.0s

        // First disconnect -> alerts after 1s debounce (at t = 2.0s)
        policy.ProcessSnapshot(new NetworkSnapshot(NetworkState.Disconnected, null, NetworkType.Other));
        clock.Advance(TimeSpan.FromSeconds(1.5)); // t = 2.5s
        Assert.Single(alerts);

        // Connect to a new network (Office_WiFi) -> alerts at t = 4.0s (debounce 1s from t=3.0s)
        clock.Advance(TimeSpan.FromSeconds(0.5)); // t = 3.0s
        var connectedOffice = new NetworkSnapshot(NetworkState.Connected, "Office_WiFi", NetworkType.WiFi);
        policy.ProcessSnapshot(connectedOffice);
        clock.Advance(TimeSpan.FromSeconds(1.5)); // t = 4.5s
        Assert.Equal(2, alerts.Count);

        // Rapid disconnect again at t=4.5s (debounce fires at t=5.5s)
        // From first disconnect (t=2.0s) to this disconnect (t=5.5s) is 3.5s < 5s cooldown
        policy.ProcessSnapshot(new NetworkSnapshot(NetworkState.Disconnected, null, NetworkType.Other));
        clock.Advance(TimeSpan.FromSeconds(1.5)); // t = 6.0s

        // Must be suppressed because Disconnected alert was already emitted at t=2.0s (< 5.0s ago)
        Assert.Equal(2, alerts.Count);

        // Advance beyond 5s from the first disconnect alert (t=2.0s + 5.0s = 7.0s)
        clock.Advance(TimeSpan.FromSeconds(2.0)); // t = 8.0s

        // Third disconnect attempt at t=8.0s (debounce fires at t=9.0s, which is 7.0s after t=2.0s)
        policy.ProcessSnapshot(new NetworkSnapshot(NetworkState.Disconnected, null, NetworkType.Other));
        clock.Advance(TimeSpan.FromSeconds(1.5)); // t = 9.5s

        // Now cooldown has elapsed: alert must be emitted
        Assert.Equal(3, alerts.Count);
        Assert.Equal(NetworkState.Disconnected, alerts[2].State);
    }

    [Fact]
    public void SuspendResume_SuppressesAlerts_ForTenSeconds()
    {
        var clock = new FakeTimeProvider();
        using var policy = new NetworkAlertPolicy(clock, suspendSuppressionDuration: TimeSpan.FromSeconds(10.0));

        var alerts = new List<NetworkSnapshot>();
        policy.AlertTriggered += (_, snap) => alerts.Add(snap);

        // Baseline
        policy.ProcessSnapshot(new NetworkSnapshot(NetworkState.Connected, "Home_WiFi", NetworkType.WiFi));
        clock.Advance(TimeSpan.FromSeconds(2));

        // Host system resumes from sleep
        policy.NotifyResumedFromSuspend();
        Assert.True(policy.IsInSuspensionSuppression);

        // Network changes during wakeup flurry (t = 2s after resume)
        clock.Advance(TimeSpan.FromSeconds(2));
        policy.ProcessSnapshot(new NetworkSnapshot(NetworkState.Disconnected, null, NetworkType.Other));
        clock.Advance(TimeSpan.FromSeconds(2)); // t = 4s

        policy.ProcessSnapshot(new NetworkSnapshot(NetworkState.Connected, "Office_WiFi", NetworkType.WiFi));
        clock.Advance(TimeSpan.FromSeconds(3)); // t = 7s

        // Still inside 10s suppression window: zero alerts
        Assert.Empty(alerts);

        // Advance to 11s after resume (window expired)
        clock.Advance(TimeSpan.FromSeconds(4)); // t = 11s
        Assert.False(policy.IsInSuspensionSuppression);

        // A new change after suppression window expires DOES trigger an alert
        policy.ProcessSnapshot(new NetworkSnapshot(NetworkState.Disconnected, null, NetworkType.Other));
        clock.Advance(TimeSpan.FromSeconds(1.5));

        Assert.Single(alerts);
        Assert.Equal(NetworkState.Disconnected, alerts[0].State);
    }
}
