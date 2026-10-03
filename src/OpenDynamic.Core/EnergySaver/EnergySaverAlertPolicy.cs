namespace OpenDynamic.Core.EnergySaver;

/// <summary>
/// Pure domain policy governing when Windows Energy Saver state transitions warrant a user alert.
/// Applies startup suppression (never alerts on initial app load), suspension suppression
/// (10 seconds quiet window after resuming from sleep/hibernation), and cooldown (minimum 5s between alerts).
/// Adheres strictly to Golden Rule 5 (pure logic in Core, zero Windows/WPF dependencies, deterministic TimeProvider).
/// </summary>
public sealed class EnergySaverAlertPolicy
{
    private readonly object _syncLock = new();
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _cooldownDuration;
    private readonly TimeSpan _suspendSuppressionDuration;

    private bool _isInitialized;
    private EnergySaverState _currentState = EnergySaverState.Unknown;
    private EnergySaverState? _lastEmittedState;
    private DateTimeOffset _lastAlertTimeUtc = DateTimeOffset.MinValue;
    private DateTimeOffset _suppressUntilUtc = DateTimeOffset.MinValue;

    /// <summary>
    /// Event triggered when a validated, debounced energy saver state change warrants a visual island notice.
    /// </summary>
    public event EventHandler<EnergySaverState>? AlertTriggered;

    public EnergySaverState CurrentState
    {
        get
        {
            lock (_syncLock) return _currentState;
        }
    }

    public EnergySaverState? LastEmittedState
    {
        get
        {
            lock (_syncLock) return _lastEmittedState;
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

    public TimeSpan CooldownDuration => _cooldownDuration;
    public TimeSpan SuspendSuppressionDuration => _suspendSuppressionDuration;

    public EnergySaverAlertPolicy(
        TimeProvider? timeProvider = null,
        TimeSpan? cooldownDuration = null,
        TimeSpan? suspendSuppressionDuration = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _cooldownDuration = cooldownDuration ?? TimeSpan.FromSeconds(5.0);
        _suspendSuppressionDuration = suspendSuppressionDuration ?? TimeSpan.FromSeconds(10.0);
    }

    /// <summary>
    /// Establishes the baseline energy saver status on startup without triggering an alert.
    /// </summary>
    public void Initialize(EnergySaverState initialState)
    {
        lock (_syncLock)
        {
            _currentState = initialState;
            _lastEmittedState = initialState;
            _isInitialized = true;
        }
    }

    /// <summary>
    /// Feeds a new energy saver state snapshot into the policy.
    /// </summary>
    public void ProcessState(EnergySaverState newState)
    {
        bool shouldEmit = false;
        EnergySaverState stateToEmit = EnergySaverState.Unknown;

        lock (_syncLock)
        {
            if (!_isInitialized)
            {
                Initialize(newState);
                return;
            }

            _currentState = newState;

            // Devices without battery or unsupported platforms never emit alerts
            if (newState == EnergySaverState.Unknown || newState == EnergySaverState.NotSupported)
            {
                _lastEmittedState = newState;
                return;
            }

            var now = _timeProvider.GetUtcNow();

            // Suppress alerts during post-wake recovery window (10s)
            if (now < _suppressUntilUtc)
            {
                _lastEmittedState = newState;
                return;
            }

            // State has not changed from the last alerted state
            if (_lastEmittedState.HasValue && newState == _lastEmittedState.Value)
            {
                return;
            }

            // Respect cooldown period between alerts
            if (_lastAlertTimeUtc != DateTimeOffset.MinValue && (now - _lastAlertTimeUtc) < _cooldownDuration)
            {
                return;
            }

            if (newState == EnergySaverState.On || newState == EnergySaverState.Off)
            {
                _lastEmittedState = newState;
                _lastAlertTimeUtc = now;
                shouldEmit = true;
                stateToEmit = newState;
            }
        }

        if (shouldEmit)
        {
            AlertTriggered?.Invoke(this, stateToEmit);
        }
    }

    /// <summary>
    /// Notifies the policy that the host system is suspending (sleep/hibernation).
    /// </summary>
    public void NotifySuspended()
    {
        // No-op or clear transient state
    }

    /// <summary>
    /// Notifies the policy that the host system has resumed from sleep.
    /// Suppresses energy saver alerts for 10 seconds to avoid spurious startup flurries.
    /// </summary>
    public void NotifyResumedFromSuspend()
    {
        lock (_syncLock)
        {
            _suppressUntilUtc = _timeProvider.GetUtcNow() + _suspendSuppressionDuration;
        }
    }
}
