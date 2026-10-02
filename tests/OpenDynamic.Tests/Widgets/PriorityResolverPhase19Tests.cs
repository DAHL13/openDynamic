using OpenDynamic.Core.State;
using OpenDynamic.Core.Widgets;

namespace OpenDynamic.Tests.Widgets;

public class PriorityResolverPhase19Tests
{
    private sealed class MockSource : IActivitySource
    {
        public string Id { get; init; } = "";
        public int Priority { get; init; }
        public bool IsActive { get; init; } = true;
        public bool IsTransient { get; init; }
        public DateTimeOffset? LastActivatedUtc { get; init; }
        public TimeSpan? TransientDuration { get; init; }
        public ActivityActivationMode ActivationMode { get; init; } = ActivityActivationMode.Event;
        public IslandActivity? CurrentActivity => null;
#pragma warning disable CS0067
        public event EventHandler? Changed;
#pragma warning restore CS0067
    }

    [Fact]
    public void AmbientClock_WhenAtRest_WithoutHover_DoesNotKeepIslandVisible()
    {
        var resolver = new PriorityResolver();
        var clockSource = new MockSource
        {
            Id = "clock",
            Priority = ActivityPriority.AmbientClock, // 5
            IsActive = true,
            ActivationMode = ActivityActivationMode.OnHover
        };

        var result = resolver.Resolve(new[] { clockSource }, isHovering: false);

        Assert.Null(result.Primary);
        Assert.Null(result.Secondary);
        Assert.False(result.HasActiveActivity);
        Assert.Equal(IslandState.Hidden, result.SuggestedState);
    }

    [Fact]
    public void AmbientClock_WhenAtRest_WithHover_ActivatesInCompactMode()
    {
        var resolver = new PriorityResolver();
        var clockSource = new MockSource
        {
            Id = "clock",
            Priority = ActivityPriority.AmbientClock, // 5
            IsActive = true,
            ActivationMode = ActivityActivationMode.OnHover
        };

        var result = resolver.Resolve(new[] { clockSource }, isHovering: true);

        Assert.Equal("clock", result.Primary?.Id);
        Assert.Null(result.Secondary);
        Assert.False(result.IsSplit);
        Assert.Equal(IslandState.Compact, result.SuggestedState);
    }

    [Theory]
    [InlineData("hardware", ActivityPriority.Hardware)]       // 10
    [InlineData("media", ActivityPriority.Media)]             // 30
    [InlineData("stopwatch", ActivityPriority.Stopwatch)]     // 45
    [InlineData("timer", ActivityPriority.Timer)]             // 50
    [InlineData("clipboard", ActivityPriority.Clipboard)]     // 55
    [InlineData("device", ActivityPriority.Device)]           // 60
    [InlineData("network", ActivityPriority.Network)]         // 65
    [InlineData("volume", ActivityPriority.Volume)]           // 80
    [InlineData("battery", ActivityPriority.Battery)]         // 90
    [InlineData("timer-alert", ActivityPriority.TimerAlert)]  // 100
    public void AmbientClock_WhenAnyOtherActivityActive_OtherActivityWinsAndClockNeverShows(string activityId, int priority)
    {
        var resolver = new PriorityResolver();
        var clockSource = new MockSource
        {
            Id = "clock",
            Priority = ActivityPriority.AmbientClock, // 5
            IsActive = true,
            ActivationMode = ActivityActivationMode.OnHover
        };

        var otherActivity = new MockSource
        {
            Id = activityId,
            Priority = priority,
            IsActive = true,
            ActivationMode = ActivityActivationMode.Event
        };

        // Regardless of hovering state, other activity MUST win
        var resultHover = resolver.Resolve(new[] { clockSource, otherActivity }, isHovering: true);
        var resultNoHover = resolver.Resolve(new[] { clockSource, otherActivity }, isHovering: false);

        Assert.Equal(activityId, resultHover.Primary?.Id);
        Assert.Null(resultHover.Secondary); // Clock is never included as secondary split
        Assert.NotEqual("clock", resultHover.Primary?.Id);

        Assert.Equal(activityId, resultNoHover.Primary?.Id);
        Assert.Null(resultNoHover.Secondary);
    }

    [Fact]
    public void AmbientClock_WhenSplitPossibleWithOtherEventSources_ClockNeverParticipatesInSplit()
    {
        var resolver = new PriorityResolver();
        var clockSource = new MockSource
        {
            Id = "clock",
            Priority = ActivityPriority.AmbientClock,
            IsActive = true,
            ActivationMode = ActivityActivationMode.OnHover
        };

        var timerSource = new MockSource
        {
            Id = "timer",
            Priority = ActivityPriority.Timer, // 50
            IsActive = true,
            ActivationMode = ActivityActivationMode.Event
        };

        var mediaSource = new MockSource
        {
            Id = "media",
            Priority = ActivityPriority.Media, // 30
            IsActive = true,
            ActivationMode = ActivityActivationMode.Event
        };

        var result = resolver.Resolve(new[] { clockSource, timerSource, mediaSource }, isHovering: true);

        Assert.Equal("timer", result.Primary?.Id);
        Assert.Equal("media", result.Secondary?.Id);
        Assert.True(result.IsSplit);
        Assert.Equal(IslandState.Split, result.SuggestedState);
    }
}
