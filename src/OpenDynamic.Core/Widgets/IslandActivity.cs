namespace OpenDynamic.Core.Widgets;

/// <summary>
/// Immutable snapshot representing an activity emitted by an activity source.
/// </summary>
/// <param name="Id">Unique identifier of the activity or emitting source.</param>
/// <param name="Title">Primary display title or label.</param>
/// <param name="Subtitle">Optional contextual subtitle or status description.</param>
/// <param name="Priority">Numerical priority; higher values take precedence.</param>
/// <param name="IsTransient">Indicates whether the activity is ephemeral (e.g. transient volume or notice).</param>
/// <param name="Duration">Optional lifespan duration for transient activities.</param>
/// <param name="CreatedAtUtc">Timestamp when this activity instance was created or triggered.</param>
public sealed record IslandActivity(
    string Id,
    string Title,
    string? Subtitle = null,
    int Priority = ActivityPriority.Normal,
    bool IsTransient = false,
    TimeSpan? Duration = null,
    DateTimeOffset? CreatedAtUtc = null);
