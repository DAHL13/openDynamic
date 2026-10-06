namespace OpenDynamic.Core.Devices;

/// <summary>
/// Pure domain policy determining whether peripheral device connection/disconnection events warrant an alert.
/// Applies burst coalescing (~800ms) per device, cooldown (minimum 3s between identical events per device),
/// ignore list filtering, initial enumeration suppression, and 10-second suppression following sleep resumption.
/// Adheres strictly to Golden Rule 5 (zero Windows/WPF dependencies, deterministic TimeProvider).
/// </summary>
public sealed class DeviceAlertPolicy : IDisposable
{
    private const int MaxCooldownEntries = 256;
    private const int MaxKnownDevices = 512;

    private readonly object _syncLock = new();
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _coalesceDuration;
    private readonly TimeSpan _cooldownDuration;
    private readonly TimeSpan _suspendSuppressionDuration;

    private readonly HashSet<string> _ignoredDevices = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _knownConnectedDevices = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTimeOffset> _lastEmittedAlertTimes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (DeviceEvent Event, ITimer Timer, long Generation)> _pendingCoalesce = new(StringComparer.OrdinalIgnoreCase);

    private long _nextCoalesceGeneration;
    private bool _isEnumerationCompleted;
    private DateTimeOffset _suppressUntilUtc = DateTimeOffset.MinValue;
    private bool _isDisposed;

    /// <summary>
    /// Event triggered when a consolidated, coalesced device event warrants a user notification.
    /// </summary>
    public event EventHandler<DeviceEvent>? AlertTriggered;

    public bool IsEnumerationCompleted
    {
        get
        {
            lock (_syncLock) return _isEnumerationCompleted;
        }
    }

    public bool IsInSuspensionSuppression
    {
        get
        {
            lock (_syncLock) return _timeProvider.GetUtcNow() < _suppressUntilUtc;
        }
    }

    public IReadOnlyCollection<string> IgnoredDevices
    {
        get
        {
            lock (_syncLock) return _ignoredDevices.ToList();
        }
    }

    public DeviceAlertPolicy(
        TimeProvider? timeProvider = null,
        TimeSpan? coalesceDuration = null,
        TimeSpan? cooldownDuration = null,
        TimeSpan? suspendSuppressionDuration = null,
        IEnumerable<string>? initialIgnoredDevices = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _coalesceDuration = coalesceDuration ?? TimeSpan.FromMilliseconds(800);
        _cooldownDuration = cooldownDuration ?? TimeSpan.FromSeconds(3.0);
        _suspendSuppressionDuration = suspendSuppressionDuration ?? TimeSpan.FromSeconds(10.0);

        if (initialIgnoredDevices != null)
        {
            foreach (var dev in initialIgnoredDevices)
            {
                if (!string.IsNullOrWhiteSpace(dev))
                {
                    _ignoredDevices.Add(dev.Trim());
                }
            }
        }
    }

    /// <summary>
    /// Updates the configured list of ignored devices.
    /// </summary>
    public void SetIgnoredDevices(IEnumerable<string>? ignoredDevices)
    {
        lock (_syncLock)
        {
            _ignoredDevices.Clear();
            if (ignoredDevices != null)
            {
                foreach (var dev in ignoredDevices)
                {
                    if (!string.IsNullOrWhiteSpace(dev))
                    {
                        _ignoredDevices.Add(dev.Trim());
                    }
                }
            }
        }
    }

    /// <summary>
    /// Signals that initial peripheral enumeration has completed.
    /// Events received prior to this call are considered preexisting and do not trigger alerts.
    /// </summary>
    public void NotifyEnumerationCompleted()
    {
        lock (_syncLock)
        {
            _isEnumerationCompleted = true;
        }
    }

