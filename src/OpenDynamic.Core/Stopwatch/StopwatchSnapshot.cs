namespace OpenDynamic.Core.Stopwatch;

/// <summary>
/// Immutable snapshot representing the stopwatch state at a specific point in time.
/// Pure domain model with zero UI dependencies (Golden Rule 5).
/// </summary>
/// <param name="ElapsedTime">Total accumulated elapsed time.</param>
/// <param name="CurrentLapTime">Elapsed time in the currently active uncommitted lap.</param>
/// <param name="Laps">List of completed laps in chronological order.</param>
/// <param name="State">Current lifecycle execution state.</param>
/// <param name="FormattedElapsed">Formatted elapsed time (<c>mm:ss.cc</c> or <c>h:mm:ss</c>).</param>
/// <param name="FormattedPrecise">Formatted precise elapsed time including centiseconds (<c>mm:ss.cc</c> or <c>h:mm:ss.cc</c>).</param>
/// <param name="FormattedCurrentLap">Formatted duration of the current uncommitted lap.</param>
public sealed record StopwatchSnapshot(
    TimeSpan ElapsedTime,
    TimeSpan CurrentLapTime,
    IReadOnlyList<StopwatchLap> Laps,
    StopwatchState State,
    string FormattedElapsed,
    string FormattedPrecise,
    string FormattedCurrentLap);
