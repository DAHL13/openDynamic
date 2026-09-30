namespace OpenDynamic.Core.Privacy;

/// <summary>
/// Passive monitor observing Windows ConsentStore registry keys for microphone and webcam access.
/// Adheres strictly to Golden Rule 1 (zero polling) and Golden Rule 10 (no hardware capture).
/// </summary>
public interface IPrivacyAccessMonitor : IDisposable
{
    /// <summary>
    /// Current privacy state aggregated across non-ignored applications.
    /// </summary>
    PrivacyAccessState CurrentState { get; }

    /// <summary>
    /// Underlying aggregator instance.
    /// </summary>
    IPrivacyAccessAggregator Aggregator { get; }

    /// <summary>
    /// Event raised when the aggregate sensor access state changes.
    /// </summary>
    event EventHandler<PrivacyAccessState>? StateChanged;

    /// <summary>
    /// Event raised when an application begins or ceases accessing a sensor.
    /// </summary>
    event EventHandler<PrivacyAccessChange>? AccessAlertTriggered;

    /// <summary>
    /// Starts passive monitoring with kernel event wait handles (zero polling).
    /// </summary>
    void Start();

    /// <summary>
    /// Stops passive monitoring and cleanly releases background threads and OS handles.
    /// </summary>
    void Stop();
}
