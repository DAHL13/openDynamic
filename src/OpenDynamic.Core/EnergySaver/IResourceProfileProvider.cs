namespace OpenDynamic.Core.EnergySaver;

/// <summary>
/// Provides live access and change notifications for the active system resource profile.
/// Enables services (animator, hardware, visualizer) to dynamically adjust operational footprint.
/// </summary>
public interface IResourceProfileProvider
{
    /// <summary>
    /// Gets the current active resource profile.
    /// </summary>
    ResourceProfile CurrentProfile { get; }

    /// <summary>
    /// Event raised when the resource profile changes (e.g. energy saver turned on/off or settings modified).
    /// </summary>
    event EventHandler<ResourceProfile>? ResourceProfileChanged;
}
