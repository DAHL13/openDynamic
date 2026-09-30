using OpenDynamic.Core.State;
using OpenDynamic.Core.Widgets;
using Xunit;

namespace OpenDynamic.Tests.Widgets;

public sealed class PriorityResolverPhase12Tests
{
    private sealed class MockSource : IActivitySource
    {
        public string Id { get; init; } = "";
        public int Priority { get; init; }
        public bool IsActive { get; init; } = true;
        public bool IsTransient { get; init; }
        public DateTimeOffset? LastActivatedUtc { get; init; }
        public TimeSpan? TransientDuration { get; init; }
        public IslandActivity? CurrentActivity => null;
#pragma warning disable CS0067
        public event EventHandler? Changed;
#pragma warning restore CS0067
    }

    [Fact]
    public void Timer_Priority50_WinsOver_Stopwatch_Priority45()
    {
        var resolver = new PriorityResolver();
        var now = DateTimeOffset.UtcNow;

        var timer = new MockSource
        {
            Id = "timer",
            Priority = ActivityPriority.Timer, // 50
            IsActive = true,
            LastActivatedUtc = now.AddMinutes(-5)
        };

        var stopwatch = new MockSource
        {
            Id = "stopwatch",
            Priority = ActivityPriority.Stopwatch, // 45
            IsActive = true,
            LastActivatedUtc = now.AddMinutes(-2)
        };

        var result = resolver.Resolve(new[] { stopwatch, timer }, now);

        Assert.Equal("timer", result.Primary?.Id);
        Assert.Equal("stopwatch", result.Secondary?.Id);
        Assert.Equal(IslandState.Split, result.SuggestedState);
    }

    [Fact]
    public void Stopwatch_Priority45_WinsOver_Media_Priority30()
    {
        var resolver = new PriorityResolver();
        var now = DateTimeOffset.UtcNow;

        var stopwatch = new MockSource
        {
            Id = "stopwatch",
            Priority = ActivityPriority.Stopwatch, // 45
            IsActive = true,
            LastActivatedUtc = now.AddMinutes(-10)
        };

        var media = new MockSource
        {
            Id = "media",
            Priority = ActivityPriority.Media, // 30
            IsActive = true,
            LastActivatedUtc = now.AddMinutes(-1)
        };

        var result = resolver.Resolve(new[] { media, stopwatch }, now);

        Assert.Equal("stopwatch", result.Primary?.Id);
        Assert.Equal("media", result.Secondary?.Id);
        Assert.Equal(IslandState.Split, result.SuggestedState);
    }

    [Fact]
    public void TransientTimerAlert_Priority100_Preempts_BothTimerAndStopwatch_InSplitMode()
    {
        var resolver = new PriorityResolver();
        var now = DateTimeOffset.UtcNow;

        var timerAlert = new MockSource
        {
            Id = "timer",
            Priority = ActivityPriority.TimerAlert, // 100
            IsActive = true,
            IsTransient = true,
            LastActivatedUtc = now,
            TransientDuration = TimeSpan.FromSeconds(5)
        };

        var stopwatch = new MockSource
        {
            Id = "stopwatch",
            Priority = ActivityPriority.Stopwatch, // 45
            IsActive = true
        };

        var media = new MockSource
        {
            Id = "media",
            Priority = ActivityPriority.Media, // 30
            IsActive = true
        };

        var result = resolver.Resolve(new[] { stopwatch, media, timerAlert }, now);

        // Transient alert must take full compact island, suppressing split
        Assert.Equal("timer", result.Primary?.Id);
        Assert.Null(result.Secondary);
        Assert.Equal(IslandState.Compact, result.SuggestedState);
    }

    [Fact]
    public void NetworkAlert_Priority65_Preempts_Stopwatch()
    {
        var resolver = new PriorityResolver();
        var now = DateTimeOffset.UtcNow;

        var network = new MockSource
        {
            Id = "network",
            Priority = ActivityPriority.Network, // 65
            IsActive = true,
            IsTransient = true,
            LastActivatedUtc = now,
            TransientDuration = TimeSpan.FromSeconds(3)
        };

        var stopwatch = new MockSource
        {
            Id = "stopwatch",
            Priority = ActivityPriority.Stopwatch, // 45
            IsActive = true
        };

        var result = resolver.Resolve(new[] { stopwatch, network }, now);

        Assert.Equal("network", result.Primary?.Id);
        Assert.Null(result.Secondary);
        Assert.Equal(IslandState.Compact, result.SuggestedState);
    }
}
