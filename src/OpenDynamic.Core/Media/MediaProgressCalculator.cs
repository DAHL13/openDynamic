namespace OpenDynamic.Core.Media;

/// <summary>
/// Pure domain logic for extrapolating media playback position locally without polling GSMTC APIs.
/// Honors Golden Rule 1 (0% CPU at rest) and Golden Rule 5 (Core domain isolation).
/// </summary>
public static class MediaProgressCalculator
{
    /// <summary>
    /// Calculates the current track position by extrapolating time elapsed since last updated timestamp when playing.
    /// </summary>
    /// <param name="basePosition">Last confirmed position from media session.</param>
    /// <param name="lastUpdatedUtc">Timestamp when basePosition was recorded.</param>
    /// <param name="start">Start time boundary of the track (usually 0:00).</param>
    /// <param name="end">End time boundary of the track (track duration).</param>
    /// <param name="isPlaying">True if playback is currently active.</param>
    /// <param name="nowUtc">Current UTC timestamp.</param>
    /// <returns>Extrapolated position clamped to [start, end].</returns>
    public static TimeSpan CalculateCurrentPosition(
        TimeSpan basePosition,
        DateTimeOffset lastUpdatedUtc,
        TimeSpan start,
        TimeSpan end,
        bool isPlaying,
        DateTimeOffset nowUtc)
    {
        if (end <= start)
        {
            return TimeSpan.Zero;
        }

        var calculated = basePosition;

        if (isPlaying && lastUpdatedUtc != default && nowUtc > lastUpdatedUtc)
        {
            var elapsed = nowUtc - lastUpdatedUtc;
            calculated = basePosition + elapsed;
        }

        if (calculated < start)
        {
            return start;
        }

        if (calculated > end)
        {
            return end;
        }

        return calculated;
    }

    /// <summary>
    /// Computes the normalized playback progress ratio [0.0, 1.0] given the current position and bounds.
    /// </summary>
    public static double CalculateProgressRatio(TimeSpan currentPosition, TimeSpan start, TimeSpan end)
    {
        var totalMs = (end - start).TotalMilliseconds;
        if (totalMs <= 0)
        {
            return 0.0;
        }

        var currentMs = (currentPosition - start).TotalMilliseconds;
        return Math.Clamp(currentMs / totalMs, 0.0, 1.0);
    }

    /// <summary>
    /// Formats a time span cleanly as mm:ss or h:mm:ss.
    /// </summary>
    public static string FormatTime(TimeSpan time)
    {
        if (time < TimeSpan.Zero)
        {
            time = TimeSpan.Zero;
        }

        if (time.TotalHours >= 1)
        {
            return $"{(int)time.TotalHours}:{time.Minutes:D2}:{time.Seconds:D2}";
        }

        return $"{time.Minutes}:{time.Seconds:D2}";
    }
}
