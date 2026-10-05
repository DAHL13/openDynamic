using System.Globalization;

namespace OpenDynamic.Core.Screenshots;

/// <summary>
/// Represents a volatile, in-memory metadata entry for a captured screenshot.
/// Strictly resides in RAM and is cleared when the application closes.
/// </summary>
public sealed record ScreenshotEntry
{
    public string FilePath { get; init; }
    public string FileName { get; init; }
    public string Extension { get; init; }
    public long FileSizeBytes { get; init; }
    public int PixelWidth { get; init; }
    public int PixelHeight { get; init; }
    public DateTimeOffset CapturedAtUtc { get; init; }
    public bool IsAvailable { get; init; }

    public string FormattedSize => FormatFileSize(FileSizeBytes);
    public string FormattedDimensions => FormatDimensions(PixelWidth, PixelHeight);
    public string StatusLabel => IsAvailable ? FormattedSize : "No disponible";

    public string MetadataSummary
    {
        get
        {
            if (!IsAvailable)
            {
                return "Archivo no disponible";
            }

            if (PixelWidth > 0 && PixelHeight > 0)
            {
                return $"{FormattedDimensions} • {FormattedSize}";
            }

            return FormattedSize;
        }
    }

    public ScreenshotEntry(
        string filePath,
        long fileSizeBytes,
        DateTimeOffset capturedAtUtc,
        int pixelWidth = 0,
        int pixelHeight = 0,
        bool isAvailable = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        FilePath = filePath;
        FileName = Path.GetFileName(filePath);
        Extension = NormalizeExtension(Path.GetExtension(filePath));
        FileSizeBytes = Math.Max(0L, fileSizeBytes);
        PixelWidth = Math.Max(0, pixelWidth);
        PixelHeight = Math.Max(0, pixelHeight);
        CapturedAtUtc = capturedAtUtc;
        IsAvailable = isAvailable;
    }

    public static string NormalizeExtension(string? ext)
    {
        if (string.IsNullOrWhiteSpace(ext))
        {
            return string.Empty;
        }

        string trimmed = ext.Trim();
        if (!trimmed.StartsWith('.'))
        {
            trimmed = "." + trimmed;
        }

        return trimmed.ToLowerInvariant();
    }

    public static string FormatFileSize(long bytes)
    {
        if (bytes <= 0)
        {
            return "0 B";
        }

        if (bytes < 1024)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{bytes} B");
        }

        double kb = bytes / 1024.0;
        if (kb < 1024.0)
        {
            return kb >= 100.0
                ? string.Create(CultureInfo.InvariantCulture, $"{kb:F0} KB")
                : string.Create(CultureInfo.InvariantCulture, $"{kb:F1} KB");
        }

        double mb = kb / 1024.0;
        return string.Create(CultureInfo.InvariantCulture, $"{mb:F1} MB");
    }

    public static string FormatDimensions(int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            return string.Empty;
        }

        return string.Create(CultureInfo.InvariantCulture, $"{width} × {height}");
    }
}
