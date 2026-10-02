using OpenDynamic.Core.Clock;

namespace OpenDynamic.Tests.Clock;

public class ClockTickSchedulerTests
{
    private sealed class TestTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow;

        public TestTimeProvider(DateTimeOffset initialUtc)
        {
            _utcNow = initialUtc;
        }

        public void SetUtcNow(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    [Fact]
    public void CalculateDelay_MinuteBoundary_CalculatesRemainingSecondsAndMilliseconds()
    {
        // 14:20:15.300 -> Remaining until 14:21:00.000:
        // Remaining seconds: 59 - 15 = 44
        // Remaining millis: 1000 - 300 = 700
        // Total ms: 44,700 ms
        var time = new DateTimeOffset(2026, 10, 2, 14, 20, 15, 300, TimeSpan.Zero);
        var delay = ClockTickScheduler.CalculateDelay(time, showSeconds: false);

        Assert.Equal(44700, delay.TotalMilliseconds);
    }

    [Fact]
    public void CalculateDelay_SecondsBoundary_CalculatesRemainingMillisecondsInCurrentSecond()
    {
        // 14:20:15.300 -> Remaining until 14:20:16.000: 700 ms
        var time = new DateTimeOffset(2026, 10, 2, 14, 20, 15, 300, TimeSpan.Zero);
        var delay = ClockTickScheduler.CalculateDelay(time, showSeconds: true);

        Assert.Equal(700, delay.TotalMilliseconds);
    }

    [Fact]
    public void CalculateDelay_MidnightCrossing_CalculatesCorrectDelayAcrossMidnight()
    {
        // 23:59:58.400 -> Remaining until 00:00:00.000:
        // Remaining seconds: 59 - 58 = 1
        // Remaining millis: 1000 - 400 = 600
        // Total ms: 1600 ms
        var time = new DateTimeOffset(2026, 10, 2, 23, 59, 58, 400, TimeSpan.Zero);
        var delay = ClockTickScheduler.CalculateDelay(time, showSeconds: false);

        Assert.Equal(1600, delay.TotalMilliseconds);
    }

    [Fact]
    public void CalculateDelay_EnforcesMinimumDelayOf50Milliseconds()
    {
        // 14:20:59.980 -> Remaining is 20 ms, which is less than the 50 ms minimum
        var time = new DateTimeOffset(2026, 10, 2, 14, 20, 59, 980, TimeSpan.Zero);
        var delay = ClockTickScheduler.CalculateDelay(time, showSeconds: false);

        Assert.Equal(50, delay.TotalMilliseconds);

        // Same for second boundary:
        var delaySec = ClockTickScheduler.CalculateDelay(time, showSeconds: true);
        Assert.Equal(50, delaySec.TotalMilliseconds);
    }

    [Fact]
    public void GetDelayUntilNextTick_RecalculatesAccuratelyAfterTimeJump()
    {
        var initial = new DateTimeOffset(2026, 10, 2, 10, 0, 10, 0, TimeSpan.Zero);
        var timeProvider = new TestTimeProvider(initial);
        var scheduler = new ClockTickScheduler(timeProvider);

        var delay1 = scheduler.GetDelayUntilNextTick(showSeconds: false);
        // 50 seconds remaining -> 50,000 ms
        Assert.Equal(50000, delay1.TotalMilliseconds);

        // Simulate sudden clock jump (e.g., timezone change, daylight saving, manual adjustment):
        // Jumped to 16:45:50.000
        var jumpedTime = new DateTimeOffset(2026, 10, 2, 16, 45, 50, 0, TimeSpan.Zero);
        timeProvider.SetUtcNow(jumpedTime);

        var delay2 = scheduler.GetDelayUntilNextTick(showSeconds: false);
        // 10 seconds remaining -> 10,000 ms
        Assert.Equal(10000, delay2.TotalMilliseconds);
    }
}
