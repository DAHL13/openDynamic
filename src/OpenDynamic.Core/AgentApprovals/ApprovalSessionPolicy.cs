namespace OpenDynamic.Core.AgentApprovals;

/// <summary>
/// Pure domain policy governing an active approval session.
/// Enforces accidental-click grace period (600 ms), 90-second timeout,
/// safe fallback to AskNative (Golden Rule 12), and action authorization according to risk level.
/// </summary>
public sealed class ApprovalSessionPolicy
{
    public static readonly TimeSpan DefaultGracePeriod = TimeSpan.FromMilliseconds(600);
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(90);

    private readonly TimeProvider _timeProvider;
    private readonly object _syncLock = new();

    public ApprovalRequest Request { get; }
    public ApprovalPresentation Presentation { get; }
    public DateTimeOffset StartTimeUtc { get; }
    public TimeSpan GracePeriod { get; }
    public TimeSpan Timeout { get; }

    public bool IsExpanded { get; private set; }
    public bool IsResolved { get; private set; }
    public ApprovalResponse? Resolution { get; private set; }
    public string? ResolutionSource { get; private set; }

    public ApprovalSessionPolicy(
        ApprovalRequest request,
        TimeProvider? timeProvider = null,
        TimeSpan? gracePeriod = null,
        TimeSpan? timeout = null)
    {
        Request = request ?? throw new ArgumentNullException(nameof(request));
        _timeProvider = timeProvider ?? TimeProvider.System;
        GracePeriod = gracePeriod ?? DefaultGracePeriod;
        Timeout = timeout ?? DefaultTimeout;

        Presentation = ApprovalPresentation.Create(request);
        StartTimeUtc = _timeProvider.GetUtcNow();
    }

    /// <summary>
    /// Checks whether the anti-accidental click grace period is still active.
    /// </summary>
    public bool IsInGracePeriod(DateTimeOffset? now = null)
    {
        var current = now ?? _timeProvider.GetUtcNow();
        return (current - StartTimeUtc) < GracePeriod;
    }

    /// <summary>
    /// Gets remaining grace period duration. Returns <see cref="TimeSpan.Zero"/> if elapsed.
    /// </summary>
    public TimeSpan GetRemainingGracePeriod(DateTimeOffset? now = null)
    {
        var current = now ?? _timeProvider.GetUtcNow();
        var elapsed = current - StartTimeUtc;
        if (elapsed >= GracePeriod) return TimeSpan.Zero;
        return GracePeriod - elapsed;
    }

    /// <summary>
    /// Checks whether the approval session has exceeded its maximum timeout.
    /// </summary>
    public bool HasTimedOut(DateTimeOffset? now = null)
    {
        var current = now ?? _timeProvider.GetUtcNow();
        return (current - StartTimeUtc) >= Timeout;
    }

    /// <summary>
    /// Gets remaining session duration before automatic timeout to AskNative.
    /// </summary>
    public TimeSpan GetRemainingTime(DateTimeOffset? now = null)
    {
        var current = now ?? _timeProvider.GetUtcNow();
        var elapsed = current - StartTimeUtc;
        if (elapsed >= Timeout) return TimeSpan.Zero;
        return Timeout - elapsed;
    }

    /// <summary>
    /// Marks the request as visually expanded by the user or auto-expansion.
    /// </summary>
    public void MarkExpanded()
    {
        lock (_syncLock)
        {
            IsExpanded = true;
        }
    }

    /// <summary>
    /// Determines whether the user is authorized to click "Allow".
    /// Denied during grace period and when expanded review is required but not yet expanded.
    /// </summary>
    public bool CanAllow(DateTimeOffset? now = null)
    {
        lock (_syncLock)
        {
            if (IsResolved) return false;
            if (IsInGracePeriod(now)) return false;
            if (Presentation.RequiresExpandedReview && !IsExpanded) return false;
            return true;
        }
    }

    /// <summary>
    /// Determines whether the user is authorized to approve via keyboard hotkey.
    /// Strictly forbidden for High-risk commands (Golden Rule 12 &amp; 13).
    /// </summary>
    public bool CanAllowViaHotkey(DateTimeOffset? now = null)
    {
        lock (_syncLock)
        {
            if (Presentation.Risk == RiskLevel.High) return false;
            return CanAllow(now);
        }
    }

    /// <summary>
    /// Determines whether the user is authorized to click "Deny".
    /// Denied only during grace period or if already resolved.
    /// </summary>
    public bool CanDeny(DateTimeOffset? now = null)
    {
        lock (_syncLock)
        {
            if (IsResolved) return false;
            if (IsInGracePeriod(now)) return false;
            return true;
        }
    }

    /// <summary>
    /// Tries to approve the request, returning true if allowed by policy.
    /// </summary>
    public bool TryAllow(out ApprovalResponse response, DateTimeOffset? now = null)
    {
        lock (_syncLock)
        {
            if (!CanAllow(now))
            {
                response = ApprovalResponse.AskNative("Acción no autorizada en este estado");
                return false;
            }

            IsResolved = true;
            ResolutionSource = "user_allow";
            Resolution = ApprovalResponse.Allow();
            response = Resolution;
            return true;
        }
    }

    /// <summary>
    /// Tries to deny the request with a specified reason.
    /// </summary>
    public bool TryDeny(string reason, out ApprovalResponse response, DateTimeOffset? now = null)
    {
        lock (_syncLock)
        {
            if (!CanDeny(now))
            {
                response = ApprovalResponse.AskNative("Acción no autorizada en este estado");
                return false;
            }

            IsResolved = true;
            ResolutionSource = "user_deny";
            Resolution = ApprovalResponse.Deny(reason);
            response = Resolution;
            return true;
        }
    }

    /// <summary>
    /// User explicitly chooses to delegate to Antigravity's native dialog prompt.
    /// </summary>
    public ApprovalResponse DecideInAntigravity(string? reason = "Decidir en Antigravity")
    {
        lock (_syncLock)
        {
            if (IsResolved && Resolution != null) return Resolution;

            IsResolved = true;
            ResolutionSource = "user_decide_in_antigravity";
            Resolution = ApprovalResponse.AskNative(reason);
            return Resolution;
        }
    }

    /// <summary>
    /// Resolves the session due to timeout, falling back safely to AskNative.
    /// </summary>
    public ApprovalResponse Expire(DateTimeOffset? now = null)
    {
        lock (_syncLock)
        {
            if (IsResolved && Resolution != null) return Resolution;

            IsResolved = true;
            ResolutionSource = "timeout";
            Resolution = ApprovalResponse.AskNative("Tiempo de espera agotado (90 s)");
            return Resolution;
        }
    }

    /// <summary>
    /// Resolves the session due to client hook disconnection.
    /// </summary>
    public ApprovalResponse CancelByClientDisconnect()
    {
        lock (_syncLock)
        {
            if (IsResolved && Resolution != null) return Resolution;

            IsResolved = true;
            ResolutionSource = "client_disconnected";
            Resolution = ApprovalResponse.AskNative("Cliente desconectado");
            return Resolution;
        }
    }

    /// <summary>
    /// Rejection triggered when the dynamic island is hidden, paused, in fullscreen, or disabled.
    /// Fails immediately and safely to AskNative.
    /// </summary>
    public ApprovalResponse RejectBySystemUnavailable(string reason)
    {
        lock (_syncLock)
        {
            if (IsResolved && Resolution != null) return Resolution;

            IsResolved = true;
            ResolutionSource = "system_unavailable";
            Resolution = ApprovalResponse.AskNative(reason);
            return Resolution;
        }
    }
}