    /// <summary>
    /// Resets enumeration baseline and clears pending coalesce timers when watchers are stopped.
    /// </summary>
    public void ResetEnumeration()
    {
        lock (_syncLock)
        {
            _isEnumerationCompleted = false;
            _knownConnectedDevices.Clear();
            foreach (var (_, timer, _) in _pendingCoalesce.Values)
            {
                timer.Dispose();
            }
            _pendingCoalesce.Clear();
        }
    }

    /// <summary>
    /// Notifies the policy that the system resumed from sleep.
    /// Suppresses all device alerts for 10 seconds to avoid wakeup flurry notices.
    /// </summary>
    public void NotifyResumedFromSuspend()
    {
        lock (_syncLock)
        {
            var now = _timeProvider.GetUtcNow();
            _suppressUntilUtc = now + _suspendSuppressionDuration;

            // Clear any active coalesce timers
            foreach (var (_, timer, _) in _pendingCoalesce.Values)
            {
                timer.Dispose();
            }
            _pendingCoalesce.Clear();
        }
    }

    /// <summary>
    /// Notifies the policy that the system is entering suspension.
    /// </summary>
    public void NotifySuspended()
    {
        lock (_syncLock)
        {
            foreach (var (_, timer, _) in _pendingCoalesce.Values)
            {
                timer.Dispose();
            }
            _pendingCoalesce.Clear();
        }
    }

    /// <summary>
    /// Feeds a raw device event into the policy.
    /// </summary>
    public void ProcessDeviceEvent(DeviceEvent devEvent)
    {
        ArgumentNullException.ThrowIfNull(devEvent);

        lock (_syncLock)
        {
            if (_isDisposed) return;

            // 1. Ignore list filter: check both device name and device ID
            if (IsIgnoredLocked(devEvent))
            {
                return;
            }

            // 2. Pre-enumeration baseline suppression
            if (!_isEnumerationCompleted)
            {
                if (devEvent.Type == DeviceEventType.Connected)
                {
                    if (_knownConnectedDevices.Count >= MaxKnownDevices)
                    {
                        _knownConnectedDevices.Clear();
                    }
                    _knownConnectedDevices.Add(devEvent.DeviceId);
                }
                else
                {
                    _knownConnectedDevices.Remove(devEvent.DeviceId);
                }
                return;
            }

            var now = _timeProvider.GetUtcNow();

            // 3. Post-suspension suppression window
            if (now < _suppressUntilUtc)
            {
                if (devEvent.Type == DeviceEventType.Connected)
                {
                    if (_knownConnectedDevices.Count >= MaxKnownDevices)
                    {
                        _knownConnectedDevices.Clear();
                    }
                    _knownConnectedDevices.Add(devEvent.DeviceId);
                }
                else
                {
                    _knownConnectedDevices.Remove(devEvent.DeviceId);
                }
                return;
            }

            // 4. Burst Coalesce: if an event is already queued for this device, update it and restart coalesce timer
            string key = devEvent.DeviceId;
            if (_pendingCoalesce.TryGetValue(key, out var existing))
            {
                existing.Timer.Dispose();
                _pendingCoalesce.Remove(key);
            }

            long generation = ++_nextCoalesceGeneration;
            var timer = _timeProvider.CreateTimer(
                OnCoalesceTimerElapsed,
                new CoalesceTimerState(key, generation),
                _coalesceDuration,
                Timeout.InfiniteTimeSpan);

            _pendingCoalesce[key] = (devEvent, timer, generation);
        }
    }

    /// <summary>
    /// Immediately flushes all pending coalesced device events.
    /// </summary>
    public void Flush()
    {
        List<DeviceEvent> eventsToEmit = new();

        lock (_syncLock)
        {
            if (_isDisposed) return;

            foreach (var (key, (devEvent, timer, _)) in _pendingCoalesce.ToList())
            {
                timer.Dispose();
                _pendingCoalesce.Remove(key);

                var evaluated = EvaluateFinalEventLocked(devEvent);
                if (evaluated != null)
                {
                    eventsToEmit.Add(evaluated);
                }
            }
        }

        foreach (var ev in eventsToEmit)
        {
            AlertTriggered?.Invoke(this, ev);
        }
    }

