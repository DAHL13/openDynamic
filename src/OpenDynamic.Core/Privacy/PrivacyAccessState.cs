namespace OpenDynamic.Core.Privacy;

/// <summary>
/// Immutable snapshot representing aggregate active sensor access across the system.
/// </summary>
public sealed record PrivacyAccessState
{
    public static readonly PrivacyAccessState Empty = new(
        isMicrophoneActive: false,
        isCameraActive: false,
        activeMicrophoneApps: Array.Empty<string>(),
        activeCameraApps: Array.Empty<string>());

    /// <summary>
    /// Indicates whether one or more non-ignored applications are currently accessing the microphone.
    /// </summary>
    public bool IsMicrophoneActive { get; init; }

    /// <summary>
    /// Indicates whether one or more non-ignored applications are currently accessing the camera/webcam.
    /// </summary>
    public bool IsCameraActive { get; init; }

    /// <summary>
    /// List of friendly names of applications currently accessing the microphone.
    /// </summary>
    public IReadOnlyList<string> ActiveMicrophoneApps { get; init; }

    /// <summary>
    /// List of friendly names of applications currently accessing the camera.
    /// </summary>
    public IReadOnlyList<string> ActiveCameraApps { get; init; }

    /// <summary>
    /// Convenience property indicating if any sensor is currently active.
    /// </summary>
    public bool HasActiveResource => IsMicrophoneActive || IsCameraActive;

    public PrivacyAccessState(
        bool isMicrophoneActive,
        bool isCameraActive,
        IReadOnlyList<string>? activeMicrophoneApps = null,
        IReadOnlyList<string>? activeCameraApps = null)
    {
        IsMicrophoneActive = isMicrophoneActive;
        IsCameraActive = isCameraActive;
        ActiveMicrophoneApps = activeMicrophoneApps ?? Array.Empty<string>();
        ActiveCameraApps = activeCameraApps ?? Array.Empty<string>();
    }
}
