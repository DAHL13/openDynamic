namespace OpenDynamic.Core.Widgets;

/// <summary>
/// Defines the contract for any source capable of declaring or producing dynamic island activities.
/// Widgets and background sources implement this interface to participate in priority resolution.
/// </summary>
public interface IActivitySource
{
    /// <summary>
    /// Unique identifier for this source (e.g., "media", "timer", "demo-a").
    /// </summary>
    string Id { get; }

    /// <summary>
    /// Numerical priority value. Higher numbers take precedence.
    /// </summary>
    int Priority { get; }

    /// <summary>
    /// Indicates whether this source currently has active content to display.
    /// </summary>
    bool IsActive { get; }

    /// <summary>
    /// Indicates whether this source represents a transient, short-lived alert or overlay.
    /// </summary>
    bool IsTransient { get; }

    /// <summary>
    /// Timestamp when this source was most recently activated or updated.
    /// Used for deterministic tie-breaking (most recently activated wins).
    /// </summary>
    DateTimeOffset? LastActivatedUtc { get; }

    /// <summary>
    /// Optional lifespan for transient sources before they automatically expire.
    /// </summary>
    TimeSpan? TransientDuration { get; }

    /// <summary>
    /// Current activity snapshot associated with this source, if available.
    /// </summary>
    IslandActivity? CurrentActivity { get; }

    /// <summary>
    /// Occurs whenever this source's state, priority, or activity data changes.
    /// </summary>
    event EventHandler? Changed;
}
