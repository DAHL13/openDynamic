using System.Globalization;
using OpenDynamic.Core.Clock;

namespace OpenDynamic.Tests.Clock;

public class ClockFormatterTests
{
    private readonly CultureInfo _esMx = CultureInfo.GetCultureInfo("es-MX");
    private readonly CultureInfo _enUs = CultureInfo.GetCultureInfo("en-US");
    private readonly CultureInfo _enGb = CultureInfo.GetCultureInfo("en-GB");

    private readonly DateTimeOffset _sampleTime = new(2026, 10, 2, 15, 30, 45, TimeSpan.FromHours(-6));

    [Fact]
    public void FormatTime_TwelveHour_ForcesAmPmInAllCultures()
    {
        string formattedUs = ClockFormatter.FormatTime(_sampleTime, ClockTimeFormat.TwelveHour, showSeconds: false, _enUs);
        string formattedMx = ClockFormatter.FormatTime(_sampleTime, ClockTimeFormat.TwelveHour, showSeconds: false, _esMx);
        string formattedGb = ClockFormatter.FormatTime(_sampleTime, ClockTimeFormat.TwelveHour, showSeconds: false, _enGb);

        Assert.Contains("3:30", formattedUs);
        Assert.Contains("PM", formattedUs, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("3:30", formattedMx);

        // en-GB normally uses 24h, but TwelveHour forced MUST output 12-hour with PM
        Assert.Contains("3:30", formattedGb);
        Assert.Contains("PM", formattedGb, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FormatTime_TwelveHour_WithSeconds_IncludesSeconds()
    {
        string formattedUs = ClockFormatter.FormatTime(_sampleTime, ClockTimeFormat.TwelveHour, showSeconds: true, _enUs);
        Assert.Contains("3:30:45", formattedUs);
        Assert.Contains("PM", formattedUs, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FormatTime_TwentyFourHour_Forces24HourWithoutAmPm()
    {
        string formattedUs = ClockFormatter.FormatTime(_sampleTime, ClockTimeFormat.TwentyFourHour, showSeconds: false, _enUs);
        string formattedMx = ClockFormatter.FormatTime(_sampleTime, ClockTimeFormat.TwentyFourHour, showSeconds: false, _esMx);
        string formattedGb = ClockFormatter.FormatTime(_sampleTime, ClockTimeFormat.TwentyFourHour, showSeconds: false, _enGb);

        Assert.Equal("15:30", formattedUs);
        Assert.Equal("15:30", formattedMx);
        Assert.Equal("15:30", formattedGb);
    }

    [Fact]
    public void FormatTime_TwentyFourHour_WithSeconds_IncludesSeconds()
    {
        string formatted = ClockFormatter.FormatTime(_sampleTime, ClockTimeFormat.TwentyFourHour, showSeconds: true, _enUs);
        Assert.Equal("15:30:45", formatted);
    }

    [Fact]
    public void FormatTime_Auto_FollowsCultureConvention()
    {
        string formattedUs = ClockFormatter.FormatTime(_sampleTime, ClockTimeFormat.Auto, showSeconds: false, _enUs);
        string formattedGb = ClockFormatter.FormatTime(_sampleTime, ClockTimeFormat.Auto, showSeconds: false, _enGb);

        // en-US uses 12h
        Assert.Contains("3:30", formattedUs);
        Assert.Contains("PM", formattedUs, StringComparison.OrdinalIgnoreCase);

        // en-GB uses 24h
        Assert.Contains("15:30", formattedGb);
    }

    [Fact]
    public void FormatDateLong_FormatsAccordingToCulture()
    {
        string dateMx = ClockFormatter.FormatDateLong(_sampleTime, _esMx);
        string dateUs = ClockFormatter.FormatDateLong(_sampleTime, _enUs);
        string dateGb = ClockFormatter.FormatDateLong(_sampleTime, _enGb);

        Assert.Contains("octubre", dateMx, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("October", dateUs, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("October", dateGb, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FormatDateShort_FormatsConciseDate()
    {
        string dateUs = ClockFormatter.FormatDateShort(_sampleTime, _enUs);
        Assert.Contains("Oct", dateUs, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("2", dateUs);
    }

    [Fact]
    public void GetIsoWeekNumber_ReturnsCorrectWeek()
    {
        // 2026-10-02 is in ISO week 40
        int week = ClockFormatter.GetIsoWeekNumber(_sampleTime);
        Assert.Equal(40, week);
    }

    [Fact]
    public void FormatWeekNumber_LocalizesPrefix()
    {
        string weekEs = ClockFormatter.FormatWeekNumber(_sampleTime, _esMx);
        string weekEn = ClockFormatter.FormatWeekNumber(_sampleTime, _enUs);

        Assert.Equal("Semana 40", weekEs);
        Assert.Equal("Week 40", weekEn);
    }

    [Fact]
    public void FormatAccessibleDescription_CombinesTimeAndLongDate()
    {
        string accessible = ClockFormatter.FormatAccessibleDescription(_sampleTime, ClockTimeFormat.TwentyFourHour, showSeconds: false, _esMx);

        Assert.StartsWith("15:30", accessible);
        Assert.Contains("octubre", accessible, StringComparison.OrdinalIgnoreCase);
    }
}
