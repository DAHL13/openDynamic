using OpenDynamic.Core.State;
using OpenDynamic.Core.Widgets;

namespace OpenDynamic.Tests.Widgets;

public sealed class PriorityResolverPhase21Tests
{
    private sealed class MockSource : IActivitySource
    {
        public string Id { get; init; } = string.Empty;
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
    public void ActivityPriority_Screenshot_IsExactly75()
    {
        Assert.Equal(75, ActivityPriority.Screenshot);
        Assert.True(ActivityPriority.Volume > ActivityPriority.Screenshot);
        Assert.True(ActivityPriority.Screenshot > ActivityPriority.Network);
        Assert.True(ActivityPriority.Screenshot > ActivityPriority.Device);
        Assert.True(ActivityPriority.Screenshot > ActivityPriority.Clipboard);
        Assert.True(ActivityPriority.Screenshot > ActivityPriority.Media);
    }

    [Theory]
    [InlineData("clock", ActivityPriority.AmbientClock)]
    [InlineData("hardware", ActivityPriority.Hardware)]
    [InlineData("media", ActivityPriority.Media)]
    [InlineData("stopwatch", ActivityPriority.Stopwatch)]
    [InlineData("timer", ActivityPriority.Timer)]
    [InlineData("clipboard", ActivityPriority.Clipboard)]
    [InlineData("device", ActivityPriority.Device)]
    [InlineData("network", ActivityPriority.Network)]
    public void Screenshot_WinsOverLowerPriorityActivities(string lowerId, int lowerPriority)
    {
        var resolver = new PriorityResolver();
        var now = DateTimeOffset.UtcNow;
        var screenshotSource = new MockSource
        {
            Id = "screenshot",
            Priority = ActivityPriority.Screenshot,
            IsActive = true,
            IsTransient = true,
            LastActivatedUtc = now,
            TransientDuration = TimeSpan.FromSeconds(6)
        };

        var lowerSource = new MockSource
        {
            Id = lowerId,
            Priority = lowerPriority,
            IsActive = true,
            LastActivatedUtc = now.AddSeconds(-5)
        };

        var result = resolver.Resolve([lowerSource, screenshotSource], currentTime: now, isHovering: false);
        Assert.Equal("screenshot", result.Primary?.Id);
    }

    [Theory]
    [InlineData("volume", ActivityPriority.Volume)]
    [InlineData("privacy", ActivityPriority.Privacy)]
    [InlineData("energy-saver", ActivityPriority.EnergySaver)]
    [InlineData("battery", ActivityPriority.Battery)]
    [InlineData("timer-alert", ActivityPriority.TimerAlert)]
    public void Screenshot_YieldsToHigherPriorityActivities(string higherId, int higherPriority)
    {
        var resolver = new PriorityResolver();
        var now = DateTimeOffset.UtcNow;
        var screenshotSource = new MockSource
        {
            Id = "screenshot",
            Priority = ActivityPriority.Screenshot,
            IsActive = true,
            IsTransient = true,
            LastActivatedUtc = now,
            TransientDuration = TimeSpan.FromSeconds(6)
        };

        var higherSource = new MockSource
        {
            Id = higherId,
            Priority = higherPriority,
            IsActive = true,
            IsTransient = true,
            LastActivatedUtc = now,
            TransientDuration = TimeSpan.FromSeconds(3)
        };

        var result = resolver.Resolve([screenshotSource, higherSource], currentTime: now, isHovering: false);
        Assert.Equal(higherId, result.Primary?.Id);
    }

    [Fact]
    public void Screenshot_WhenTransientExpires_UnderlyingMediaResumes()
    {
        var resolver = new PriorityResolver();
        var activatedAt = DateTimeOffset.UtcNow;
        var screenshotSource = new MockSource
        {
            Id = "screenshot",
            Priority = ActivityPriority.Screenshot,
            IsActive = true,
            IsTransient = true,
            LastActivatedUtc = activatedAt,
            TransientDuration = TimeSpan.FromSeconds(6)
        };

        var mediaSource = new MockSource
        {
            Id = "media",
            Priority = ActivityPriority.Media,
            IsActive = true,
            LastActivatedUtc = activatedAt.AddSeconds(-30)
        };

        var activeResult = resolver.Resolve([mediaSource, screenshotSource], currentTime: activatedAt.AddSeconds(3), isHovering: false);
        Assert.Equal("screenshot", activeResult.Primary?.Id);

        var expiredResult = resolver.Resolve([mediaSource, screenshotSource], currentTime: activatedAt.AddSeconds(7), isHovering: false);
        Assert.Equal("media", expiredResult.Primary?.Id);
    }
}
