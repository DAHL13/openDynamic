namespace OpenDynamic.Core.Clipboard;

/// <summary>
/// Manages the volatile, in-memory collection of recent clipboard items.
/// Adheres strictly to:
/// - Golden Rule 5: 100% pure Core logic, injectable <see cref="TimeProvider"/> for deterministic testing.
/// - Golden Rule 10: Strict RAM residency. Zero persistence to disk or log files.
/// - Sequential duplicate suppression and capacity enforcement.
/// </summary>
public sealed class ClipboardHistoryManager
{
    private readonly object _syncLock = new();
    private readonly List<ClipboardItem> _items = new();
    private readonly TimeProvider _timeProvider;

    public const int DefaultCapacity = 5;
    public const int MaxAllowedCapacity = 10;
    public const int MinAllowedCapacity = 1;
    public static readonly TimeSpan DefaultExpiration = TimeSpan.FromMinutes(10);

    private int _capacity = DefaultCapacity;
    private TimeSpan _expiration = DefaultExpiration;

    /// <summary>
    /// Gets or sets the maximum number of items retained in memory (clamped between 1 and 10).
    /// </summary>
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
                _capacity = Math.Clamp(value, MinAllowedCapacity, MaxAllowedCapacity);
                TrimExcess();
            }
        }
    }

    /// <summary>
    /// Gets or sets the time duration after which an item expires and is removed.
    /// </summary>
    public TimeSpan Expiration
    {
        get
        {
            lock (_syncLock) return _expiration;
        }
        set
        {
            lock (_syncLock)
            {
                _expiration = value <= TimeSpan.Zero ? DefaultExpiration : value;
                PurgeExpiredInternal(_timeProvider.GetUtcNow());
            }
        }
    }

    /// <summary>
    /// Gets the number of currently active, non-expired items in memory.
    /// </summary>
    public int Count
    {
        get
        {
            lock (_syncLock)
            {
                PurgeExpiredInternal(_timeProvider.GetUtcNow());
                return _items.Count;
            }
        }
    }

    public ClipboardHistoryManager(TimeProvider? timeProvider = null, int capacity = DefaultCapacity, TimeSpan? expiration = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _capacity = Math.Clamp(capacity, MinAllowedCapacity, MaxAllowedCapacity);
        _expiration = expiration ?? DefaultExpiration;
    }

    /// <summary>
    /// Adds a text or URL entry to the history if it does not duplicate the consecutive previous entry.
    /// </summary>
    /// <param name="rawText">The plain text or URL content.</param>
    /// <param name="item">Outputs the added or refreshed <see cref="ClipboardItem"/>.</param>
    /// <returns>True if a new item was added; false if it was rejected as empty, whitespace, or a consecutive duplicate.</returns>
    public bool TryAddText(string? rawText, out ClipboardItem? item)
    {
        item = null;
        if (string.IsNullOrWhiteSpace(rawText))
        {
            return false;
        }

        var now = _timeProvider.GetUtcNow();

        lock (_syncLock)
        {
            PurgeExpiredInternal(now);

            var kind = ClipboardFormatter.ClassifyTextKind(rawText);
            var preview = ClipboardFormatter.SanitizeTextPreview(rawText);

            // Check consecutive duplicate against the top item
            if (_items.Count > 0)
            {
                var top = _items[0];
                if (top.Kind == kind && string.Equals(top.RawContent, rawText, StringComparison.Ordinal))
                {
                    // Refresh timestamp to extend lifespan without duplicating entry
                    top.TimestampUtc = now;
                    item = top;
                    return false;
                }
            }

            item = new ClipboardItem(
                id: Guid.NewGuid().ToString("N"),
                kind: kind,
                rawContent: rawText,
                displayPreview: preview,
                timestampUtc: now,
                length: rawText.Length);

            _items.Insert(0, item);
            TrimExcess();
            return true;
        }
    }

    /// <summary>
    /// Adds a file drop entry to the history using only file count metadata.
    /// File paths are strictly forbidden from memory retention or logging.
    /// </summary>
    /// <param name="fileCount">Number of files dropped.</param>
    /// <param name="item">Outputs the added or refreshed <see cref="ClipboardItem"/>.</param>
    /// <returns>True if a new item was added; false if count was invalid or a consecutive duplicate.</returns>
    public bool TryAddFiles(int fileCount, out ClipboardItem? item)
    {
        item = null;
        if (fileCount <= 0)
        {
            return false;
        }

        var now = _timeProvider.GetUtcNow();

        lock (_syncLock)
        {
            PurgeExpiredInternal(now);

            var preview = ClipboardFormatter.FormatFilesPreview(fileCount);

            // Check consecutive duplicate against top item
            if (_items.Count > 0)
            {
                var top = _items[0];
                if (top.Kind == ClipboardItemKind.Files && top.Length == fileCount)
                {
                    top.TimestampUtc = now;
                    item = top;
                    return false;
                }
            }

            item = new ClipboardItem(
                id: Guid.NewGuid().ToString("N"),
                kind: ClipboardItemKind.Files,
                rawContent: null,
                displayPreview: preview,
                timestampUtc: now,
                length: fileCount);

            _items.Insert(0, item);
            TrimExcess();
            return true;
        }
    }

    /// <summary>
    /// Adds an image entry to the history using only a generic sanitized label.
    /// Bitmap image bytes are strictly forbidden from memory retention.
    /// </summary>
    /// <param name="item">Outputs the added or refreshed <see cref="ClipboardItem"/>.</param>
    /// <returns>True if a new item was added; false if it was a consecutive duplicate.</returns>
    public bool TryAddImage(out ClipboardItem? item)
    {
        var now = _timeProvider.GetUtcNow();

        lock (_syncLock)
        {
            PurgeExpiredInternal(now);

            // Check consecutive duplicate
            if (_items.Count > 0 && _items[0].Kind == ClipboardItemKind.Image)
            {
                _items[0].TimestampUtc = now;
                item = _items[0];
                return false;
            }

            item = new ClipboardItem(
                id: Guid.NewGuid().ToString("N"),
                kind: ClipboardItemKind.Image,
                rawContent: null,
                displayPreview: ClipboardFormatter.FormatImagePreview(),
                timestampUtc: now,
                length: 0);

            _items.Insert(0, item);
            TrimExcess();
            return true;
        }
    }

    /// <summary>
    /// Returns a snapshot list of current active, non-expired clipboard entries ordered newest to oldest.
    /// </summary>
    public IReadOnlyList<ClipboardItem> GetRecentItems()
    {
        lock (_syncLock)
        {
            PurgeExpiredInternal(_timeProvider.GetUtcNow());
            return _items.ToList();
        }
    }

    /// <summary>
    /// Clears all entries from RAM immediately.
    /// Used when session locks, system suspends, widget toggles off, or app shuts down.
    /// </summary>
    public void Clear()
    {
        lock (_syncLock)
        {
            _items.Clear();
        }
    }

    private void PurgeExpiredInternal(DateTimeOffset now)
    {
        if (_items.Count == 0) return;

        for (int i = _items.Count - 1; i >= 0; i--)
        {
            if (now - _items[i].TimestampUtc > _expiration)
            {
                _items.RemoveAt(i);
            }
        }
    }

    private void TrimExcess()
    {
        while (_items.Count > _capacity)
        {
            _items.RemoveAt(_items.Count - 1);
        }
    }
}
