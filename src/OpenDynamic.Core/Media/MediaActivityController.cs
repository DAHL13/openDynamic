using OpenDynamic.Core.Settings;

namespace OpenDynamic.Core.Media;

/// <summary>
/// Domain coordinator managing media activity states, pause grace periods, and lifecycle rules.
/// Pure net10.0 implementation free of UI/WPF types to enable isolated unit testing (Golden Rule 5).
/// </summary>
public sealed class MediaActivityController : IDisposable
{
    private readonly AppSettings _settings;
    private readonly Func<DateTimeOffset> _timeProvider;
    private IMediaSession? _currentSession;
    private bool _isDisposed;

    public bool IsActive { get; private set; }
    public MediaPlaybackStatus PlaybackStatus { get; private set; } = MediaPlaybackStatus.Closed;
    public IMediaSession? CurrentSession => _currentSession;
    public DateTimeOffset? PauseGraceExpirationUtc { get; private set; }
    public bool IsGraceTimerRunning => PauseGraceExpirationUtc.HasValue;

    public event EventHandler? StateChanged;

    public MediaActivityController(AppSettings? settings = null, Func<DateTimeOffset>? timeProvider = null)
    {
        _settings = settings ?? new AppSettings();
        _timeProvider = timeProvider ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Attaches and observes a media session.
    /// </summary>
    public void AttachSession(IMediaSession? session)
    {
        if (ReferenceEquals(_currentSession, session)) return;

        DetachSession();

        _currentSession = session;
        if (_currentSession != null)
        {
            _currentSession.PlaybackInfoChanged += OnSessionPlaybackInfoChanged;
            _currentSession.SessionClosed += OnSessionClosed;
            UpdateFromCurrentSession();
        }
        else
        {
            HandleSessionTerminated();
        }
    }

    /// <summary>
    /// Detaches from the active media session and marks activity inactive.
    /// </summary>
    public void DetachSession()
    {
        if (_currentSession != null)
        {
            _currentSession.PlaybackInfoChanged -= OnSessionPlaybackInfoChanged;
            _currentSession.SessionClosed -= OnSessionClosed;
            _currentSession = null;
        }

        HandleSessionTerminated();
    }

    private void OnSessionPlaybackInfoChanged(object? sender, EventArgs e)
    {
        UpdateFromCurrentSession();
    }

    private void OnSessionClosed(object? sender, EventArgs e)
    {
        DetachSession();
    }

    private void UpdateFromCurrentSession()
    {
        if (_currentSession == null)
        {
            HandleSessionTerminated();
            return;
        }

        ProcessPlaybackStatusChange(_currentSession.PlaybackStatus);
    }

    /// <summary>
    /// Processes a playback status transition following the 10-second pause grace period rule.
    /// </summary>
    public void ProcessPlaybackStatusChange(MediaPlaybackStatus newStatus)
    {
        PlaybackStatus = newStatus;

        switch (newStatus)
        {
            case MediaPlaybackStatus.Playing:
                // Cancel pause grace timer immediately and become active
                PauseGraceExpirationUtc = null;
                IsActive = true;
                NotifyStateChanged();
                break;

            case MediaPlaybackStatus.Paused:
                if (IsActive)
                {
                    // Start grace period countdown if currently active
                    var graceDuration = TimeSpan.FromSeconds(Math.Max(1, _settings.MediaPauseGracePeriodSeconds));
                    PauseGraceExpirationUtc = _timeProvider().Add(graceDuration);
                    NotifyStateChanged();
                }
                break;

            case MediaPlaybackStatus.Stopped:
            case MediaPlaybackStatus.Closed:
                HandleSessionTerminated();
                break;
        }
    }

    /// <summary>
    /// Evaluates if the pause grace period timer has expired against the given or current timestamp.
    /// </summary>
    /// <returns>True if grace period expired and activity was deactivated; otherwise false.</returns>
    public bool CheckGraceTimerExpiration(DateTimeOffset? nowUtc = null)
    {
        if (!PauseGraceExpirationUtc.HasValue)
        {
            return false;
        }

        var current = nowUtc ?? _timeProvider();
        if (current >= PauseGraceExpirationUtc.Value)
        {
            PauseGraceExpirationUtc = null;
            IsActive = false;
            NotifyStateChanged();
            return true;
        }

        return false;
    }

    private void HandleSessionTerminated()
    {
        PauseGraceExpirationUtc = null;
        PlaybackStatus = MediaPlaybackStatus.Closed;

        if (IsActive)
        {
            IsActive = false;
            NotifyStateChanged();
        }
    }

    private void NotifyStateChanged()
    {
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        DetachSession();
        StateChanged = null;
    }
}
