namespace OpenDynamic.Core.Network;

/// <summary>
/// Pure domain policy determining when network connectivity transitions warrant an alert.
/// Applies debounce (~1s) for event bursts, cooldown (minimum 5s between identical alerts),
/// startup suppression, and 10-second suppression following system resume from sleep.
/// Adheres strictly to Golden Rule 5 (zero Windows/WPF dependencies, deterministic TimeProvider).
/// </summary>
public sealed class NetworkAlertPolicy : IDisposable
{
    private readonly object _syncLock = new();
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _debounceDuration;
    private readonly TimeSpan _cooldownDuration;
    private readonly TimeSpan _suspendSuppressionDuration;

    private readonly ITimer _debounceTimer;

    private const int MaxAlertHistoryEntries = 128;

    private bool _isInitialized;
    private NetworkSnapshot _currentSnapshot = NetworkSnapshot.Disconnected;
    private NetworkSnapshot? _lastEmittedSnapshot;
    private readonly Dictionary<string, DateTimeOffset> _lastEmittedAlertTimes = new();
    private DateTimeOffset _suppressUntilUtc = DateTimeOffset.MinValue;
    private NetworkSnapshot? _pendingSnapshot;
    private bool _isDisposed;

    /// <summary>
    /// Event triggered when a consolidated, debounced network state change warrants a user alert.
    /// </summary>
    public event EventHandler<NetworkSnapshot>? AlertTriggered;

    public NetworkSnapshot CurrentSnapshot
    {
        get
        {
            lock (_syncLock) return _currentSnapshot;
        }
    }

    public NetworkSnapshot? LastEmittedSnapshot
    {
        get
        {
            lock (_syncLock) return _lastEmittedSnapshot;
        }
    }

    public bool IsInitialized
    {
        get
        {
            lock (_syncLock) return _isInitialized;
        }
    }

    public bool IsInSuspensionSuppression
    {
        get
        {
            lock (_syncLock) return _timeProvider.GetUtcNow() < _suppressUntilUtc;
        }
    }

    public bool HasPendingDebounce
    {
        get
        {
            lock (_syncLock) return _pendingSnapshot.HasValue;
        }
    }

    public TimeSpan DebounceDuration => _debounceDuration;
    public TimeSpan CooldownDuration => _cooldownDuration;
    public TimeSpan SuspendSuppressionDuration => _suspendSuppressionDuration;

    public NetworkAlertPolicy(
        TimeProvider? timeProvider = null,
        TimeSpan? debounceDuration = null,
        TimeSpan? cooldownDuration = null,
        TimeSpan? suspendSuppressionDuration = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _debounceDuration = debounceDuration ?? TimeSpan.FromSeconds(1.0);
        _cooldownDuration = cooldownDuration ?? TimeSpan.FromSeconds(5.0);
        _suspendSuppressionDuration = suspendSuppressionDuration ?? TimeSpan.FromSeconds(10.0);

        _debounceTimer = _timeProvider.CreateTimer(
            OnDebounceTimerElapsed,
            null,
            Timeout.InfiniteTimeSpan,
            Timeout.InfiniteTimeSpan);
    }

