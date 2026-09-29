using OpenDynamic.Core.State;
using OpenDynamic.Core.Widgets;
using Xunit;

namespace OpenDynamic.Tests.Widgets;

public class PriorityResolverPhase6Tests
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
    public void TimerRunning_And_MediaRunning_ResolvesSplitMode()
    {
        var resolver = new PriorityResolver();
        var now = DateTimeOffset.UtcNow;

        var timerSource = new MockSource
        {
            Id = "timer",
            Priority = ActivityPriority.Timer, // 50
            IsActive = true,
            IsTransient = false,
            LastActivatedUtc = now
        };

        var mediaSource = new MockSource
        {
            Id = "media",
            Priority = ActivityPriority.Media, // 30
            IsActive = true,
            IsTransient = false,
            LastActivatedUtc = now.AddMinutes(-2)
        };

        var sources = new List<IActivitySource> { timerSource, mediaSource };
        var result = resolver.Resolve(sources, now);

        Assert.Equal("timer", result.Primary?.Id);
        Assert.Equal("media", result.Secondary?.Id);
        Assert.Equal(IslandState.Split, result.SuggestedState);
    }

    [Fact]
    public void MediaRunning_And_HardwareRunning_ResolvesSplitMode()
    {
        var resolver = new PriorityResolver();
        var now = DateTimeOffset.UtcNow;

        var mediaSource = new MockSource
        {
            Id = "media",
            Priority = ActivityPriority.Media, // 30
            IsActive = true,
            IsTransient = false,
            LastActivatedUtc = now
        };

        var hardwareSource = new MockSource
        {
            Id = "hardware",
            Priority = ActivityPriority.Hardware, // 10
            IsActive = true,
            IsTransient = false,
            LastActivatedUtc = now.AddMinutes(-10)
        };

        var sources = new List<IActivitySource> { mediaSource, hardwareSource };
        var result = resolver.Resolve(sources, now);

        Assert.Equal("media", result.Primary?.Id);
        Assert.Equal("hardware", result.Secondary?.Id);
        Assert.Equal(IslandState.Split, result.SuggestedState);
    }

    [Fact]
    public void HardwareRunning_Alone_ResolvesCompactMode()
    {
        var resolver = new PriorityResolver();
        var now = DateTimeOffset.UtcNow;

        var hardwareSource = new MockSource
        {
            Id = "hardware",
            Priority = ActivityPriority.Hardware, // 10
            IsActive = true,
            IsTransient = false,
            LastActivatedUtc = now
        };

        var sources = new List<IActivitySource> { hardwareSource };
        var result = resolver.Resolve(sources, now);

        Assert.Equal("hardware", result.Primary?.Id);
        Assert.Null(result.Secondary);
        Assert.Equal(IslandState.Compact, result.SuggestedState);
    }

    [Fact]
    public void TimerCompletedAlert_Priority100_PreemptsAllOtherActivities()
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

        var battery = new MockSource
        {
            Id = "battery",
            Priority = ActivityPriority.Battery, // 90
            IsActive = true,
            IsTransient = true,
            LastActivatedUtc = now,
            TransientDuration = TimeSpan.FromSeconds(3)
        };

        var volume = new MockSource
        {
            Id = "volume",
            Priority = ActivityPriority.Volume, // 80
            IsActive = true,
            IsTransient = true,
            LastActivatedUtc = now,
            TransientDuration = TimeSpan.FromSeconds(2)
        };

        var media = new MockSource
        {
            Id = "media",
            Priority = ActivityPriority.Media, // 30
            IsActive = true,
            IsTransient = false,
            LastActivatedUtc = now.AddMinutes(-5)
        };

        var hardware = new MockSource
        {
            Id = "hardware",
            Priority = ActivityPriority.Hardware, // 10
            IsActive = true,
            IsTransient = false,
            LastActivatedUtc = now.AddMinutes(-10)
        };

        var sources = new List<IActivitySource> { hardware, media, volume, battery, timerAlert };

        // Timer alert (100) must win as exclusive spotlight
        var result = resolver.Resolve(sources, now);
        Assert.Equal("timer", result.Primary?.Id);
        Assert.Null(result.Secondary);
        Assert.Equal(IslandState.Compact, result.SuggestedState);
        Assert.Equal(now.AddSeconds(2), result.NextExpirationUtc); // Earliest expiration (volume 2s)
    }

    [Fact]
    public void TransientNotice_PreemptsSplitMode_AndRestoresSplitAfterExpiration()
    {
        var resolver = new PriorityResolver();
        var now = DateTimeOffset.UtcNow;

        var timer = new MockSource
        {
            Id = "timer",
            Priority = 50,
            IsActive = true,
            IsTransient = false,
            LastActivatedUtc = now
        };

        var media = new MockSource
        {
            Id = "media",
            Priority = 30,
            IsActive = true,
            IsTransient = false,
            LastActivatedUtc = now
        };

        var volumeAlert = new MockSource
        {
            Id = "volume",
            Priority = 80,
            IsActive = true,
            IsTransient = true,
            LastActivatedUtc = now,
            TransientDuration = TimeSpan.FromSeconds(2)
        };

        var sources = new List<IActivitySource> { timer, media, volumeAlert };

        // While volume alert is active (t=0s): Volume takes over as exclusive primary
        var activeAlertResult = resolver.Resolve(sources, now);
        Assert.Equal("volume", activeAlertResult.Primary?.Id);
        Assert.Null(activeAlertResult.Secondary);
        Assert.Equal(IslandState.Compact, activeAlertResult.SuggestedState);

        // After volume expires (t=2.1s): Restores Split mode with Timer and Media
        var expiredResult = resolver.Resolve(sources, now.AddSeconds(2.1));
        Assert.Equal("timer", expiredResult.Primary?.Id);
        Assert.Equal("media", expiredResult.Secondary?.Id);
        Assert.Equal(IslandState.Split, expiredResult.SuggestedState);
    }
}
