using System.Windows.Media;

namespace OpenDynamic.App.Widgets.Messages;

/// <summary>
/// Message broadcast whenever the dynamic cover art accent color is extracted or updated.
/// Allows the notch border outline to react smoothly with a short ColorAnimation (~300ms).
/// </summary>
public sealed record MediaAccentColorChangedMessage(Color? AccentColor);
