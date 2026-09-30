namespace OpenDynamic.Core.Clipboard;

/// <summary>
/// Represents a clipboard entry stored strictly in volatile RAM memory.
/// Adheres strictly to the Privacy Rule (Golden Rule 10):
/// - Resides strictly in RAM (never persisted to disk, settings, or log sinks).
/// - For images and files, no binary pixel data or file system paths are stored; only sanitized labels and counts.
/// </summary>
public sealed class ClipboardItem
{
    /// <summary>
    /// Unique identifier for the clipboard entry.
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// Categorized item kind.
    /// </summary>
    public ClipboardItemKind Kind { get; }

    /// <summary>
    /// Raw text content in memory (for Text or Url kinds).
    /// Null for Image or Files kinds to prevent holding bulky or sensitive disk metadata.
    /// </summary>
    public string? RawContent { get; }

    /// <summary>
    /// Sanitized, single-line preview string truncated safely for display (~80 chars max).
    /// </summary>
    public string DisplayPreview { get; }

    /// <summary>
    /// Timestamp when the entry was captured or refreshed (UTC).
    /// </summary>
    public DateTimeOffset TimestampUtc { get; set; }

    /// <summary>
    /// Length of the raw text in characters, or file count for file drop items.
    /// Used strictly for metadata logging and display without leaking content.
    /// </summary>
    public int Length { get; }

    public ClipboardItem(
        string id,
        ClipboardItemKind kind,
        string? rawContent,
        string displayPreview,
        DateTimeOffset timestampUtc,
        int length)
    {
        Id = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("N") : id;
        Kind = kind;
        RawContent = rawContent;
        DisplayPreview = displayPreview ?? string.Empty;
        TimestampUtc = timestampUtc;
        Length = Math.Max(0, length);
    }
}
