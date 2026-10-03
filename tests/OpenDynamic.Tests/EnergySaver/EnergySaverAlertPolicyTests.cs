using OpenDynamic.Core.EnergySaver;
using OpenDynamic.Tests.Timer;
using Xunit;

namespace OpenDynamic.Tests.EnergySaver;

public class EnergySaverAlertPolicyTests
{
    [Fact]
    public void Startup_DoesNotEmitAlert_WhenInitializedWithOff()
    {
        var clock = new FakeTimeProvider();
        var policy = new EnergySaverAlertPolicy(clock);

        var alerts = new List<EnergySaverState>();
        policy.AlertTriggered += (s, state) => alerts.Add(state);

        policy.Initialize(EnergySaverState.Off);

        Assert.True(policy.IsInitialized);
        Assert.Equal(EnergySaverState.Off, policy.CurrentState);
        Assert.Empty(alerts);
    }

    [Fact]
    public void Startup_DoesNotEmitAlert_WhenInitializedWithOn()
    {
        var clock = new FakeTimeProvider();
        var policy = new EnergySaverAlertPolicy(clock);

        var alerts = new List<EnergySaverState>();
        policy.AlertTriggered += (s, state) => alerts.Add(state);

        policy.Initialize(EnergySaverState.On);

        Assert.True(policy.IsInitialized);
        Assert.Equal(EnergySaverState.On, policy.CurrentState);
        Assert.Empty(alerts);
    }

    [Fact]
    public void Startup_FirstProcessState_InitializesSilentlyWithoutAlert()
    {
        var clock = new FakeTimeProvider();
        var policy = new EnergySaverAlertPolicy(clock);

        var alerts = new List<EnergySaverState>();
        policy.AlertTriggered += (s, state) => alerts.Add(state);

        policy.ProcessState(EnergySaverState.On);

        Assert.True(policy.IsInitialized);
        Assert.Equal(EnergySaverState.On, policy.CurrentState);
        Assert.Empty(alerts);
    }

    [Fact]
    public void Transition_OffToOn_EmitsAlert()
    {
        var clock = new FakeTimeProvider();
        var policy = new EnergySaverAlertPolicy(clock);

        var alerts = new List<EnergySaverState>();
        policy.AlertTriggered += (s, state) => alerts.Add(state);

        policy.Initialize(EnergySaverState.Off);
        clock.Advance(TimeSpan.FromSeconds(1));

        policy.ProcessState(EnergySaverState.On);

        Assert.Single(alerts);
        Assert.Equal(EnergySaverState.On, alerts[0]);
        Assert.Equal(EnergySaverState.On, policy.CurrentState);
    }

    [Fact]
    public void Transition_OnToOff_EmitsAlert()
    {
        var clock = new FakeTimeProvider();
        var policy = new EnergySaverAlertPolicy(clock);

        var alerts = new List<EnergySaverState>();
        policy.AlertTriggered += (s, state) => alerts.Add(state);

        policy.Initialize(EnergySaverState.On);
        clock.Advance(TimeSpan.FromSeconds(1));

        policy.ProcessState(EnergySaverState.Off);

        Assert.Single(alerts);
        Assert.Equal(EnergySaverState.Off, alerts[0]);
        Assert.Equal(EnergySaverState.Off, policy.CurrentState);
    }

    [Fact]
    public void SameState_DoesNotEmitDuplicateAlert()
    {
        var clock = new FakeTimeProvider();
        var policy = new EnergySaverAlertPolicy(clock);

        var alerts = new List<EnergySaverState>();
        policy.AlertTriggered += (s, state) => alerts.Add(state);

        policy.Initialize(EnergySaverState.Off);
        clock.Advance(TimeSpan.FromSeconds(1));

        policy.ProcessState(EnergySaverState.On);
        Assert.Single(alerts);

        clock.Advance(TimeSpan.FromSeconds(10));
        policy.ProcessState(EnergySaverState.On);

        Assert.Single(alerts);
    }

    [Theory]
    [InlineData(EnergySaverState.Unknown)]
    [InlineData(EnergySaverState.NotSupported)]
    public void UnsupportedOrUnknown_NeverEmitsAlerts(EnergySaverState nonAlertingState)
    {
        var clock = new FakeTimeProvider();
        var policy = new EnergySaverAlertPolicy(clock);

        var alerts = new List<EnergySaverState>();
        policy.AlertTriggered += (s, state) => alerts.Add(state);

        policy.Initialize(EnergySaverState.Off);
        clock.Advance(TimeSpan.FromSeconds(1));

        policy.ProcessState(nonAlertingState);

        Assert.Empty(alerts);
        Assert.Equal(nonAlertingState, policy.CurrentState);
    }

    [Fact]
    public void SuspendResume_SuppressesAlertsForTenSeconds()
    {
        var clock = new FakeTimeProvider();
        var policy = new EnergySaverAlertPolicy(clock, suspendSuppressionDuration: TimeSpan.FromSeconds(10.0));

        var alerts = new List<EnergySaverState>();
        policy.AlertTriggered += (s, state) => alerts.Add(state);

        policy.Initialize(EnergySaverState.Off);
        clock.Advance(TimeSpan.FromSeconds(1));

        policy.NotifySuspended();
        policy.NotifyResumedFromSuspend();

        Assert.True(policy.IsInSuspensionSuppression);

        // System changes state 3 seconds after wake -> MUST BE SUPPRESSED
        clock.Advance(TimeSpan.FromSeconds(3));
        policy.ProcessState(EnergySaverState.On);
        Assert.Empty(alerts);

        // Advance past 10 seconds suppression window
        clock.Advance(TimeSpan.FromSeconds(8)); // Total 11s elapsed
        Assert.False(policy.IsInSuspensionSuppression);

        // Transition after suppression window -> Alert is emitted
        policy.ProcessState(EnergySaverState.Off);
        Assert.Single(alerts);
        Assert.Equal(EnergySaverState.Off, alerts[0]);
    }

    [Fact]
    public void Cooldown_SuppressesRapidTransitionsWithinFiveSeconds()
    {
        var clock = new FakeTimeProvider();
        var policy = new EnergySaverAlertPolicy(clock, cooldownDuration: TimeSpan.FromSeconds(5.0));

        var alerts = new List<EnergySaverState>();
        policy.AlertTriggered += (s, state) => alerts.Add(state);

        policy.Initialize(EnergySaverState.Off);
        clock.Advance(TimeSpan.FromSeconds(1));

        // First transition -> Emits
        policy.ProcessState(EnergySaverState.On);
        Assert.Single(alerts);

        // Second transition 2 seconds later (within 5s cooldown) -> Suppressed
        clock.Advance(TimeSpan.FromSeconds(2));
        policy.ProcessState(EnergySaverState.Off);
        Assert.Single(alerts);

        // Advance past 5s cooldown (3.5s more -> 5.5s after first alert)
        clock.Advance(TimeSpan.FromSeconds(3.5));
        policy.ProcessState(EnergySaverState.Off);
        Assert.Equal(2, alerts.Count);
        Assert.Equal(EnergySaverState.Off, alerts[1]);
    }
}