    /// <summary>
    /// Initializes baseline network status on startup without triggering an alert.
    /// </summary>
    public void Initialize(NetworkSnapshot initialSnapshot)
    {
        lock (_syncLock)
        {
            _currentSnapshot = initialSnapshot;
            _lastEmittedSnapshot = initialSnapshot;
            _lastEmittedAlertTimes[$"{(int)initialSnapshot.State}:{initialSnapshot.NetworkName ?? ""}"] = _timeProvider.GetUtcNow();
            _isInitialized = true;
            _pendingSnapshot = null;
            _debounceTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>
    /// Feeds a new network snapshot from the platform into the policy.
    /// </summary>
    public void ProcessSnapshot(NetworkSnapshot snapshot)
    {
        lock (_syncLock)
        {
            if (_isDisposed) return;

            _currentSnapshot = snapshot;

            if (!_isInitialized)
            {
                Initialize(snapshot);
                return;
            }

            var now = _timeProvider.GetUtcNow();

            // During suspension suppression window: update baseline quietly without alerting
            if (now < _suppressUntilUtc)
            {
                _lastEmittedSnapshot = snapshot;
                _pendingSnapshot = null;
                _debounceTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                return;
            }

            // If incoming snapshot matches the last emitted alert, cancel any transient debounce
            if (_lastEmittedSnapshot.HasValue && snapshot.Equals(_lastEmittedSnapshot.Value))
            {
                _pendingSnapshot = null;
                _debounceTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                return;
            }

            // A different state was detected: stage debounce
            _pendingSnapshot = snapshot;
            _debounceTimer.Change(_debounceDuration, Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>
    /// Notifies the policy that the host system has resumed from sleep or hibernation.
    /// Suppresses all network alerts for 10 seconds to avoid wakeup burst notifications.
    /// </summary>
    public void NotifyResumedFromSuspend()
    {
        lock (_syncLock)
        {
            var now = _timeProvider.GetUtcNow();
            _suppressUntilUtc = now + _suspendSuppressionDuration;
            _pendingSnapshot = null;
            _debounceTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>
    /// Notifies the policy that the system is entering suspension.
    /// Cancels any active debounce timers.
    /// </summary>
    public void NotifySuspended()
    {
        lock (_syncLock)
        {
            _pendingSnapshot = null;
            _debounceTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>
    /// Immediately flushes and evaluates any pending debounce snapshot.
    /// Primarily used for deterministic testing and clean shutdown.
    /// </summary>
    public void FlushDebounce()
    {
        NetworkSnapshot? toEmit = null;

        lock (_syncLock)
        {
            if (_isDisposed || !_pendingSnapshot.HasValue) return;

            toEmit = EvaluatePendingSnapshotLocked();
        }

        if (toEmit.HasValue)
        {
            AlertTriggered?.Invoke(this, toEmit.Value);
        }
    }

    private void OnDebounceTimerElapsed(object? state)
    {
        NetworkSnapshot? toEmit = null;

        lock (_syncLock)
        {
            if (_isDisposed || !_pendingSnapshot.HasValue) return;

            toEmit = EvaluatePendingSnapshotLocked();
        }

        if (toEmit.HasValue)
        {
            AlertTriggered?.Invoke(this, toEmit.Value);
        }
    }

    private NetworkSnapshot? EvaluatePendingSnapshotLocked()
    {
        if (!_pendingSnapshot.HasValue) return null;

        var pending = _pendingSnapshot.Value;
        _pendingSnapshot = null;
        _debounceTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

        var now = _timeProvider.GetUtcNow();

        // 1. Suspension suppression check
        if (now < _suppressUntilUtc)
        {
            _lastEmittedSnapshot = pending;
            return null;
        }

        // 2. Redundancy check against last emitted alert
        if (_lastEmittedSnapshot.HasValue && pending.Equals(_lastEmittedSnapshot.Value))
        {
            return null;
        }

        // 3. Cooldown check between identical alerts (minimum 5s between identical alerts)
        string alertKey = $"{(int)pending.State}:{pending.NetworkName ?? ""}";
        if (_lastEmittedAlertTimes.TryGetValue(alertKey, out var lastAlertTime) &&
            (now - lastAlertTime) < _cooldownDuration)
        {
            return null;
        }

        if (_lastEmittedAlertTimes.Count >= MaxAlertHistoryEntries)
        {
            _lastEmittedAlertTimes.Clear();
        }

        _lastEmittedAlertTimes[alertKey] = now;
        _lastEmittedSnapshot = pending;
        return pending;
    }

    public void Dispose()
    {
        lock (_syncLock)
        {
            if (_isDisposed) return;
            _isDisposed = true;

            _debounceTimer.Dispose();
            _pendingSnapshot = null;
            AlertTriggered = null;
        }
    }
}
