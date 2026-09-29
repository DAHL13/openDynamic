using OpenDynamic.Core.Media;
using OpenDynamic.Core.Settings;

namespace OpenDynamic.Tests.Media;

public class MediaActivityControllerTests
{
    private DateTimeOffset _currentTime = new(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);

    private MediaActivityController CreateController(int gracePeriodSeconds = 10)
    {
        var settings = new AppSettings { MediaPauseGracePeriodSeconds = gracePeriodSeconds };
        return new MediaActivityController(settings, () => _currentTime);
    }

    [Fact]
    public void InitialState_IsInactiveAndClosed()
    {
        using var controller = CreateController();

        Assert.False(controller.IsActive);
        Assert.Equal(MediaPlaybackStatus.Closed, controller.PlaybackStatus);
        Assert.False(controller.IsGraceTimerRunning);
    }

    [Fact]
    public void AttachSession_WhenPlaying_BecomesActiveImmediately()
    {
        using var controller = CreateController();
        using var session = new TestMediaSession { PlaybackStatus = MediaPlaybackStatus.Playing };

        controller.AttachSession(session);

        Assert.True(controller.IsActive);
        Assert.Equal(MediaPlaybackStatus.Playing, controller.PlaybackStatus);
        Assert.False(controller.IsGraceTimerRunning);
    }

    [Fact]
    public void WhenPaused_WhileActive_EntersGracePeriodOf10Seconds()
    {
        using var controller = CreateController(gracePeriodSeconds: 10);
        using var session = new TestMediaSession { PlaybackStatus = MediaPlaybackStatus.Playing };

        controller.AttachSession(session);
        Assert.True(controller.IsActive);

        // Transition: Playing -> Paused
        session.TriggerPlaybackInfoChanged(MediaPlaybackStatus.Paused);

        // Activity remains active during grace period
        Assert.True(controller.IsActive);
        Assert.Equal(MediaPlaybackStatus.Paused, controller.PlaybackStatus);
        Assert.True(controller.IsGraceTimerRunning);
        Assert.Equal(_currentTime.AddSeconds(10), controller.PauseGraceExpirationUtc);
    }

    [Fact]
    public void WhenPaused_AndGracePeriodExpires_BecomesInactive()
    {
        using var controller = CreateController(gracePeriodSeconds: 10);
        using var session = new TestMediaSession { PlaybackStatus = MediaPlaybackStatus.Playing };

        controller.AttachSession(session);
        session.TriggerPlaybackInfoChanged(MediaPlaybackStatus.Paused);

        Assert.True(controller.IsActive);

        // Advance time by 9 seconds: still active
        _currentTime = _currentTime.AddSeconds(9);
        var expiredBefore = controller.CheckGraceTimerExpiration(_currentTime);
        Assert.False(expiredBefore);
        Assert.True(controller.IsActive);

        // Advance time to 10 seconds: expires and deactivates
        _currentTime = _currentTime.AddSeconds(1);
        var expiredAt = controller.CheckGraceTimerExpiration(_currentTime);
        Assert.True(expiredAt);
        Assert.False(controller.IsActive);
        Assert.False(controller.IsGraceTimerRunning);
    }

    [Fact]
    public void WhenPaused_AndResumedBeforeGracePeriodExpires_CancelsGraceTimerAndRemainsActive()
    {
        using var controller = CreateController(gracePeriodSeconds: 10);
        using var session = new TestMediaSession { PlaybackStatus = MediaPlaybackStatus.Playing };

        controller.AttachSession(session);
        session.TriggerPlaybackInfoChanged(MediaPlaybackStatus.Paused);
        Assert.True(controller.IsGraceTimerRunning);

        // Advance 4 seconds and resume playback
        _currentTime = _currentTime.AddSeconds(4);
        session.TriggerPlaybackInfoChanged(MediaPlaybackStatus.Playing);

        // Grace timer cancelled, playback continues active
        Assert.True(controller.IsActive);
        Assert.False(controller.IsGraceTimerRunning);
        Assert.Null(controller.PauseGraceExpirationUtc);
        Assert.Equal(MediaPlaybackStatus.Playing, controller.PlaybackStatus);
    }

    [Fact]
    public void WhenPlayerCloses_ImmediatelyDeactivatesWithoutWaitingGracePeriod()
    {
        using var controller = CreateController(gracePeriodSeconds: 10);
        using var session = new TestMediaSession { PlaybackStatus = MediaPlaybackStatus.Playing };

        controller.AttachSession(session);
        Assert.True(controller.IsActive);

        // Session closed (player terminated)
        session.TriggerSessionClosed();

        Assert.False(controller.IsActive);
        Assert.False(controller.IsGraceTimerRunning);
        Assert.Null(controller.PauseGraceExpirationUtc);
    }

    [Fact]
    public void WhenPlayerStops_ImmediatelyDeactivates()
    {
        using var controller = CreateController(gracePeriodSeconds: 10);
        using var session = new TestMediaSession { PlaybackStatus = MediaPlaybackStatus.Playing };

        controller.AttachSession(session);
        Assert.True(controller.IsActive);

        session.TriggerPlaybackInfoChanged(MediaPlaybackStatus.Stopped);

        Assert.False(controller.IsActive);
        Assert.False(controller.IsGraceTimerRunning);
    }

    [Fact]
    public void DetachSession_ImmediatelyDeactivatesAndCleansUp()
    {
        using var controller = CreateController(gracePeriodSeconds: 10);
        using var session = new TestMediaSession { PlaybackStatus = MediaPlaybackStatus.Playing };

        controller.AttachSession(session);
        Assert.True(controller.IsActive);

        controller.DetachSession();

        Assert.False(controller.IsActive);
        Assert.Null(controller.CurrentSession);
        Assert.False(controller.IsGraceTimerRunning);
    }

    [Fact]
    public void StateChangedEvent_FiresOnStateTransitions()
    {
        using var controller = CreateController(gracePeriodSeconds: 10);
        using var session = new TestMediaSession { PlaybackStatus = MediaPlaybackStatus.Closed };

        int eventCount = 0;
        controller.StateChanged += (s, e) => eventCount++;

        controller.AttachSession(session);
        session.TriggerPlaybackInfoChanged(MediaPlaybackStatus.Playing);
        session.TriggerPlaybackInfoChanged(MediaPlaybackStatus.Paused);
        controller.CheckGraceTimerExpiration(_currentTime.AddSeconds(15));

        Assert.True(eventCount >= 3);
    }
}
