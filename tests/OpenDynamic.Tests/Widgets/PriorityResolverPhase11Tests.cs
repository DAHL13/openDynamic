using OpenDynamic.Core.State;
using OpenDynamic.Core.Widgets;
using Xunit;

namespace OpenDynamic.Tests.Widgets;

public class PriorityResolverPhase11Tests
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
    public void NetworkAlert_Priority65_WinsOver_DeviceAlert_Priority60()
    {
        var resolver = new PriorityResolver();
        var now = DateTimeOffset.UtcNow;

        var networkSource = new MockSource
        {
            Id = "network",
            Priority = ActivityPriority.Network, // 65
            IsActive = true,
            IsTransient = true,
            LastActivatedUtc = now,
            TransientDuration = TimeSpan.FromSeconds(3)
        };

        var deviceSource = new MockSource
        {
            Id = "device",
            Priority = ActivityPriority.Device, // 60
            IsActive = true,
            IsTransient = true,
            LastActivatedUtc = now,
            TransientDuration = TimeSpan.FromSeconds(3)
        };

        var sources = new List<IActivitySource> { deviceSource, networkSource };
        var result = resolver.Resolve(sources, now);

        Assert.Equal("network", result.Primary?.Id);
        Assert.Null(result.Secondary);
        Assert.Equal(IslandState.Compact, result.SuggestedState);
    }

    [Fact]
    public void VolumeAlert_Priority80_WinsOver_NetworkAlert_Priority65()
    {
        var resolver = new PriorityResolver();
        var now = DateTimeOffset.UtcNow;

        var networkSource = new MockSource
        {
            Id = "network",
            Priority = ActivityPriority.Network, // 65
            IsActive = true,
            IsTransient = true,
            LastActivatedUtc = now,
            TransientDuration = TimeSpan.FromSeconds(3)
        };

        var volumeSource = new MockSource
        {
            Id = "volume",
            Priority = ActivityPriority.Volume, // 80
            IsActive = true,
            IsTransient = true,
            LastActivatedUtc = now,
            TransientDuration = TimeSpan.FromSeconds(2)
        };

        var sources = new List<IActivitySource> { networkSource, volumeSource };
        var result = resolver.Resolve(sources, now);

        Assert.Equal("volume", result.Primary?.Id);
        Assert.Null(result.Secondary);
        Assert.Equal(IslandState.Compact, result.SuggestedState);
    }

    [Fact]
    public void DeviceAlert_Priority60_PreemptsSplitMode_AndRestoresSplitAfterExpiration()
    {
        var resolver = new PriorityResolver();
        var now = DateTimeOffset.UtcNow;

        var timer = new MockSource
        {
            Id = "timer",
            Priority = ActivityPriority.Timer, // 50
            IsActive = true,
            IsTransient = false,
            LastActivatedUtc = now
        };

        var media = new MockSource
        {
            Id = "media",
            Priority = ActivityPriority.Media, // 30
            IsActive = true,
            IsTransient = false,
            LastActivatedUtc = now
        };

        var deviceAlert = new MockSource
        {
            Id = "device",
            Priority = ActivityPriority.Device, // 60
            IsActive = true,
            IsTransient = true,
            LastActivatedUtc = now,
            TransientDuration = TimeSpan.FromSeconds(3)
        };

        var sources = new List<IActivitySource> { timer, media, deviceAlert };

        // While device alert is active (t=0s): Device wins exclusive primary spotlight
        var activeAlertResult = resolver.Resolve(sources, now);
        Assert.Equal("device", activeAlertResult.Primary?.Id);
        Assert.Null(activeAlertResult.Secondary);
        Assert.Equal(IslandState.Compact, activeAlertResult.SuggestedState);

        // After device alert expires (t=3.1s): Restores Split mode (Timer + Media)
        var expiredResult = resolver.Resolve(sources, now.AddSeconds(3.1));
        Assert.Equal("timer", expiredResult.Primary?.Id);
        Assert.Equal("media", expiredResult.Secondary?.Id);
        Assert.Equal(IslandState.Split, expiredResult.SuggestedState);
    }

    [Fact]
    public void NetworkAlert_Priority65_PreemptsDeviceAlert_ThenDeviceAlertWinsAfterNetworkExpires()
    {
        var resolver = new PriorityResolver();
        var now = DateTimeOffset.UtcNow;

        var networkAlert = new MockSource
        {
            Id = "network",
            Priority = ActivityPriority.Network, // 65
            IsActive = true,
            IsTransient = true,
            LastActivatedUtc = now,
            TransientDuration = TimeSpan.FromSeconds(2)
        };

        var deviceAlert = new MockSource
        {
            Id = "device",
            Priority = ActivityPriority.Device, // 60
            IsActive = true,
            IsTransient = true,
            LastActivatedUtc = now,
            TransientDuration = TimeSpan.FromSeconds(4)
        };

        var sources = new List<IActivitySource> { networkAlert, deviceAlert };

        // t=0: Network (65) takes precedence
        var resultT0 = resolver.Resolve(sources, now);
        Assert.Equal("network", resultT0.Primary?.Id);
        Assert.Null(resultT0.Secondary);

        // t=2.1s: Network expired (2s), Device (4s) is still active -> Device takes primary
        var resultT2 = resolver.Resolve(sources, now.AddSeconds(2.1));
        Assert.Equal("device", resultT2.Primary?.Id);
        Assert.Null(resultT2.Secondary);

        // t=4.1s: Both expired -> Hidden
        var resultT4 = resolver.Resolve(sources, now.AddSeconds(4.1));
        Assert.Null(resultT4.Primary);
        Assert.Equal(IslandState.Hidden, resultT4.SuggestedState);
    }

    [Fact]
    public void CompletePriorityHierarchy_TimerAlert_Battery_Volume_Network_Device_Timer_Media_Hardware()
    {
        var resolver = new PriorityResolver();
        var now = DateTimeOffset.UtcNow;

        var timerAlert = new MockSource { Id = "timerAlert", Priority = ActivityPriority.TimerAlert, IsActive = true, IsTransient = true, LastActivatedUtc = now, TransientDuration = TimeSpan.FromSeconds(5) };
        var battery = new MockSource { Id = "battery", Priority = ActivityPriority.Battery, IsActive = true, IsTransient = true, LastActivatedUtc = now, TransientDuration = TimeSpan.FromSeconds(5) };
        var volume = new MockSource { Id = "volume", Priority = ActivityPriority.Volume, IsActive = true, IsTransient = true, LastActivatedUtc = now, TransientDuration = TimeSpan.FromSeconds(5) };
        var network = new MockSource { Id = "network", Priority = ActivityPriority.Network, IsActive = true, IsTransient = true, LastActivatedUtc = now, TransientDuration = TimeSpan.FromSeconds(5) };
        var device = new MockSource { Id = "device", Priority = ActivityPriority.Device, IsActive = true, IsTransient = true, LastActivatedUtc = now, TransientDuration = TimeSpan.FromSeconds(5) };
        var timer = new MockSource { Id = "timer", Priority = ActivityPriority.Timer, IsActive = true, IsTransient = false, LastActivatedUtc = now };
        var media = new MockSource { Id = "media", Priority = ActivityPriority.Media, IsActive = true, IsTransient = false, LastActivatedUtc = now };
        var hardware = new MockSource { Id = "hardware", Priority = ActivityPriority.Hardware, IsActive = true, IsTransient = false, LastActivatedUtc = now };

        // Test TimerAlert (100) vs Battery (90)
        var res1 = resolver.Resolve(new[] { battery, timerAlert }, now);
        Assert.Equal("timerAlert", res1.Primary?.Id);

        // Test Battery (90) vs Volume (80)
        var res2 = resolver.Resolve(new[] { volume, battery }, now);
        Assert.Equal("battery", res2.Primary?.Id);

        // Test Volume (80) vs Network (65)
        var res3 = resolver.Resolve(new[] { network, volume }, now);
        Assert.Equal("volume", res3.Primary?.Id);

        // Test Network (65) vs Device (60)
        var res4 = resolver.Resolve(new[] { device, network }, now);
        Assert.Equal("network", res4.Primary?.Id);

        // Test Device (60) vs Timer (50)
        var res5 = resolver.Resolve(new[] { timer, device }, now);
        Assert.Equal("device", res5.Primary?.Id);

        // Test Timer (50) vs Media (30) in single mode (Split mode disabled or tested)
        var res6 = resolver.Resolve(new[] { media, timer }, now);
        Assert.Equal("timer", res6.Primary?.Id);
        Assert.Equal("media", res6.Secondary?.Id);

        // Test Media (30) vs Hardware (10)
        var res7 = resolver.Resolve(new[] { hardware, media }, now);
        Assert.Equal("media", res7.Primary?.Id);
        Assert.Equal("hardware", res7.Secondary?.Id);
    }
}
