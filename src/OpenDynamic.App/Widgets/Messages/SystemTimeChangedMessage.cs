namespace OpenDynamic.App.Widgets.Messages;

/// <summary>
/// Decoupled message published when system time, timezone, or daylight saving changes
/// reactively via WM_TIMECHANGE or SystemEvents.TimeChanged (zero polling).
/// </summary>
public sealed record SystemTimeChangedMessage;
