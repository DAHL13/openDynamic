using OpenDynamic.Core.Media.Gestures;
using OpenDynamic.Tests.Timer;
using Xunit;

namespace OpenDynamic.Tests.Media;

public class SwipeGestureDetectorTests
{
    [Fact]
    public void ProcessWheelDelta_BelowThreshold_ReturnsNone()
    {
        var fakeTime = new FakeTimeProvider();
        var detector = new SwipeGestureDetector(fakeTime, threshold: 120.0);

        var r1 = detector.ProcessWheelDelta(40.0);
        var r2 = detector.ProcessWheelDelta(50.0);

        Assert.Equal(SwipeGestureAction.None, r1);
        Assert.Equal(SwipeGestureAction.None, r2);
        Assert.Equal(90.0, detector.AccumulatedDelta);
    }

    [Fact]
    public void ProcessWheelDelta_ExceedsPositiveThreshold_ReturnsNext()
    {
        var fakeTime = new FakeTimeProvider();
        var detector = new SwipeGestureDetector(fakeTime, threshold: 120.0);

        detector.ProcessWheelDelta(60.0);
        var result = detector.ProcessWheelDelta(65.0);

        Assert.Equal(SwipeGestureAction.Next, result);
        Assert.Equal(0.0, detector.AccumulatedDelta);
    }

    [Fact]
    public void ProcessWheelDelta_ExceedsNegativeThreshold_ReturnsPrevious()
    {
        var fakeTime = new FakeTimeProvider();
        var detector = new SwipeGestureDetector(fakeTime, threshold: 120.0);

        detector.ProcessWheelDelta(-60.0);
        var result = detector.ProcessWheelDelta(-70.0);

        Assert.Equal(SwipeGestureAction.Previous, result);
        Assert.Equal(0.0, detector.AccumulatedDelta);
    }

    [Fact]
    public void ProcessWheelDelta_InCooldown_SuppressesSecondTrigger()
    {
        var fakeTime = new FakeTimeProvider();
        var detector = new SwipeGestureDetector(fakeTime, threshold: 120.0, cooldown: TimeSpan.FromMilliseconds(400));

        // First trigger
        var first = detector.ProcessWheelDelta(130.0);
        Assert.Equal(SwipeGestureAction.Next, first);
        Assert.True(detector.IsInCooldown());

        // Immediate subsequent bursts (e.g. inertia scroll) within 400ms must be suppressed
        fakeTime.Advance(TimeSpan.FromMilliseconds(150));
        var suppressed1 = detector.ProcessWheelDelta(150.0);
        Assert.Equal(SwipeGestureAction.None, suppressed1);
        Assert.Equal(0.0, detector.AccumulatedDelta);

        fakeTime.Advance(TimeSpan.FromMilliseconds(150)); // Total 300ms < 400ms
        var suppressed2 = detector.ProcessWheelDelta(200.0);
        Assert.Equal(SwipeGestureAction.None, suppressed2);

        // Advance past cooldown (Total 450ms > 400ms)
        fakeTime.Advance(TimeSpan.FromMilliseconds(150));
        Assert.False(detector.IsInCooldown());

        var second = detector.ProcessWheelDelta(130.0);
        Assert.Equal(SwipeGestureAction.Next, second);
    }

    [Fact]
    public void ProcessDragDelta_SwipingLeft_ReturnsNext()
    {
        var fakeTime = new FakeTimeProvider();
        var detector = new SwipeGestureDetector(fakeTime, threshold: 40.0);

        // Swiping left means deltaX < 0
        detector.ProcessDragDelta(-20.0);
        var result = detector.ProcessDragDelta(-25.0);

        Assert.Equal(SwipeGestureAction.Next, result);
        Assert.Equal(0.0, detector.AccumulatedDelta);
    }

    [Fact]
    public void ProcessDragDelta_SwipingRight_ReturnsPrevious()
    {
        var fakeTime = new FakeTimeProvider();
        var detector = new SwipeGestureDetector(fakeTime, threshold: 40.0);

        // Swiping right means deltaX > 0
        detector.ProcessDragDelta(20.0);
        var result = detector.ProcessDragDelta(25.0);

        Assert.Equal(SwipeGestureAction.Previous, result);
        Assert.Equal(0.0, detector.AccumulatedDelta);
    }

    [Fact]
    public void DirectionFlip_ResetsAccumulation()
    {
        var fakeTime = new FakeTimeProvider();
        var detector = new SwipeGestureDetector(fakeTime, threshold: 100.0);

        detector.ProcessWheelDelta(60.0);
        Assert.Equal(60.0, detector.AccumulatedDelta);

        // Opposite direction arrives
        detector.ProcessWheelDelta(-30.0);
        // Previous accumulation was reset and new accumulation is -30.0
        Assert.Equal(-30.0, detector.AccumulatedDelta);
    }

    [Fact]
    public void Reset_ClearsAccumulation()
    {
        var fakeTime = new FakeTimeProvider();
        var detector = new SwipeGestureDetector(fakeTime, threshold: 100.0);

        detector.ProcessWheelDelta(80.0);
        Assert.Equal(80.0, detector.AccumulatedDelta);

        detector.Reset();
        Assert.Equal(0.0, detector.AccumulatedDelta);
    }
}
