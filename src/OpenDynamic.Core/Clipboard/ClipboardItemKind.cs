namespace OpenDynamic.Core.Clipboard;

/// <summary>
/// Specifies the categorized kind of clipboard item.
/// </summary>
public enum ClipboardItemKind
{
    /// <summary>
    /// Plain or formatted text content.
    /// </summary>
    Text,

    /// <summary>
    /// Web or network link (URL).
    /// </summary>
    Url,

    /// <summary>
    /// Image bitmap (retained strictly as a generic label; zero raw image bytes stored).
    /// </summary>
    Image,

    /// <summary>
    /// File drop collection (retained strictly as a count label; zero paths stored).
    /// </summary>
    Files
}
