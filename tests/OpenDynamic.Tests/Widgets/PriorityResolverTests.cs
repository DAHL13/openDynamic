using OpenDynamic.Core.State;
using OpenDynamic.Core.Widgets;

namespace OpenDynamic.Tests.Widgets;

public class PriorityResolverTests
{
    private readonly PriorityResolver _resolver = new();

    private sealed class MockActivitySource : IActivitySource
    {
        public string Id { get; init; } = "mock";
        public int Priority { get; init; } = ActivityPriority.Normal;
        public bool IsActive { get; init; } = true;
        public bool IsTransient { get; init; } = false;
        public DateTimeOffset? LastActivatedUtc { get; init; }
        public TimeSpan? TransientDuration { get; init; }
        public IslandActivity? CurrentActivity { get; init; }

        public event EventHandler? Changed;

        public void NotifyChanged() => Changed?.Invoke(this, EventArgs.Empty);
    }

    [Fact]
    public void Resolve_EmptyList_ReturnsHiddenAndNoActivities()
    {
        var result = _resolver.Resolve(Array.Empty<IActivitySource>());

        Assert.Null(result.Primary);
        Assert.Null(result.Secondary);
        Assert.False(result.HasActiveActivity);
        Assert.False(result.IsSplit);
        Assert.Equal(IslandState.Hidden, result.SuggestedState);
    }

    [Fact]
    public void Resolve_NullList_ReturnsHiddenAndNoActivities()
    {
        var result = _resolver.Resolve(null);

        Assert.Null(result.Primary);
        Assert.Null(result.Secondary);
        Assert.False(result.HasActiveActivity);
        Assert.False(result.IsSplit);
        Assert.Equal(IslandState.Hidden, result.SuggestedState);
    }

    [Fact]
    public void Resolve_AllInactiveSources_ReturnsHidden()
    {
        var sources = new[]
        {
            new MockActivitySource { Id = "s1", Priority = 100, IsActive = false },
            new MockActivitySource { Id = "s2", Priority = 50, IsActive = false }
        };

        var result = _resolver.Resolve(sources);

        Assert.Null(result.Primary);
        Assert.Null(result.Secondary);
        Assert.False(result.HasActiveActivity);
        Assert.Equal(IslandState.Hidden, result.SuggestedState);
    }

    [Fact]
    public void Resolve_SingleActiveSource_ReturnsCompactWithPrimary()
    {
        var source = new MockActivitySource { Id = "media", Priority = 50, IsActive = true };

        var result = _resolver.Resolve(new[] { source });

        Assert.Same(source, result.Primary);
        Assert.Null(result.Secondary);
        Assert.True(result.HasActiveActivity);
        Assert.False(result.IsSplit);
        Assert.Equal(IslandState.Compact, result.SuggestedState);
    }

    [Fact]
    public void Resolve_TwoActiveSources_HigherPriorityBecomesPrimary_OtherBecomesSecondarySplit()
    {
        var lowPriority = new MockActivitySource { Id = "timer", Priority = 40, IsActive = true };
        var highPriority = new MockActivitySource { Id = "media", Priority = 70, IsActive = true };

        var result = _resolver.Resolve(new[] { lowPriority, highPriority });

        Assert.Same(highPriority, result.Primary);
        Assert.Same(lowPriority, result.Secondary);
        Assert.True(result.HasActiveActivity);
        Assert.True(result.IsSplit);
        Assert.Equal(IslandState.Split, result.SuggestedState);
    }

    [Fact]
    public void Resolve_MoreThanTwoActiveSources_SelectsTopTwoOnly()
    {
        var s1 = new MockActivitySource { Id = "s1", Priority = 10, IsActive = true };
        var s2 = new MockActivitySource { Id = "s2", Priority = 90, IsActive = true };
        var s3 = new MockActivitySource { Id = "s3", Priority = 50, IsActive = true };
        var s4 = new MockActivitySource { Id = "s4", Priority = 30, IsActive = true };

        var result = _resolver.Resolve(new[] { s1, s2, s3, s4 });

        Assert.Same(s2, result.Primary);   // 90
        Assert.Same(s3, result.Secondary); // 50
        Assert.True(result.IsSplit);
        Assert.Equal(IslandState.Split, result.SuggestedState);
    }

    [Fact]
    public void Resolve_TieByPriority_MoreRecentlyActivatedWins()
    {
        var baseTime = new DateTimeOffset(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);

        var older = new MockActivitySource
        {
            Id = "older",
            Priority = 50,
            IsActive = true,
            LastActivatedUtc = baseTime
        };

        var newer = new MockActivitySource
        {
            Id = "newer",
            Priority = 50,
            IsActive = true,
            LastActivatedUtc = baseTime.AddMinutes(5)
        };

        var result = _resolver.Resolve(new[] { older, newer });

        Assert.Same(newer, result.Primary);
        Assert.Same(older, result.Secondary);
    }

