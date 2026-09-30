namespace OpenDynamic.Core.Stopwatch;

/// <summary>
/// Immutable record representing a recorded lap in the stopwatch.
/// Adheres strictly to Golden Rule 5 (isolated in Core, zero UI dependencies).
/// </summary>
/// <param name="LapNumber">1-based lap index.</param>
/// <param name="LapDuration">Duration of this individual lap.</param>
/// <param name="SplitTime">Cumulative elapsed time at the moment the lap was recorded.</param>
/// <param name="FormattedLap">Formatted duration of the lap (<c>mm:ss.cc</c> or <c>h:mm:ss.cc</c>).</param>
/// <param name="FormattedSplit">Formatted cumulative split time (<c>mm:ss.cc</c> or <c>h:mm:ss.cc</c>).</param>
public sealed record StopwatchLap(
    int LapNumber,
    TimeSpan LapDuration,
    TimeSpan SplitTime,
    string FormattedLap,
    string FormattedSplit);
