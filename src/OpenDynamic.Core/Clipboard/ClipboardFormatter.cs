using System.Text;
using System.Text.RegularExpressions;

namespace OpenDynamic.Core.Clipboard;

/// <summary>
/// Pure algorithmic formatter and classifier for clipboard contents.
/// Operates without any dependency on WPF or Windows APIs (Golden Rule 5).
/// </summary>
public static partial class ClipboardFormatter
{
    public const int DefaultMaxPreviewLength = 80;

    [GeneratedRegex(@"\s+")]
    private static partial Regex MultipleWhitespaceRegex();

    /// <summary>
    /// Sanitizes and collapses multiline text into a clean single-line preview string,
    /// truncated to <paramref name="maxLength"/> characters with an ellipsis if needed.
    /// </summary>
    /// <param name="input">The raw text to sanitize.</param>
    /// <param name="maxLength">Maximum allowed character count for the preview. Defaults to 80.</param>
    /// <returns>A single-line sanitized preview string.</returns>
    public static string SanitizeTextPreview(string? input, int maxLength = DefaultMaxPreviewLength)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        maxLength = Math.Max(10, maxLength);

        // Replace all consecutive whitespace, tabs, and newlines with a single space
        string collapsed = MultipleWhitespaceRegex().Replace(input.Trim(), " ");

        if (collapsed.Length <= maxLength)
        {
            return collapsed;
        }

        // Truncate safely with ellipsis
        return string.Concat(collapsed.AsSpan(0, maxLength - 1), "…");
    }

    /// <summary>
    /// Classifies whether a text string represents an absolute web/network URL or general plain text.
    /// </summary>
    /// <param name="text">The text to analyze.</param>
    /// <returns><see cref="ClipboardItemKind.Url"/> if the text is a valid HTTP/HTTPS/FTP URI; otherwise <see cref="ClipboardItemKind.Text"/>.</returns>
    public static ClipboardItemKind ClassifyTextKind(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return ClipboardItemKind.Text;
        }

        string trimmed = text.Trim();

        if (Uri.TryCreate(trimmed, UriKind.Absolute, out Uri? uri))
        {
            if (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
                uri.Scheme.Equals(Uri.UriSchemeFtp, StringComparison.OrdinalIgnoreCase))
            {
                return ClipboardItemKind.Url;
            }
        }

        return ClipboardItemKind.Text;
    }

    /// <summary>
    /// Generates a privacy-safe label for file drop items without leaking file paths or names.
    /// </summary>
    /// <param name="count">Number of dropped files or folders.</param>
    /// <returns>A friendly localized count string (e.g. "1 archivo" or "3 archivos").</returns>
    public static string FormatFilesPreview(int count)
    {
        int safeCount = Math.Max(0, count);
        return safeCount == 1 ? "1 archivo" : $"{safeCount} archivos";
    }

    /// <summary>
    /// Generates a privacy-safe label for image items without leaking visual contents.
    /// </summary>
    /// <returns>"Imagen copiada"</returns>
    public static string FormatImagePreview()
    {
        return "Imagen copiada";
    }
}
