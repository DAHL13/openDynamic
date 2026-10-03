using OpenDynamic.Core.State;
using OpenDynamic.Core.Widgets;

namespace OpenDynamic.Tests.Widgets;

public class PriorityResolverPhase20Tests
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
    public void EnergySaver_Priority_Is88()
    {
        Assert.Equal(88, ActivityPriority.EnergySaver);
    }

    [Theory]
    [InlineData("clock", ActivityPriority.AmbientClock)]     // 5
    [InlineData("hardware", ActivityPriority.Hardware)]       // 10
    [InlineData("media", ActivityPriority.Media)]             // 30
    [InlineData("stopwatch", ActivityPriority.Stopwatch)]     // 45
    [InlineData("timer", ActivityPriority.Timer)]             // 50
    [InlineData("clipboard", ActivityPriority.Clipboard)]     // 55
    [InlineData("device", ActivityPriority.Device)]           // 60
    [InlineData("network", ActivityPriority.Network)]         // 65
    [InlineData("volume", ActivityPriority.Volume)]           // 80
    [InlineData("privacy", ActivityPriority.Privacy)]         // 85
    public void EnergySaver_WinsOverLowerPriorityActivities(string lowerId, int lowerPriority)
    {
        var resolver = new PriorityResolver();
        var energySaverSource = new MockSource
        {
            Id = "energy-saver",
            Priority = ActivityPriority.EnergySaver, // 88
            IsActive = true,
            IsTransient = true,
            TransientDuration = TimeSpan.FromSeconds(3)
        };

        var lowerSource = new MockSource
        {
            Id = lowerId,
            Priority = lowerPriority,
            IsActive = true
        };

        var result = resolver.Resolve(new[] { lowerSource, energySaverSource }, isHovering: false);

        Assert.Equal("energy-saver", result.Primary?.Id);
    }

    [Theory]
    [InlineData("battery", ActivityPriority.Battery)]           // 90
    [InlineData("timer-alert", ActivityPriority.TimerAlert)]    // 100
    public void EnergySaver_YieldsToHigherPriorityActivities(string higherId, int higherPriority)
    {
        var resolver = new PriorityResolver();
        var energySaverSource = new MockSource
        {
            Id = "energy-saver",
            Priority = ActivityPriority.EnergySaver, // 88
            IsActive = true,
            IsTransient = true,
            TransientDuration = TimeSpan.FromSeconds(3)
        };

        var higherSource = new MockSource
        {
            Id = higherId,
            Priority = higherPriority,
            IsActive = true
        };

        var result = resolver.Resolve(new[] { energySaverSource, higherSource }, isHovering: false);

        Assert.Equal(higherId, result.Primary?.Id);
    }

    [Fact]
    public void EnergySaver_WhenTransient_TakesExclusiveSpotlight_NoSplit()
    {
        var resolver = new PriorityResolver();
        var now = DateTimeOffset.UtcNow;
        var energySaverSource = new MockSource
        {
            Id = "energy-saver",
            Priority = ActivityPriority.EnergySaver, // 88
            IsActive = true,
            IsTransient = true,
            LastActivatedUtc = now,
            TransientDuration = TimeSpan.FromSeconds(3)
        };

        var mediaSource = new MockSource
        {
            Id = "media",
            Priority = ActivityPriority.Media, // 30
            IsActive = true
        };

        var result = resolver.Resolve(new[] { mediaSource, energySaverSource }, currentTime: now, isHovering: false);

        // Exclusive spotlight: no split allowed while transient alert is active
        Assert.Equal("energy-saver", result.Primary?.Id);
        Assert.Null(result.Secondary);
        Assert.False(result.IsSplit);
        Assert.Equal(IslandState.Compact, result.SuggestedState);
    }

    [Fact]
    public void EnergySaver_WhenTransientExpires_UnderlyingMediaResumes()
    {
        var resolver = new PriorityResolver();
        var activatedAt = DateTimeOffset.UtcNow;
        var energySaverSource = new MockSource
        {
            Id = "energy-saver",
            Priority = ActivityPriority.EnergySaver, // 88
            IsActive = true,
            IsTransient = true,
            LastActivatedUtc = activatedAt,
            TransientDuration = TimeSpan.FromSeconds(3)
        };

        var mediaSource = new MockSource
        {
            Id = "media",
            Priority = ActivityPriority.Media, // 30
            IsActive = true
        };

        // Query at 4 seconds later (after 3s expiration)
        var result = resolver.Resolve(new[] { mediaSource, energySaverSource }, currentTime: activatedAt.AddSeconds(4), isHovering: false);

        Assert.Equal("media", result.Primary?.Id);
        Assert.Null(result.Secondary);
        Assert.False(result.IsSplit);
    }
}
