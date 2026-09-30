namespace OpenDynamic.Core.Privacy;

/// <summary>
/// Pure domain aggregator that evaluates sensor access entries based on FILETIME timestamps,
/// applies exclusion filters, and emits deterministic state and alert events.
/// Pure C# (Regla de Oro 5) with zero dependencies on Windows, Win32, or UI.
/// </summary>
public class PrivacyAccessAggregator : IPrivacyAccessAggregator
{
    private readonly object _syncLock = new();
    private readonly TimeProvider _timeProvider;
    private readonly HashSet<string> _ignoredApps = new(StringComparer.OrdinalIgnoreCase);

    private readonly HashSet<string> _activeMicApps = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _activeCamApps = new(StringComparer.OrdinalIgnoreCase);

    private PrivacyAccessState _currentState = PrivacyAccessState.Empty;

    public PrivacyAccessState CurrentState
    {
        get
        {
            lock (_syncLock)
            {
                return _currentState;
            }
        }
    }

    public IReadOnlyCollection<string> IgnoredApps
    {
        get
        {
            lock (_syncLock)
            {
                return _ignoredApps.ToArray();
            }
        }
    }

    public event EventHandler<PrivacyAccessState>? StateChanged;
    public event EventHandler<PrivacyAccessChange>? AccessAlertTriggered;

    public PrivacyAccessAggregator(
        IEnumerable<string>? ignoredApps = null,
        TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;

        if (ignoredApps != null)
        {
            foreach (var app in ignoredApps)
            {
                if (!string.IsNullOrWhiteSpace(app))
                {
                    _ignoredApps.Add(app.Trim());
                }
            }
        }
    }

    public void UpdateIgnoredApps(IEnumerable<string> ignoredApps)
    {
        ArgumentNullException.ThrowIfNull(ignoredApps);

        lock (_syncLock)
        {
            _ignoredApps.Clear();
            foreach (var app in ignoredApps)
            {
                if (!string.IsNullOrWhiteSpace(app))
                {
                    _ignoredApps.Add(app.Trim());
                }
            }
        }
    }

    public PrivacyAccessState ProcessEntries(IEnumerable<PrivacyAccessEntry> rawEntries)
    {
        ArgumentNullException.ThrowIfNull(rawEntries);

        var newMicApps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var newCamApps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var alertList = new List<PrivacyAccessChange>();
        var now = _timeProvider.GetUtcNow();

        PrivacyAccessState newState;
        bool hasStateChange = false;

        lock (_syncLock)
        {
            foreach (var entry in rawEntries)
            {
                if (entry == null || !entry.IsInUse)
                {
                    continue;
                }

                string name = !string.IsNullOrWhiteSpace(entry.DisplayName) ? entry.DisplayName.Trim() : entry.AppId.Trim();
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                if (IsAppIgnored(name, entry.AppId))
                {
                    continue;
                }

                if (entry.Resource == PrivacyResourceType.Microphone)
                {
                    newMicApps.Add(name);
                }
                else if (entry.Resource == PrivacyResourceType.Camera)
                {
                    newCamApps.Add(name);
                }
            }

            // Detect Microphone transitions
            // Started
            foreach (var app in newMicApps)
            {
                if (!_activeMicApps.Contains(app))
                {
                    alertList.Add(new PrivacyAccessChange
                    {
                        Resource = PrivacyResourceType.Microphone,
                        EventKind = PrivacyAccessEventKind.Started,
                        AppName = app,
                        TimestampUtc = now
                    });
                }
            }
            // Stopped
            foreach (var app in _activeMicApps)
            {
                if (!newMicApps.Contains(app))
                {
                    alertList.Add(new PrivacyAccessChange
                    {
                        Resource = PrivacyResourceType.Microphone,
                        EventKind = PrivacyAccessEventKind.Stopped,
                        AppName = app,
                        TimestampUtc = now
                    });
                }
            }

            // Detect Camera transitions
            // Started
            foreach (var app in newCamApps)
            {
                if (!_activeCamApps.Contains(app))
                {
                    alertList.Add(new PrivacyAccessChange
                    {
                        Resource = PrivacyResourceType.Camera,
                        EventKind = PrivacyAccessEventKind.Started,
                        AppName = app,
                        TimestampUtc = now
                    });
                }
            }
            // Stopped
            foreach (var app in _activeCamApps)
            {
                if (!newCamApps.Contains(app))
                {
                    alertList.Add(new PrivacyAccessChange
                    {
                        Resource = PrivacyResourceType.Camera,
                        EventKind = PrivacyAccessEventKind.Stopped,
                        AppName = app,
                        TimestampUtc = now
                    });
                }
            }

            // Check if aggregate state changed
            bool micActive = newMicApps.Count > 0;
            bool camActive = newCamApps.Count > 0;

            if (_currentState.IsMicrophoneActive != micActive ||
                _currentState.IsCameraActive != camActive ||
                !_activeMicApps.SetEquals(newMicApps) ||
                !_activeCamApps.SetEquals(newCamApps))
            {
                hasStateChange = true;
            }

            _activeMicApps.Clear();
            _activeMicApps.UnionWith(newMicApps);

            _activeCamApps.Clear();
            _activeCamApps.UnionWith(newCamApps);

            newState = new PrivacyAccessState(
                isMicrophoneActive: micActive,
                isCameraActive: camActive,
                activeMicrophoneApps: _activeMicApps.OrderBy(a => a, StringComparer.OrdinalIgnoreCase).ToArray(),
                activeCameraApps: _activeCamApps.OrderBy(a => a, StringComparer.OrdinalIgnoreCase).ToArray());

            _currentState = newState;
        }

        // Fire alert events outside lock to prevent deadlock
        foreach (var alert in alertList)
        {
            AccessAlertTriggered?.Invoke(this, alert);
        }

        if (hasStateChange)
        {
            StateChanged?.Invoke(this, newState);
        }

        return newState;
    }

    public void Reset()
    {
        PrivacyAccessState emptyState;
        lock (_syncLock)
        {
            _activeMicApps.Clear();
            _activeCamApps.Clear();
            _currentState = PrivacyAccessState.Empty;
            emptyState = _currentState;
        }

        StateChanged?.Invoke(this, emptyState);
    }

    private bool IsAppIgnored(string name, string appId)
    {
        // Exclude openDynamic itself
        if (name.Contains("openDynamic", StringComparison.OrdinalIgnoreCase) ||
            appId.Contains("openDynamic", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (_ignoredApps.Contains(name) || _ignoredApps.Contains(appId))
        {
            return true;
        }

        // Check without .exe extension if applicable
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            string baseName = name[..^4];
            if (_ignoredApps.Contains(baseName))
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>
/// Alias for <see cref="PrivacyAccessAggregator"/> conforming to architecture terminology.
/// </summary>
public sealed class PrivacyIndicatorAggregator : PrivacyAccessAggregator
{
    public PrivacyIndicatorAggregator(
        IEnumerable<string>? ignoredApps = null,
        TimeProvider? timeProvider = null)
        : base(ignoredApps, timeProvider)
    {
    }
}
