using System.Globalization;

namespace OpenDynamic.Core.Clock;

/// <summary>
/// Domain service responsible for formatting time, date, week numbers, and accessibility descriptions
/// according to culture conventions and user settings.
/// Strictly isolated in Core without UI dependencies (Golden Rule 5).
/// </summary>
public static class ClockFormatter
{
    /// <summary>
    /// Formats the time string according to the requested <see cref="ClockTimeFormat"/>, seconds visibility, and culture.
    /// </summary>
    /// <param name="time">The timestamp to format.</param>
    /// <param name="format">The time formatting mode (Auto, TwelveHour, TwentyFourHour).</param>
    /// <param name="showSeconds">Indicates whether seconds should be included.</param>
    /// <param name="culture">The culture to use; defaults to <see cref="CultureInfo.CurrentCulture"/>.</param>
    /// <returns>Formatted time string.</returns>
    public static string FormatTime(
        DateTimeOffset time,
        ClockTimeFormat format = ClockTimeFormat.Auto,
        bool showSeconds = false,
        CultureInfo? culture = null)
    {
        var targetCulture = culture ?? CultureInfo.CurrentCulture;

        return format switch
        {
            ClockTimeFormat.TwelveHour => FormatTwelveHour(time, showSeconds, targetCulture),
            ClockTimeFormat.TwentyFourHour => FormatTwentyFourHour(time, showSeconds, targetCulture),
            _ => FormatAuto(time, showSeconds, targetCulture)
        };
    }

    /// <summary>
    /// Formats a concise short date suitable for compact or glanceable notch display (e.g. "vie, 2 oct" or "Fri, Oct 2").
    /// </summary>
    public static string FormatDateShort(DateTimeOffset time, CultureInfo? culture = null)
    {
        var targetCulture = culture ?? CultureInfo.CurrentCulture;
        return time.ToString("ddd, d MMM", targetCulture);
    }

    /// <summary>
    /// Formats the full long date according to system culture (e.g. "viernes, 2 de octubre de 2026").
    /// </summary>
    public static string FormatDateLong(DateTimeOffset time, CultureInfo? culture = null)
    {
        var targetCulture = culture ?? CultureInfo.CurrentCulture;
        return time.ToString("D", targetCulture);
    }

    /// <summary>
    /// Calculates the ISO 8601 week number of the year for the specified timestamp.
    /// </summary>
    public static int GetIsoWeekNumber(DateTimeOffset time)
    {
        return ISOWeek.GetWeekOfYear(time.DateTime);
    }

    /// <summary>
    /// Formats the week number with localized prefix (e.g. "Semana 40" in Spanish, "Week 40" in English).
    /// </summary>
    public static string FormatWeekNumber(DateTimeOffset time, CultureInfo? culture = null)
    {
        var targetCulture = culture ?? CultureInfo.CurrentCulture;
        int week = GetIsoWeekNumber(time);

        if (targetCulture.TwoLetterISOLanguageName.Equals("es", StringComparison.OrdinalIgnoreCase))
        {
            return $"Semana {week}";
        }

        return $"Week {week}";
    }

    /// <summary>
    /// Formats a comprehensive accessibility label for screen readers and UI Automation (AutomationProperties.Name).
    /// </summary>
    public static string FormatAccessibleDescription(
        DateTimeOffset time,
        ClockTimeFormat format = ClockTimeFormat.Auto,
        bool showSeconds = false,
        CultureInfo? culture = null)
    {
        var targetCulture = culture ?? CultureInfo.CurrentCulture;
        string timePart = FormatTime(time, format, showSeconds, targetCulture);
        string datePart = FormatDateLong(time, targetCulture);
        return $"{timePart}, {datePart}";
    }

    private static string FormatAuto(DateTimeOffset time, bool showSeconds, CultureInfo culture)
    {
        if (showSeconds)
        {
            return time.ToString("T", culture);
        }

        return time.ToString("t", culture);
    }

    private static string FormatTwelveHour(DateTimeOffset time, bool showSeconds, CultureInfo culture)
    {
        var dtfi = (DateTimeFormatInfo)culture.DateTimeFormat.Clone();
        if (string.IsNullOrWhiteSpace(dtfi.AMDesignator))
        {
            dtfi.AMDesignator = "AM";
        }
        if (string.IsNullOrWhiteSpace(dtfi.PMDesignator))
        {
            dtfi.PMDesignator = "PM";
        }

        string pattern = showSeconds ? "h:mm:ss tt" : "h:mm tt";
        return time.ToString(pattern, dtfi);
    }

    private static string FormatTwentyFourHour(DateTimeOffset time, bool showSeconds, CultureInfo culture)
    {
        string pattern = showSeconds ? "HH:mm:ss" : "HH:mm";
        return time.ToString(pattern, culture);
    }
}
