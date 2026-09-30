using OpenDynamic.Core.Timer;
using Xunit;

namespace OpenDynamic.Tests.Timer;

public sealed class TimerCollectionTests
{
    [Fact]
    public void Constructor_InitializesWithSingleDefaultTimer()
    {
        var clock = new FakeTimeProvider();
        using var collection = new TimerCollection(clock);

        Assert.Single(collection.Timers);
        Assert.Equal("primary", collection.PrimaryTimer.Id);
        Assert.Equal("Temporizador", collection.PrimaryTimer.Label);
        Assert.Equal(TimerState.Stopped, collection.PrimaryTimer.State);
    }

    [Fact]
    public void AddTimer_EnforcesMaximumLimitOfFive()
    {
        var clock = new FakeTimeProvider();
        using var collection = new TimerCollection(clock);

        // 1 initial timer already exists. Add 4 more = 5 total.
        for (int i = 1; i <= 4; i++)
        {
            var timer = collection.AddTimer($"Timer {i}", TimeSpan.FromMinutes(i));
            Assert.NotNull(timer);
        }

        Assert.Equal(5, collection.Timers.Count);

        // Attempting to add a 6th timer must throw
        Assert.Throws<InvalidOperationException>(() =>
            collection.AddTimer("Timer 6", TimeSpan.FromMinutes(6)));

        // TryAddTimer must return false
        bool added = collection.TryAddTimer("Timer 6", TimeSpan.FromMinutes(6), out var overflowTimer);
        Assert.False(added);
        Assert.Null(overflowTimer);
        Assert.Equal(5, collection.Timers.Count);
    }

    [Fact]
    public void PrimaryTimer_SelectsTheOneEndingEarliestAmongRunningTimers()
    {
        var startTime = new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);
        var clock = new FakeTimeProvider(startTime);
        using var collection = new TimerCollection(clock);

        // Default timer (Timer A) -> 10 minutes
        var timerA = collection.PrimaryTimer;
        timerA.Label = "Largo (10m)";
        timerA.Start(TimeSpan.FromMinutes(10));

        // Add Timer B -> 5 minutes
        var timerB = collection.AddTimer("Medio (5m)", TimeSpan.FromMinutes(5));
        timerB.Start();

        // Add Timer C -> 2 minutes
        var timerC = collection.AddTimer("Corto (2m)", TimeSpan.FromMinutes(2));
        timerC.Start();

        // Among running timers, Timer C (2m) ends earliest -> Primary must be Timer C
        Assert.Equal(timerC.Id, collection.PrimaryTimer.Id);
        Assert.Equal("Corto (2m)", collection.PrimaryTimer.Label);

        // Advance 2 minutes: Timer C completes
        clock.Advance(TimeSpan.FromMinutes(2));
        collection.UpdateTick();

        Assert.Equal(TimerState.Completed, timerC.State);

        // Now among remaining running timers (A: 8m left, B: 3m left), Timer B ends earliest
        Assert.Equal(timerB.Id, collection.PrimaryTimer.Id);
        Assert.Equal("Medio (5m)", collection.PrimaryTimer.Label);

        // Advance 3 minutes: Timer B completes
        clock.Advance(TimeSpan.FromMinutes(3));
        collection.UpdateTick();

        Assert.Equal(TimerState.Completed, timerB.State);

        // Now Timer A is the only running timer (5m left)
        Assert.Equal(timerA.Id, collection.PrimaryTimer.Id);
        Assert.Equal("Largo (10m)", collection.PrimaryTimer.Label);
    }

    [Fact]
    public void CompletedTimers_EnqueueAlertsAndServeInSequence()
    {
        var startTime = new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);
        var clock = new FakeTimeProvider(startTime);
        using var collection = new TimerCollection(clock);

        var timer1 = collection.PrimaryTimer;
        timer1.Label = "Huevos";
        timer1.Start(TimeSpan.FromMinutes(3));

        var timer2 = collection.AddTimer("Té", TimeSpan.FromMinutes(3));
        timer2.Start();

        var alertsReceived = new List<TimerAlert>();
        collection.AlertTriggered += (s, alert) => alertsReceived.Add(alert);

        // Both timers expire at the exact same moment (+3 minutes)
        clock.Advance(TimeSpan.FromMinutes(3));
        collection.UpdateTick();

        // Exactly one alert should be active immediately, and one pending
        Assert.NotNull(collection.ActiveAlert);
        Assert.Single(collection.PendingAlerts);
        Assert.Single(alertsReceived);
        Assert.Equal("Huevos", collection.ActiveAlert.Label);

        // User or UI completes 5 seconds alert duration for first timer -> Dismiss and get next
        var nextAlert = collection.DismissActiveAlertAndGetNext();

        Assert.NotNull(nextAlert);
        Assert.Equal(nextAlert, collection.ActiveAlert);
        Assert.Equal("Té", nextAlert.Label);
        Assert.Empty(collection.PendingAlerts);
        Assert.Equal(2, alertsReceived.Count);

        // Dismiss second alert -> Queue is now fully cleared
        var finalAlert = collection.DismissActiveAlertAndGetNext();
        Assert.Null(finalAlert);
        Assert.Null(collection.ActiveAlert);
        Assert.Empty(collection.PendingAlerts);
    }

    [Fact]
    public void RemoveTimer_RemovesSuccessfully_AndMaintainsNonEmptyCollection()
    {
        var clock = new FakeTimeProvider();
        using var collection = new TimerCollection(clock);

        var customTimer = collection.AddTimer("Pasta", TimeSpan.FromMinutes(8));
        Assert.Equal(2, collection.Timers.Count);

        bool removed = collection.RemoveTimer(customTimer.Id);
        Assert.True(removed);
        Assert.Single(collection.Timers);
        Assert.Null(collection.GetTimer(customTimer.Id));

        // Removing the last remaining timer recreates a default timer
        var defaultId = collection.Timers[0].Id;
        collection.RemoveTimer(defaultId);

        Assert.Single(collection.Timers);
        Assert.Equal("primary", collection.Timers[0].Id);
    }

    [Fact]
    public void BackgroundTimer_TriggersCompletionWithoutManualUpdateTick()
    {
        var startTime = new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);
        var clock = new FakeTimeProvider(startTime);
        using var collection = new TimerCollection(clock);

        var timer = collection.PrimaryTimer;
        timer.Start(TimeSpan.FromMinutes(5));

        bool alertFired = false;
        collection.AlertTriggered += (s, e) => alertFired = true;

        // Advance virtual clock: FakeTimeProvider triggers registered background ITimer
        clock.Advance(TimeSpan.FromMinutes(5));

        Assert.True(alertFired);
        Assert.Equal(TimerState.Completed, timer.State);
    }

    [Fact]
    public void Pomodoro_FirstTimerMaintainsFullBackwardCompatibility()
    {
        var startTime = new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);
        var clock = new FakeTimeProvider(startTime);
        using var collection = new TimerCollection(clock);

        var timer = collection.PrimaryTimer;
        timer.SetMode(TimerMode.PomodoroWork, TimeSpan.FromMinutes(25));
        timer.Start();

        Assert.Equal(TimerMode.PomodoroWork, timer.Mode);
        Assert.Equal(TimeSpan.FromMinutes(25), timer.TotalDuration);
        Assert.Equal("25:00", timer.CurrentSnapshot.FormattedTime);

        clock.Advance(TimeSpan.FromMinutes(25));
        var snapshot = timer.UpdateTick();

        Assert.Equal(TimerState.Completed, snapshot.State);
    }
}
