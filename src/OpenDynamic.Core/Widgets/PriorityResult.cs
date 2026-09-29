using OpenDynamic.Core.State;

namespace OpenDynamic.Core.Widgets;

/// <summary>
/// Represents the outcome of evaluating a collection of <see cref="IActivitySource"/> candidates.
/// </summary>
/// <param name="Primary">The highest-priority active source to display in the main capsule, or null if none.</param>
/// <param name="Secondary">The secondary active source to display in split mode, or null if single or none.</param>
/// <param name="SuggestedState">The recommended <see cref="IslandState"/> based on active sources.</param>
/// <param name="NextExpirationUtc">The earliest UTC expiration timestamp among active transient sources, if any.</param>
public sealed record PriorityResult(
    IActivitySource? Primary,
    IActivitySource? Secondary,
    IslandState SuggestedState,
    DateTimeOffset? NextExpirationUtc = null)
{
    /// <summary>
    /// Indicates whether at least one active source is available.
    /// </summary>
    public bool HasActiveActivity => Primary != null;

    /// <summary>
    /// Indicates whether two distinct sources are active and suitable for split presentation.
    /// </summary>
    public bool IsSplit => Primary != null && Secondary != null;
}
