namespace OpenDynamic.Core.Screenshots;

/// <summary>
/// Manages the volatile, in-memory list of the most recent screenshot entries (default capacity: 5).
/// Never persists paths to disk or logs. Evaluates file availability dynamically so missing files
/// are flagged as unavailable ("No disponible") and can be removed by the user.
/// </summary>
public sealed class ScreenshotHistory
{
    public const int DefaultCapacity = 5;
    public const int MinCapacity = 1;
    public const int MaxCapacity = 10;
    public static readonly TimeSpan DefaultRetentionDuration = TimeSpan.FromMinutes(30);

    private readonly object _syncLock = new();
    private readonly List<ScreenshotEntry> _entries = new();
    private readonly TimeProvider _timeProvider;
    private readonly Func<string, bool> _fileExistsProbe;

    private int _capacity = DefaultCapacity;
    private TimeSpan _retentionDuration = DefaultRetentionDuration;

    public int Capacity
    {
        get
        {
            lock (_syncLock) return _capacity;
        }
        set
        {
            lock (_syncLock)
            {
                _capacity = Math.Clamp(value, MinCapacity, MaxCapacity);
                TrimExcess();
            }
        }
    }

    public TimeSpan RetentionDuration
    {
        get
        {
            lock (_syncLock) return _retentionDuration;
        }
        set
        {
            lock (_syncLock)
            {
                _retentionDuration = value <= TimeSpan.Zero ? DefaultRetentionDuration : value;
                PurgeExpiredInternal(_timeProvider.GetUtcNow());
            }
        }
    }

    public int Count
    {
        get
        {
            lock (_syncLock)
            {
                PurgeExpiredInternal(_timeProvider.GetUtcNow());
                return _entries.Count;
            }
        }
    }

    public ScreenshotHistory(
        TimeProvider? timeProvider = null,
        int capacity = DefaultCapacity,
        TimeSpan? retentionDuration = null,
        Func<string, bool>? fileExistsProbe = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _capacity = Math.Clamp(capacity, MinCapacity, MaxCapacity);
        _retentionDuration = retentionDuration ?? DefaultRetentionDuration;
        _fileExistsProbe = fileExistsProbe ?? File.Exists;
    }

    /// <summary>
    /// Adds or refreshes a screenshot entry at the top of the in-memory history.
    /// </summary>
    public ScreenshotEntry Add(
        string filePath,
        long fileSizeBytes,
        int pixelWidth = 0,
        int pixelHeight = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var now = _timeProvider.GetUtcNow();
        bool exists = SafeFileExists(filePath);

        var entry = new ScreenshotEntry(
            filePath: filePath,
            fileSizeBytes: fileSizeBytes,
            capturedAtUtc: now,
            pixelWidth: pixelWidth,
            pixelHeight: pixelHeight,
            isAvailable: exists);

        lock (_syncLock)
        {
            PurgeExpiredInternal(now);

            // Remove existing entry for the same path (case-insensitive on Windows)
            _entries.RemoveAll(e => string.Equals(e.FilePath, filePath, StringComparison.OrdinalIgnoreCase));

            _entries.Insert(0, entry);
            TrimExcess();
        }

        return entry;
    }

    /// <summary>
    /// Returns the recent screenshot entries ordered newest to oldest,
    /// refreshing each entry's <see cref="ScreenshotEntry.IsAvailable"/> state.
    /// </summary>
    public IReadOnlyList<ScreenshotEntry> GetRecentEntries()
    {
        lock (_syncLock)
        {
            PurgeExpiredInternal(_timeProvider.GetUtcNow());

            for (int i = 0; i < _entries.Count; i++)
            {
                var current = _entries[i];
                bool available = SafeFileExists(current.FilePath);
                if (current.IsAvailable != available)
                {
                    _entries[i] = current with { IsAvailable = available };
                }
            }

            return _entries.ToList();
        }
    }

    /// <summary>
    /// Removes a specific screenshot path from the in-memory history.
    /// </summary>
    public bool Remove(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return false;
        }

        lock (_syncLock)
        {
            int removed = _entries.RemoveAll(e => string.Equals(e.FilePath, filePath, StringComparison.OrdinalIgnoreCase));
            return removed > 0;
        }
    }

    /// <summary>
    /// Clears all screenshot entries from memory immediately.
    /// </summary>
    public void Clear()
    {
        lock (_syncLock)
        {
            _entries.Clear();
        }
    }

    private bool SafeFileExists(string filePath)
    {
        try
        {
            return _fileExistsProbe(filePath);
        }
        catch
        {
            return false;
        }
    }

    private void PurgeExpiredInternal(DateTimeOffset now)
    {
        if (_entries.Count == 0)
        {
            return;
        }

        for (int i = _entries.Count - 1; i >= 0; i--)
        {
            if ((now - _entries[i].CapturedAtUtc) > _retentionDuration)
            {
                _entries.RemoveAt(i);
            }
        }
    }

    private void TrimExcess()
    {
        while (_entries.Count > _capacity)
        {
            _entries.RemoveAt(_entries.Count - 1);
        }
    }
}
