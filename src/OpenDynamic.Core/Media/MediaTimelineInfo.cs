namespace OpenDynamic.Core.Media;

/// <summary>
/// Immutable snapshot of media track timeline properties and boundaries.
/// </summary>
public sealed record MediaTimelineInfo(
    TimeSpan StartTime,
    TimeSpan EndTime,
    TimeSpan Position,
    TimeSpan MinSeekTime,
    TimeSpan MaxSeekTime,
    DateTimeOffset LastUpdatedUtc);
