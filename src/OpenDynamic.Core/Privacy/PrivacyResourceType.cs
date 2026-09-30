namespace OpenDynamic.Core.Privacy;

/// <summary>
/// Identifies physical sensor privacy resources monitored by Windows ConsentStore.
/// </summary>
public enum PrivacyResourceType
{
    /// <summary>
    /// Audio input capture (Microphone).
    /// </summary>
    Microphone,

    /// <summary>
    /// Video capture device (Webcam / Camera).
    /// </summary>
    Camera
}
