namespace OpenDynamic.Core.Privacy;

/// <summary>
/// Represents a passive sensor usage entry recorded in the Windows ConsentStore registry.
/// </summary>
public sealed record PrivacyAccessEntry
{
    /// <summary>
    /// Type of physical sensor resource (Microphone or Camera).
    /// </summary>
    public PrivacyResourceType Resource { get; init; }

    /// <summary>
    /// Raw identifier or subkey string (e.g. package family name or encoded executable path).
    /// </summary>
    public string AppId { get; init; } = string.Empty;

    /// <summary>
    /// User-friendly, sanitized application or process name.
    /// </summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>
    /// FILETIME 64-bit integer timestamp when access to the sensor began.
    /// </summary>
    public long LastUsedTimeStart { get; init; }

    /// <summary>
    /// FILETIME 64-bit integer timestamp when access to the sensor ceased (0 if currently active).
    /// </summary>
    public long LastUsedTimeStop { get; init; }

    /// <summary>
    /// Evaluates whether the sensor is currently active based on Windows ConsentStore FILETIME timestamps.
    /// Active if Start is strictly greater than 0, and either Stop is 0 or Start is greater than Stop.
    /// </summary>
    public bool IsInUse => LastUsedTimeStart > 0 && (LastUsedTimeStop == 0 || LastUsedTimeStart > LastUsedTimeStop);
}
