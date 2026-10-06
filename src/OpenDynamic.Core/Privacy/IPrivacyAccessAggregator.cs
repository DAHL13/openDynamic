namespace OpenDynamic.Core.Privacy;

/// <summary>
/// Core contract for evaluating raw sensor entries, filtering exclusions,
/// tracking state transitions, and emitting lifecycle change alerts.
/// Pure, thread-safe, and independent of any OS UI or Windows APIs.
/// </summary>
public interface IPrivacyAccessAggregator
{
    /// <summary>
    /// Current consolidated sensor state.
    /// </summary>
    PrivacyAccessState CurrentState { get; }

    /// <summary>
    /// Set of process and package names excluded from sensor tracking.
    /// </summary>
    IReadOnlyCollection<string> IgnoredApps { get; }

    /// <summary>
    /// Event raised whenever aggregate state (active flag or active app list) changes.
    /// </summary>
    event EventHandler<PrivacyAccessState>? StateChanged;

    /// <summary>
    /// Event raised when an individual application starts or stops using a sensor.
    /// </summary>
    event EventHandler<PrivacyAccessChange>? AccessAlertTriggered;

    /// <summary>
    /// Processes a set of raw sensor usage entries from Windows ConsentStore or mock test data.
    /// </summary>
    PrivacyAccessState ProcessEntries(IEnumerable<PrivacyAccessEntry> rawEntries, bool suppressAlerts = false);

    /// <summary>
    /// Dynamically updates the collection of ignored applications.
    /// </summary>
    void UpdateIgnoredApps(IEnumerable<string> ignoredApps);

    /// <summary>
    /// Resets all internal tracking state to empty.
    /// </summary>
    void Reset();
}
