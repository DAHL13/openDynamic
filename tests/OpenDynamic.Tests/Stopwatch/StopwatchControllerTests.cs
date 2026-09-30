using OpenDynamic.Core.Stopwatch;
using OpenDynamic.Tests.Timer;
using Xunit;

namespace OpenDynamic.Tests.Stopwatch;

public sealed class StopwatchControllerTests
{
    [Fact]
    public void Start_SetsRunningState_AndElapsedIncreasesWithClock()
    {
        var startTime = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var clock = new FakeTimeProvider(startTime);
        var controller = new StopwatchController(clock);

        Assert.Equal(StopwatchState.Stopped, controller.State);
        Assert.Equal(TimeSpan.Zero, controller.ElapsedTime);

        controller.Start();

        Assert.Equal(StopwatchState.Running, controller.State);

        clock.Advance(TimeSpan.FromSeconds(15).Add(TimeSpan.FromMilliseconds(450)));
        var snapshot = controller.UpdateTick();

        Assert.Equal(TimeSpan.FromMilliseconds(15450), controller.ElapsedTime);
        Assert.Equal("00:15.45", snapshot.FormattedPrecise);
        Assert.Equal("00:15.45", snapshot.FormattedElapsed);
    }

    [Fact]
    public void Pause_PreservesAccumulatedTime_WithoutDrift()
    {
        var startTime = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var clock = new FakeTimeProvider(startTime);
        var controller = new StopwatchController(clock);

        controller.Start();
        clock.Advance(TimeSpan.FromSeconds(25).Add(TimeSpan.FromMilliseconds(120)));
        controller.Pause();

        Assert.Equal(StopwatchState.Paused, controller.State);
        var pausedElapsed = controller.ElapsedTime;
        Assert.Equal(TimeSpan.FromMilliseconds(25120), pausedElapsed);

        // Advance 3 hours while paused
        clock.Advance(TimeSpan.FromHours(3));
        var tickWhilePaused = controller.UpdateTick();

        // Must have zero drift
        Assert.Equal(StopwatchState.Paused, tickWhilePaused.State);
        Assert.Equal(pausedElapsed, controller.ElapsedTime);
        Assert.Equal("00:25.12", tickWhilePaused.FormattedPrecise);
    }

    [Fact]
    public void Resume_ContinuesAccumulatingTimeFromCurrentTime()
    {
        var startTime = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var clock = new FakeTimeProvider(startTime);
        var controller = new StopwatchController(clock);

        controller.Start();
        clock.Advance(TimeSpan.FromSeconds(10));
        controller.Pause();

        // Advance 10 minutes while paused
        clock.Advance(TimeSpan.FromMinutes(10));

        // Resume and advance 5 seconds
        controller.Resume();
        Assert.Equal(StopwatchState.Running, controller.State);

        clock.Advance(TimeSpan.FromSeconds(5));
        var snapshot = controller.UpdateTick();

        // 10s + 5s = 15s total (paused 10 minutes ignored)
        Assert.Equal(TimeSpan.FromSeconds(15), snapshot.ElapsedTime);
        Assert.Equal("00:15.00", snapshot.FormattedPrecise);
    }

    [Fact]
    public void Reset_ClearsElapsedTimeAndLaps_SetsStoppedState()
    {
        var startTime = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var clock = new FakeTimeProvider(startTime);
        var controller = new StopwatchController(clock);

        controller.Start();
        clock.Advance(TimeSpan.FromSeconds(30));
        controller.Lap();
        clock.Advance(TimeSpan.FromSeconds(20));

        controller.Reset();

        Assert.Equal(StopwatchState.Stopped, controller.State);
        Assert.Equal(TimeSpan.Zero, controller.ElapsedTime);
        Assert.Equal(TimeSpan.Zero, controller.CurrentLapTime);
        Assert.Empty(controller.Laps);
        Assert.Equal("00:00.00", controller.CurrentSnapshot.FormattedPrecise);
    }

