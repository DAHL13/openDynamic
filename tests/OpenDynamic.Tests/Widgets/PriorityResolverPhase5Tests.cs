using OpenDynamic.Core.State;
using OpenDynamic.Core.Widgets;
using Xunit;

namespace OpenDynamic.Tests.Widgets;

public class PriorityResolverPhase5Tests
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
    public void VolumeTransient_PreemptsMediaWidget()
    {
        var resolver = new PriorityResolver();
        var now = DateTimeOffset.UtcNow;

        var mediaSource = new MockSource
        {
            Id = "media",
            Priority = 30,
            IsActive = true,
            IsTransient = false,
            LastActivatedUtc = now.AddMinutes(-5)
        };

        var volumeSource = new MockSource
        {
            Id = "volume",
            Priority = 80,
            IsActive = true,
            IsTransient = true,
            LastActivatedUtc = now,
            TransientDuration = TimeSpan.FromSeconds(2)
        };

        var sources = new List<IActivitySource> { mediaSource, volumeSource };

        // At t = 0s: Volume (80) takes precedence as exclusive primary
        var resultNow = resolver.Resolve(sources, now);
        Assert.Equal("volume", resultNow.Primary?.Id);
        Assert.Null(resultNow.Secondary);
        Assert.Equal(IslandState.Compact, resultNow.SuggestedState);
        Assert.Equal(now.AddSeconds(2), resultNow.NextExpirationUtc);

        // At t = 2.1s: Volume has expired, Media resumes control
        var resultExpired = resolver.Resolve(sources, now.AddSeconds(2.1));
        Assert.Equal("media", resultExpired.Primary?.Id);
        Assert.Null(resultExpired.Secondary);
        Assert.Equal(IslandState.Compact, resultExpired.SuggestedState);
    }

    [Fact]
    public void BatteryTransient_PreemptsVolumeTransient()
    {
        var resolver = new PriorityResolver();
        var now = DateTimeOffset.UtcNow;

        var volumeSource = new MockSource
        {
            Id = "volume",
            Priority = 80,
            IsActive = true,
            IsTransient = true,
            LastActivatedUtc = now,
            TransientDuration = TimeSpan.FromSeconds(2)
        };

        var batterySource = new MockSource
        {
            Id = "battery",
            Priority = 90,
            IsActive = true,
            IsTransient = true,
            LastActivatedUtc = now,
            TransientDuration = TimeSpan.FromSeconds(3)
        };

        var sources = new List<IActivitySource> { volumeSource, batterySource };

        // Battery (90) beats Volume (80)
        var result = resolver.Resolve(sources, now);
        Assert.Equal("battery", result.Primary?.Id);
        Assert.Null(result.Secondary);
        Assert.Equal(now.AddSeconds(2), result.NextExpirationUtc); // Earliest expiration
    }

    [Fact]
    public void ResumingVolume_ResetsTransientExpiration()
    {
        var resolver = new PriorityResolver();
        var t0 = DateTimeOffset.UtcNow;

        var volumeSource = new MockSource
        {
            Id = "volume",
            Priority = 80,
            IsActive = true,
            IsTransient = true,
            LastActivatedUtc = t0,
            TransientDuration = TimeSpan.FromSeconds(2)
        };

        var result0 = resolver.Resolve(new[] { volumeSource }, t0);
        Assert.Equal(t0.AddSeconds(2), result0.NextExpirationUtc);

        // User interacts with mouse wheel at t = 1.5s -> resets LastActivatedUtc
        var t1 = t0.AddSeconds(1.5);
        var refreshedVolume = new MockSource
        {
            Id = "volume",
            Priority = 80,
            IsActive = true,
            IsTransient = true,
            LastActivatedUtc = t1,
            TransientDuration = TimeSpan.FromSeconds(2)
        };

        var result1 = resolver.Resolve(new[] { refreshedVolume }, t1);
        Assert.Equal(t1.AddSeconds(2), result1.NextExpirationUtc);
    }
}
