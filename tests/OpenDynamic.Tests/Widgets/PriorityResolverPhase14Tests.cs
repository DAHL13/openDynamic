using OpenDynamic.Core.State;
using OpenDynamic.Core.Widgets;
using Xunit;

namespace OpenDynamic.Tests.Widgets;

public sealed class PriorityResolverPhase14Tests
{
    private sealed class MockSource : IActivitySource
    {
        public string Id { get; set; } = "";
        public int Priority { get; set; }
        public bool IsActive { get; set; } = true;
        public bool IsTransient { get; set; }
        public DateTimeOffset? LastActivatedUtc { get; set; }
        public TimeSpan? TransientDuration { get; set; }
        public IslandActivity? CurrentActivity => null;
#pragma warning disable CS0067
        public event EventHandler? Changed;
#pragma warning restore CS0067
    }

    [Fact]
    public void Device_Priority60_WinsOver_Clipboard_Priority55()
    {
        var resolver = new PriorityResolver();
        var now = DateTimeOffset.UtcNow;

        var device = new MockSource
        {
            Id = "device",
            Priority = ActivityPriority.Device, // 60
            IsActive = true,
            LastActivatedUtc = now.AddSeconds(-5)
        };

        var clipboard = new MockSource
        {
            Id = "clipboard",
            Priority = ActivityPriority.Clipboard, // 55
            IsActive = true,
            LastActivatedUtc = now.AddSeconds(-2)
        };

        var result = resolver.Resolve(new[] { clipboard, device }, now);

        Assert.Equal("device", result.Primary?.Id);
        Assert.Equal("clipboard", result.Secondary?.Id);
        Assert.Equal(IslandState.Split, result.SuggestedState);
    }

    [Fact]
    public void Clipboard_Priority55_WinsOver_Timer_Priority50()
    {
        var resolver = new PriorityResolver();
        var now = DateTimeOffset.UtcNow;

        var clipboard = new MockSource
        {
            Id = "clipboard",
            Priority = ActivityPriority.Clipboard, // 55
            IsActive = true,
            LastActivatedUtc = now.AddSeconds(-10)
        };

        var timer = new MockSource
        {
            Id = "timer",
            Priority = ActivityPriority.Timer, // 50
            IsActive = true,
            LastActivatedUtc = now.AddSeconds(-1)
        };

        var result = resolver.Resolve(new[] { timer, clipboard }, now);

        Assert.Equal("clipboard", result.Primary?.Id);
        Assert.Equal("timer", result.Secondary?.Id);
        Assert.Equal(IslandState.Split, result.SuggestedState);
    }

    [Fact]
    public void Clipboard_Priority55_WinsOver_Stopwatch_Priority45()
    {
        var resolver = new PriorityResolver();
        var now = DateTimeOffset.UtcNow;

        var clipboard = new MockSource
        {
            Id = "clipboard",
            Priority = ActivityPriority.Clipboard, // 55
            IsActive = true,
            LastActivatedUtc = now.AddSeconds(-10)
        };

        var stopwatch = new MockSource
        {
            Id = "stopwatch",
            Priority = ActivityPriority.Stopwatch, // 45
            IsActive = true,
            LastActivatedUtc = now.AddSeconds(-1)
        };

        var result = resolver.Resolve(new[] { stopwatch, clipboard }, now);

        Assert.Equal("clipboard", result.Primary?.Id);
        Assert.Equal("stopwatch", result.Secondary?.Id);
        Assert.Equal(IslandState.Split, result.SuggestedState);
    }

    [Fact]
    public void TransientClipboard_TakesPrimaryPreemption_WhenTransientNoticeFired()
    {
        var resolver = new PriorityResolver();
        var now = DateTimeOffset.UtcNow;

        var transientClipboard = new MockSource
        {
            Id = "clipboard",
            Priority = ActivityPriority.Clipboard, // 55
            IsActive = true,
            IsTransient = true,
            LastActivatedUtc = now,
            TransientDuration = TimeSpan.FromSeconds(2.0)
        };

        var timer = new MockSource
        {
            Id = "timer",
            Priority = ActivityPriority.Timer, // 50
            IsActive = true,
            LastActivatedUtc = now.AddMinutes(-5)
        };

        var result = resolver.Resolve(new[] { timer, transientClipboard }, now);

        Assert.Equal("clipboard", result.Primary?.Id);
        Assert.Null(result.Secondary);
        Assert.Equal(IslandState.Compact, result.SuggestedState);
    }

    [Fact]
    public void DeviceTransient_Priority60_Preempts_ClipboardTransient_Priority55()
    {
        var resolver = new PriorityResolver();
        var now = DateTimeOffset.UtcNow;

        var device = new MockSource
        {
            Id = "device",
            Priority = ActivityPriority.Device, // 60
            IsActive = true,
            IsTransient = true,
            LastActivatedUtc = now,
            TransientDuration = TimeSpan.FromSeconds(3.0)
        };

        var clipboard = new MockSource
        {
            Id = "clipboard",
            Priority = ActivityPriority.Clipboard, // 55
            IsActive = true,
            IsTransient = true,
            LastActivatedUtc = now.AddSeconds(-1),
            TransientDuration = TimeSpan.FromSeconds(2.0)
        };

        var result = resolver.Resolve(new[] { clipboard, device }, now);

        Assert.Equal("device", result.Primary?.Id);
        Assert.Null(result.Secondary);
        Assert.Equal(IslandState.Compact, result.SuggestedState);
    }