    [Fact]
    public void Resolve_TieByPriority_TimestampedSourceWinsOverNullTimestamp()
    {
        var unTimestamped = new MockActivitySource
        {
            Id = "no-time",
            Priority = 50,
            IsActive = true,
            LastActivatedUtc = null
        };

        var timestamped = new MockActivitySource
        {
            Id = "timed",
            Priority = 50,
            IsActive = true,
            LastActivatedUtc = DateTimeOffset.UtcNow
        };

        var result = _resolver.Resolve(new[] { unTimestamped, timestamped });

        Assert.Same(timestamped, result.Primary);
        Assert.Same(unTimestamped, result.Secondary);
    }

    [Fact]
    public void Resolve_TieByPriorityAndTimestamp_DeterministicOrderById()
    {
        var fixedTime = new DateTimeOffset(2026, 9, 28, 15, 0, 0, TimeSpan.Zero);

        var alpha = new MockActivitySource
        {
            Id = "alpha",
            Priority = 50,
            IsActive = true,
            LastActivatedUtc = fixedTime
        };

        var beta = new MockActivitySource
        {
            Id = "beta",
            Priority = 50,
            IsActive = true,
            LastActivatedUtc = fixedTime
        };

        // Regardless of insertion order, 'alpha' wins over 'beta' deterministically
        var result1 = _resolver.Resolve(new[] { beta, alpha });
        var result2 = _resolver.Resolve(new[] { alpha, beta });

        Assert.Same(alpha, result1.Primary);
        Assert.Same(beta, result1.Secondary);

        Assert.Same(alpha, result2.Primary);
        Assert.Same(beta, result2.Secondary);
    }

    [Fact]
    public void Resolve_TransientActivityPreempts_TakesExclusiveSpotlight()
    {
        var now = new DateTimeOffset(2026, 9, 28, 18, 0, 0, TimeSpan.Zero);

        var persistentMusic = new MockActivitySource
        {
            Id = "music",
            Priority = 50,
            IsActive = true
        };

        var persistentTimer = new MockActivitySource
        {
            Id = "timer",
            Priority = 60,
            IsActive = true
        };

        var transientNotice = new MockActivitySource
        {
            Id = "volume-notice",
            Priority = 200,
            IsActive = true,
            IsTransient = true,
            LastActivatedUtc = now,
            TransientDuration = TimeSpan.FromSeconds(3)
        };

        var result = _resolver.Resolve(new[] { persistentMusic, persistentTimer, transientNotice }, now);

        // Transient alert preempts: Primary = transientNotice, Secondary = null, Compact
        Assert.Same(transientNotice, result.Primary);
        Assert.Null(result.Secondary);
        Assert.False(result.IsSplit);
        Assert.Equal(IslandState.Compact, result.SuggestedState);
        Assert.Equal(now.AddSeconds(3), result.NextExpirationUtc);
    }

    [Fact]
    public void Resolve_TransientActivityExpired_RelinquishesControlToBackgroundSources()
    {
        var startTime = new DateTimeOffset(2026, 9, 28, 18, 0, 0, TimeSpan.Zero);

        var persistentMusic = new MockActivitySource
        {
            Id = "music",
            Priority = 50,
            IsActive = true
        };

        var persistentTimer = new MockActivitySource
        {
            Id = "timer",
            Priority = 60,
            IsActive = true
        };

        var transientNotice = new MockActivitySource
        {
            Id = "volume-notice",
            Priority = 200,
            IsActive = true,
            IsTransient = true,
            LastActivatedUtc = startTime,
            TransientDuration = TimeSpan.FromSeconds(3)
        };

        // Evaluate at 4 seconds later (expired!)
        var currentTime = startTime.AddSeconds(4);
        var result = _resolver.Resolve(new[] { persistentMusic, persistentTimer, transientNotice }, currentTime);

        // Control returned to persistent sources: Timer (60) and Music (50) in Split!
        Assert.Same(persistentTimer, result.Primary);
        Assert.Same(persistentMusic, result.Secondary);
        Assert.True(result.IsSplit);
        Assert.Equal(IslandState.Split, result.SuggestedState);
        Assert.Null(result.NextExpirationUtc);
    }

    [Fact]
    public void Resolve_TransientWithoutDuration_DoesNotAutoExpire()
    {
        var now = new DateTimeOffset(2026, 9, 28, 18, 0, 0, TimeSpan.Zero);

        var transientNotice = new MockActivitySource
        {
            Id = "prompt",
            Priority = 150,
            IsActive = true,
            IsTransient = true,
            LastActivatedUtc = now,
            TransientDuration = null // No auto expiration
        };

        var result = _resolver.Resolve(new[] { transientNotice }, now.AddHours(1));

        Assert.Same(transientNotice, result.Primary);
        Assert.Null(result.Secondary);
        Assert.Equal(IslandState.Compact, result.SuggestedState);
    }
}