    [Fact]
    public void Lap_RecordsIndividualLapDurationsAndCumulativeSplitTimes()
    {
        var startTime = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var clock = new FakeTimeProvider(startTime);
        var controller = new StopwatchController(clock);

        controller.Start();

        // Lap 1 at 12.34s
        clock.Advance(TimeSpan.FromSeconds(12).Add(TimeSpan.FromMilliseconds(340)));
        var lap1 = controller.Lap();

        Assert.Equal(1, lap1.LapNumber);
        Assert.Equal(TimeSpan.FromMilliseconds(12340), lap1.LapDuration);
        Assert.Equal(TimeSpan.FromMilliseconds(12340), lap1.SplitTime);
        Assert.Equal("00:12.34", lap1.FormattedLap);
        Assert.Equal("00:12.34", lap1.FormattedSplit);

        // Lap 2 after another 8.50s (Total = 20.84s)
        clock.Advance(TimeSpan.FromSeconds(8).Add(TimeSpan.FromMilliseconds(500)));
        var lap2 = controller.Lap();

        Assert.Equal(2, lap2.LapNumber);
        Assert.Equal(TimeSpan.FromMilliseconds(8500), lap2.LapDuration);
        Assert.Equal(TimeSpan.FromMilliseconds(20840), lap2.SplitTime);
        Assert.Equal("00:08.50", lap2.FormattedLap);
        Assert.Equal("00:20.84", lap2.FormattedSplit);

        // Lap 3 after another 15.00s (Total = 35.84s)
        clock.Advance(TimeSpan.FromSeconds(15));
        var lap3 = controller.Lap();

        Assert.Equal(3, lap3.LapNumber);
        Assert.Equal(TimeSpan.FromSeconds(15), lap3.LapDuration);
        Assert.Equal(TimeSpan.FromMilliseconds(35840), lap3.SplitTime);

        Assert.Equal(3, controller.Laps.Count);
        Assert.Equal(lap1, controller.Laps[0]);
        Assert.Equal(lap2, controller.Laps[1]);
        Assert.Equal(lap3, controller.Laps[2]);
    }

    [Fact]
    public void CurrentLapTime_ReflectsTimeSinceLastLap()
    {
        var startTime = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var clock = new FakeTimeProvider(startTime);
        var controller = new StopwatchController(clock);

        controller.Start();
        clock.Advance(TimeSpan.FromSeconds(10));
        controller.Lap();

        clock.Advance(TimeSpan.FromSeconds(4).Add(TimeSpan.FromMilliseconds(200)));

        Assert.Equal(TimeSpan.FromSeconds(14).Add(TimeSpan.FromMilliseconds(200)), controller.ElapsedTime);
        Assert.Equal(TimeSpan.FromSeconds(4).Add(TimeSpan.FromMilliseconds(200)), controller.CurrentLapTime);
        Assert.Equal("00:04.20", controller.CurrentSnapshot.FormattedCurrentLap);
    }

    [Theory]
    [InlineData(0, "00:00.00")]
    [InlineData(1250, "00:01.25")]
    [InlineData(59990, "00:59.99")]
    [InlineData(60000, "01:00.00")]
    [InlineData(125500, "02:05.50")]
    public void FormatPrecise_UnderOneHour_FormatsCentiseconds(int milliseconds, string expected)
    {
        var formatted = StopwatchController.FormatPrecise(TimeSpan.FromMilliseconds(milliseconds));
        Assert.Equal(expected, formatted);
    }

    [Theory]
    [InlineData(3600000, "1:00:00")]
    [InlineData(3665000, "1:01:05")]
    [InlineData(7325000, "2:02:05")]
    public void FormatElapsed_OverOneHour_FormatsHoursMinutesSeconds(int milliseconds, string expected)
    {
        var formatted = StopwatchController.FormatElapsed(TimeSpan.FromMilliseconds(milliseconds));
        Assert.Equal(expected, formatted);
    }

    [Theory]
    [InlineData(0, "00:00")]
    [InlineData(59000, "00:59")]
    [InlineData(3665000, "1:01:05")]
    public void FormatStandard_FormatsWithoutCentiseconds(int milliseconds, string expected)
    {
        var formatted = StopwatchController.FormatStandard(TimeSpan.FromMilliseconds(milliseconds));
        Assert.Equal(expected, formatted);
    }
}