    private void OnCoalesceTimerElapsed(object? state)
    {
        if (state is not CoalesceTimerState timerState) return;

        DeviceEvent? toEmit = null;

        lock (_syncLock)
        {
            if (_isDisposed) return;

            if (_pendingCoalesce.TryGetValue(timerState.DeviceId, out var item) &&
                item.Generation == timerState.Generation)
            {
                item.Timer.Dispose();
                _pendingCoalesce.Remove(timerState.DeviceId);

                toEmit = EvaluateFinalEventLocked(item.Event);
            }
        }

        if (toEmit != null)
        {
            AlertTriggered?.Invoke(this, toEmit);
        }
    }

    private sealed record CoalesceTimerState(string DeviceId, long Generation);

    private DeviceEvent? EvaluateFinalEventLocked(DeviceEvent devEvent)
    {
        var now = _timeProvider.GetUtcNow();

        if (now < _suppressUntilUtc)
        {
            return null;
        }

        if (IsIgnoredLocked(devEvent))
        {
            return null;
        }

        // Check if device state is already identical in known inventory
        bool wasKnownConnected = _knownConnectedDevices.Contains(devEvent.DeviceId);
        if (devEvent.Type == DeviceEventType.Connected && wasKnownConnected)
        {
            // Already connected and known; no transition
            return null;
        }
        if (devEvent.Type == DeviceEventType.Disconnected && !wasKnownConnected)
        {
            // Already disconnected / unknown; no transition
            return null;
        }

        // Cooldown check for identical device and event type
        string alertSignature = $"{devEvent.DeviceId}:{(int)devEvent.Type}";
        if (_lastEmittedAlertTimes.TryGetValue(alertSignature, out var lastTime) &&
            (now - lastTime) < _cooldownDuration)
        {
            return null;
        }

        // Update tracking state
        if (devEvent.Type == DeviceEventType.Connected)
        {
            if (_knownConnectedDevices.Count >= MaxKnownDevices)
            {
                _knownConnectedDevices.Clear();
            }
            _knownConnectedDevices.Add(devEvent.DeviceId);
        }
        else
        {
            _knownConnectedDevices.Remove(devEvent.DeviceId);
        }

        if (_lastEmittedAlertTimes.Count >= MaxCooldownEntries)
        {
            PruneCooldownEntriesLocked(now);
        }

        _lastEmittedAlertTimes[alertSignature] = now;
        return devEvent;
    }

    private void PruneCooldownEntriesLocked(DateTimeOffset now)
    {
        var expiredKeys = _lastEmittedAlertTimes
            .Where(kvp => (now - kvp.Value) >= _cooldownDuration)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var k in expiredKeys)
        {
            _lastEmittedAlertTimes.Remove(k);
        }

        if (_lastEmittedAlertTimes.Count >= MaxCooldownEntries)
        {
            var oldest = _lastEmittedAlertTimes
                .OrderBy(kvp => kvp.Value)
                .Take(_lastEmittedAlertTimes.Count / 2)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var k in oldest)
            {
                _lastEmittedAlertTimes.Remove(k);
            }
        }
    }

    private bool IsIgnoredLocked(DeviceEvent devEvent)
    {
        if (_ignoredDevices.Count == 0) return false;

        if (!string.IsNullOrWhiteSpace(devEvent.DeviceName) && _ignoredDevices.Contains(devEvent.DeviceName.Trim()))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(devEvent.DeviceId) && _ignoredDevices.Contains(devEvent.DeviceId.Trim()))
        {
            return true;
        }

        return false;
    }

    public void Dispose()
    {
        lock (_syncLock)
        {
            if (_isDisposed) return;
            _isDisposed = true;

            foreach (var (_, timer, _) in _pendingCoalesce.Values)
            {
                timer.Dispose();
            }
            _pendingCoalesce.Clear();
            AlertTriggered = null;
        }
    }
}
