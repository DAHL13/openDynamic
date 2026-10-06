using OpenDynamic.Core.Timer;
using Xunit;

namespace OpenDynamic.Tests.Timer;

public sealed class TimerControllerTests
{
    [Fact]
    public void Start_CalculatesTargetEndTimeUtc_AndSetsRunningState()
    {
        var startTime = new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);
        var clock = new FakeTimeProvider(startTime);
        var controller = new TimerController(clock);

        controller.Start(TimeSpan.FromMinutes(10));

        Assert.Equal(TimerState.Running, controller.State);
        Assert.Equal(TimeSpan.FromMinutes(10), controller.TotalDuration);
        Assert.Equal(TimeSpan.FromMinutes(10), controller.RemainingTime);
        Assert.Equal(startTime.AddMinutes(10), controller.TargetEndTimeUtc);
    }

    [Fact]
    public void UpdateTick_BeforeExpiration_CalculatesRemainingAndProgress()
    {
        var startTime = new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);
        var clock = new FakeTimeProvider(startTime);
        var controller = new TimerController(clock);

        controller.Start(TimeSpan.FromMinutes(10));

        // Advance 4 minutes
        clock.Advance(TimeSpan.FromMinutes(4));
        var snapshot = controller.UpdateTick();

        Assert.Equal(TimerState.Running, snapshot.State);
        Assert.Equal(TimeSpan.FromMinutes(6), snapshot.RemainingTime);
        Assert.Equal(0.4, snapshot.ProgressRatio, precision: 3);
        Assert.Equal(0.6, snapshot.RemainingRatio, precision: 3);
        Assert.Equal("06:00", snapshot.FormattedTime);
    }

    [Fact]
    public void UpdateTick_OnExactExpiration_FiresCompletedEventAndSetsCompletedState()
    {
        var startTime = new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);
        var clock = new FakeTimeProvider(startTime);
        var controller = new TimerController(clock);

        bool completedFired = false;
        TimerSnapshot? completedSnapshot = null;
        controller.Completed += (s, snap) =>
        {
            completedFired = true;
            completedSnapshot = snap;
        };

        controller.Start(TimeSpan.FromMinutes(5));

        // Advance beyond 5 minutes
        clock.Advance(TimeSpan.FromMinutes(5));
        var snapshot = controller.UpdateTick();

        Assert.True(completedFired);
        Assert.NotNull(completedSnapshot);
        Assert.Equal(TimerState.Completed, snapshot.State);
        Assert.Equal(TimeSpan.Zero, snapshot.RemainingTime);
        Assert.Equal(1.0, snapshot.ProgressRatio);
        Assert.Equal("00:00", snapshot.FormattedTime);
        Assert.Null(controller.TargetEndTimeUtc);
    }

    [Fact]
    public void Pause_PreservesRemainingTime_WithoutDrift()
    {
        var startTime = new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);
        var clock = new FakeTimeProvider(startTime);
        var controller = new TimerController(clock);

        controller.Start(TimeSpan.FromMinutes(10));

        // Advance 3 minutes (7 min remaining)
        clock.Advance(TimeSpan.FromMinutes(3));
        controller.UpdateTick();
        Assert.Equal(TimeSpan.FromMinutes(7), controller.RemainingTime);

        // Pause timer
        controller.Pause();
        Assert.Equal(TimerState.Paused, controller.State);
        Assert.Null(controller.TargetEndTimeUtc);
        Assert.Equal(TimeSpan.FromMinutes(7), controller.RemainingTime);

        // Advance 2 hours in virtual time while paused
        clock.Advance(TimeSpan.FromHours(2));
        var tickWhilePaused = controller.UpdateTick();

        // Remaining time must remain completely preserved with zero drift
        Assert.Equal(TimerState.Paused, tickWhilePaused.State);
        Assert.Equal(TimeSpan.FromMinutes(7), tickWhilePaused.RemainingTime);
        Assert.Equal("07:00", tickWhilePaused.FormattedTime);
    }

    [Fact]
    public void Resume_RecalculatesTargetEndTimeUtc_FromCurrentTime()
    {
        var startTime = new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);
        var clock = new FakeTimeProvider(startTime);
        var controller = new TimerController(clock);

        controller.Start(TimeSpan.FromMinutes(10));
        clock.Advance(TimeSpan.FromMinutes(4)); // 6 min remaining
        controller.Pause();

        // Advance time during pause
        clock.Advance(TimeSpan.FromMinutes(30));

        // Resume at 10:34:00
        controller.Resume();
        Assert.Equal(TimerState.Running, controller.State);
        Assert.Equal(clock.GetUtcNow().AddMinutes(6), controller.TargetEndTimeUtc);

        // Advance 6 minutes to finish
        clock.Advance(TimeSpan.FromMinutes(6));
        var snapshot = controller.UpdateTick();

        Assert.Equal(TimerState.Completed, snapshot.State);
        Assert.Equal(TimeSpan.Zero, snapshot.RemainingTime);
    }

    [Fact]
    public void Pomodoro_WorkAndBreakTransitions()
    {
        var startTime = new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);
        var clock = new FakeTimeProvider(startTime);
        var controller = new TimerController(
            clock,
            pomodoroWorkDuration: TimeSpan.FromMinutes(25),
            pomodoroBreakDuration: TimeSpan.FromMinutes(5));

        // Start Pomodoro Work
        controller.Start(mode: TimerMode.PomodoroWork);
        Assert.Equal(TimerMode.PomodoroWork, controller.Mode);
        Assert.Equal(TimeSpan.FromMinutes(25), controller.TotalDuration);
        Assert.Equal("25:00", controller.CurrentSnapshot.FormattedTime);

        // Complete work session
        clock.Advance(TimeSpan.FromMinutes(25));
        var finishedWork = controller.UpdateTick();
        Assert.Equal(TimerState.Completed, finishedWork.State);

        // Switch to Break session
        controller.SetMode(TimerMode.PomodoroBreak);
        controller.Start();
        Assert.Equal(TimerMode.PomodoroBreak, controller.Mode);
        Assert.Equal(TimeSpan.FromMinutes(5), controller.TotalDuration);
        Assert.Equal(TimerState.Running, controller.State);
        Assert.Equal("05:00", controller.CurrentSnapshot.FormattedTime);

        // Complete break session
        clock.Advance(TimeSpan.FromMinutes(5));
        var finishedBreak = controller.UpdateTick();
        Assert.Equal(TimerState.Completed, finishedBreak.State);
    }

    [Fact]
    public void AddTime_WhileRunning_ExtendsTargetTimestamp()
    {
        var startTime = new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);
        var clock = new FakeTimeProvider(startTime);
        var controller = new TimerController(clock);

        controller.Start(TimeSpan.FromMinutes(5));
        clock.Advance(TimeSpan.FromMinutes(2)); // 3 min remaining

        controller.AddTime(TimeSpan.FromMinutes(3)); // 3 + 3 = 6 min remaining

        Assert.Equal(TimeSpan.FromMinutes(6), controller.RemainingTime);
        Assert.Equal(startTime.AddMinutes(8), controller.TargetEndTimeUtc);
    }

    [Theory]
    [InlineData(0, "00:00")]
    [InlineData(45, "00:45")]
    [InlineData(60, "01:00")]
    [InlineData(125, "02:05")]
    [InlineData(3599, "59:59")]
    [InlineData(3600, "01:00:00")]
    [InlineData(3665, "01:01:05")]
    [InlineData(7325, "02:02:05")]
    public void FormatTime_FormatsExactStrings(int seconds, string expected)
    {
        string actual = TimerController.FormatTime(TimeSpan.FromSeconds(seconds));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Stop_ResetsRunningTimer()
    {
        var startTime = new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);
        var clock = new FakeTimeProvider(startTime);
        var controller = new TimerController(clock, defaultStandardDuration: TimeSpan.FromMinutes(10));

        controller.Start();
        clock.Advance(TimeSpan.FromMinutes(3));
        controller.Stop();

        Assert.Equal(TimerState.Stopped, controller.State);
        Assert.Null(controller.TargetEndTimeUtc);
        Assert.Equal(TimeSpan.FromMinutes(10), controller.RemainingTime);
        Assert.Equal("10:00", controller.CurrentSnapshot.FormattedTime);
    }

    [Fact]
    public void AddTime_WhilePaused_UpdatesRemainingAndTotalDurationProperly()
    {
        var startTime = new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);
        var clock = new FakeTimeProvider(startTime);
        var controller = new TimerController(clock);

        controller.Start(TimeSpan.FromMinutes(5));
        clock.Advance(TimeSpan.FromMinutes(2)); // 3m remaining
        controller.Pause();

        controller.AddTime(TimeSpan.FromMinutes(1)); // 4m remaining, 6m total

        Assert.Equal(TimeSpan.FromMinutes(4), controller.RemainingTime);
        Assert.Equal(TimeSpan.FromMinutes(6), controller.TotalDuration);
    }

    [Fact]
    public void RestorePaused_PreservesOriginalTotalDurationAndProgress()
    {
        var startTime = new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);
        var clock = new FakeTimeProvider(startTime);
        var controller = new TimerController(clock);

        controller.RestorePaused(
            totalDuration: TimeSpan.FromMinutes(10),
            remainingTime: TimeSpan.FromMinutes(4),
            mode: TimerMode.Standard);

        Assert.Equal(TimerState.Paused, controller.State);
        Assert.Equal(TimeSpan.FromMinutes(10), controller.TotalDuration);
        Assert.Equal(TimeSpan.FromMinutes(4), controller.RemainingTime);
        Assert.Equal(0.6, controller.CurrentSnapshot.ProgressRatio, precision: 3);
    }
}
