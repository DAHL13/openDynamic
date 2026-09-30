namespace OpenDynamic.Core.Privacy;

/// <summary>
/// Type of sensor access lifecycle transition.
/// </summary>
public enum PrivacyAccessEventKind
{
    /// <summary>
    /// An application initiated sensor capture.
    /// </summary>
    Started,

    /// <summary>
    /// An application ceased sensor capture / released the device.
    /// </summary>
    Stopped
}

/// <summary>
/// Event argument data emitted when an individual sensor access transition occurs.
/// </summary>
public sealed record PrivacyAccessChange
{
    public PrivacyResourceType Resource { get; init; }
    public PrivacyAccessEventKind EventKind { get; init; }
    public string AppName { get; init; } = string.Empty;
    public DateTimeOffset TimestampUtc { get; init; }
}
