namespace OpenDynamic.Core.Widgets;

/// <summary>
/// Specifies the presentation layout mode of a widget within the Dynamic Island.
/// </summary>
public enum WidgetDisplayMode
{
    /// <summary>
    /// Rendered in the standard compact capsule pill.
    /// </summary>
    Compact,

    /// <summary>
    /// Rendered as part of a multitasking split layout (e.g. primary pill or satellite bubble).
    /// </summary>
    Split,

    /// <summary>
    /// Rendered in the expanded interactive canvas.
    /// </summary>
    Expanded
}
