using OpenDynamic.Core.State;
using OpenDynamic.Core.Widgets;

namespace OpenDynamic.Tests.Media;

public class PriorityResolverMediaTests
{
    private readonly PriorityResolver _resolver = new();

    private sealed class MockMediaActivitySource : IActivitySource
    {
        public string Id { get; init; } = "media";
        public int Priority { get; init; } = ActivityPriority.Media; // 30
        public bool IsActive { get; set; } = true;
        public bool IsTransient { get; set; } = false;
        public DateTimeOffset? LastActivatedUtc { get; set; }
        public TimeSpan? TransientDuration { get; set; }
        public IslandActivity? CurrentActivity { get; set; }

        public event EventHandler? Changed;
        public void NotifyChanged() => Changed?.Invoke(this, EventArgs.Empty);
    }

    private sealed class MockOtherActivitySource : IActivitySource
    {
        public string Id { get; init; } = "other";
        public int Priority { get; init; } = ActivityPriority.Normal; // 50
        public bool IsActive { get; set; } = true;
        public bool IsTransient { get; set; } = false;
        public DateTimeOffset? LastActivatedUtc { get; set; }
        public TimeSpan? TransientDuration { get; set; }
        public IslandActivity? CurrentActivity { get; set; }

        public event EventHandler? Changed;
        public void NotifyChanged() => Changed?.Invoke(this, EventArgs.Empty);
    }

    [Fact]
    public void Resolve_OnlyMediaActive_PresentsMediaInCompactMode()
    {
        var mediaSource = new MockMediaActivitySource { IsActive = true };

        var result = _resolver.Resolve(new[] { mediaSource });

        Assert.NotNull(result.Primary);
        Assert.Equal("media", result.Primary.Id);
        Assert.Null(result.Secondary);
        Assert.Equal(IslandState.Compact, result.SuggestedState);
    }

    [Fact]
    public void Resolve_MediaAndHigherPriority_PresentsHigherAsPrimaryAndMediaAsSecondaryInSplit()
    {
        var mediaSource = new MockMediaActivitySource { IsActive = true, Priority = ActivityPriority.Media }; // 30
        var timerSource = new MockOtherActivitySource { Id = "timer", IsActive = true, Priority = ActivityPriority.Normal }; // 50

        var result = _resolver.Resolve(new IActivitySource[] { mediaSource, timerSource });

        Assert.NotNull(result.Primary);
        Assert.Equal("timer", result.Primary.Id);
        Assert.NotNull(result.Secondary);
        Assert.Equal("media", result.Secondary.Id);
        Assert.True(result.IsSplit);
        Assert.Equal(IslandState.Split, result.SuggestedState);
    }

    [Fact]
    public void Resolve_MediaAndLowerPriority_PresentsMediaAsPrimary()
    {
        var mediaSource = new MockMediaActivitySource { IsActive = true, Priority = ActivityPriority.Media }; // 30
        var lowSource = new MockOtherActivitySource { Id = "low-task", IsActive = true, Priority = ActivityPriority.Low }; // 10

        var result = _resolver.Resolve(new IActivitySource[] { mediaSource, lowSource });

        Assert.NotNull(result.Primary);
        Assert.Equal("media", result.Primary.Id);
        Assert.NotNull(result.Secondary);
        Assert.Equal("low-task", result.Secondary.Id);
        Assert.True(result.IsSplit);
        Assert.Equal(IslandState.Split, result.SuggestedState);
    }

    [Fact]
    public void Resolve_TransientAlertInterruptsMedia_ExclusiveCompactMode()
    {
        var mediaSource = new MockMediaActivitySource { IsActive = true, Priority = ActivityPriority.Media };
        var transientAlert = new MockOtherActivitySource
        {
            Id = "volume-alert",
            IsActive = true,
            Priority = ActivityPriority.TransientNotice, // 200
            IsTransient = true,
            TransientDuration = TimeSpan.FromSeconds(2),
            LastActivatedUtc = DateTimeOffset.UtcNow
        };

        var result = _resolver.Resolve(new IActivitySource[] { mediaSource, transientAlert });

        Assert.NotNull(result.Primary);
        Assert.Equal("volume-alert", result.Primary.Id);
        Assert.Null(result.Secondary); // Transient alert preempts secondary in compact
        Assert.Equal(IslandState.Compact, result.SuggestedState);
    }
}