    [Fact]
    public void ConsolidatedPriorityHierarchy_MatchesEstablishedContract()
    {
        Assert.True(ActivityPriority.TimerAlert > ActivityPriority.Battery);
        Assert.True(ActivityPriority.Battery > ActivityPriority.Volume);
        Assert.True(ActivityPriority.Volume > ActivityPriority.Network);
        Assert.True(ActivityPriority.Network > ActivityPriority.Device);
        Assert.True(ActivityPriority.Device > ActivityPriority.Clipboard);
        Assert.True(ActivityPriority.Clipboard > ActivityPriority.Timer);
        Assert.True(ActivityPriority.Timer > ActivityPriority.Stopwatch);
        Assert.True(ActivityPriority.Stopwatch > ActivityPriority.Media);
        Assert.True(ActivityPriority.Media > ActivityPriority.Hardware);

        Assert.Equal(100, ActivityPriority.TimerAlert);
        Assert.Equal(90, ActivityPriority.Battery);
        Assert.Equal(80, ActivityPriority.Volume);
        Assert.Equal(65, ActivityPriority.Network);
        Assert.Equal(60, ActivityPriority.Device);
        Assert.Equal(55, ActivityPriority.Clipboard);
        Assert.Equal(50, ActivityPriority.Timer);
        Assert.Equal(45, ActivityPriority.Stopwatch);
        Assert.Equal(30, ActivityPriority.Media);
        Assert.Equal(10, ActivityPriority.Hardware);
    }

    [Fact]
    public void ExpandedClipboard_WithPausedTransient_IsNotDiscardedByPriorityResolver_EvenAfterInitialDuration()
    {
        var resolver = new PriorityResolver();
        var startTime = DateTimeOffset.UtcNow;

        // Simulate widget after OnExpand(): IsTransient cleared to false, TransientDuration set to null
        var expandedClipboard = new MockSource
        {
            Id = "clipboard",
            Priority = ActivityPriority.Clipboard, // 55
            IsActive = true,
            IsTransient = false,
            LastActivatedUtc = startTime,
            TransientDuration = null
        };

        // Evaluate 5 seconds later (past the original 2.0s transient duration)
        var futureTime = startTime.AddSeconds(5);
        var result = resolver.Resolve(new[] { expandedClipboard }, futureTime);

        Assert.NotNull(result.Primary);
        Assert.Equal("clipboard", result.Primary.Id);
        Assert.Null(result.NextExpirationUtc);
    }

    [Fact]
    public void ExpandedClipboard_WinsOver_LowerPriorityPersistentWidget_Indefinitely()
    {
        var resolver = new PriorityResolver();
        var startTime = DateTimeOffset.UtcNow;

        var expandedClipboard = new MockSource
        {
            Id = "clipboard",
            Priority = ActivityPriority.Clipboard, // 55
            IsActive = true,
            IsTransient = false,
            LastActivatedUtc = startTime
        };

        var persistentTimer = new MockSource
        {
            Id = "timer",
            Priority = ActivityPriority.Timer, // 50
            IsActive = true,
            IsTransient = false,
            LastActivatedUtc = startTime.AddMinutes(-2)
        };

        var futureTime = startTime.AddSeconds(30);
        var result = resolver.Resolve(new[] { persistentTimer, expandedClipboard }, futureTime);

        Assert.Equal("clipboard", result.Primary?.Id);
        Assert.Equal("timer", result.Secondary?.Id);
        Assert.Equal(IslandState.Split, result.SuggestedState);
        Assert.Null(result.NextExpirationUtc);
    }

    [Fact]
    public void ExpandedClipboard_TransitionsFromTransientToPersistent_ClearsNextExpirationUtc()
    {
        var resolver = new PriorityResolver();
        var startTime = DateTimeOffset.UtcNow;

        // Step 1: Initial transient state (2.0 seconds)
        var source = new MockSource
        {
            Id = "clipboard",
            Priority = ActivityPriority.Clipboard,
            IsActive = true,
            IsTransient = true,
            LastActivatedUtc = startTime,
            TransientDuration = TimeSpan.FromSeconds(2.0)
        };

        var initialResult = resolver.Resolve(new[] { source }, startTime.AddSeconds(0.5));
        Assert.NotNull(initialResult.Primary);
        Assert.NotNull(initialResult.NextExpirationUtc);
        Assert.Equal(startTime.AddSeconds(2.0), initialResult.NextExpirationUtc.Value);

        // Step 2: User expands notch -> OnExpand pauses transient state
        source.IsTransient = false;
        source.TransientDuration = null;

        var expandedResult = resolver.Resolve(new[] { source }, startTime.AddSeconds(1.0));
        Assert.NotNull(expandedResult.Primary);
        Assert.Equal("clipboard", expandedResult.Primary.Id);
        Assert.Null(expandedResult.NextExpirationUtc);
    }

    [Fact]
    public void ExpandedClipboard_ExpiredTransientDuration_IsNotDiscardedWhenIsTransientIsFalse()
    {
        var resolver = new PriorityResolver();
        var startTime = DateTimeOffset.UtcNow;

        var expandedSource = new MockSource
        {
            Id = "clipboard",
            Priority = ActivityPriority.Clipboard,
            IsActive = true,
            IsTransient = false,
            LastActivatedUtc = startTime,
            TransientDuration = null
        };

        // Evaluate 10 minutes later: must never be discarded as expired
        var tenMinutesLater = startTime.AddMinutes(10);
        var result = resolver.Resolve(new[] { expandedSource }, tenMinutesLater);

        Assert.NotNull(result.Primary);
        Assert.Equal("clipboard", result.Primary.Id);
        Assert.Null(result.NextExpirationUtc);
    }
}
