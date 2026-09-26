using System;
using System.Globalization;

namespace LanMountainDesktop.Services.ClockAirApp;

public static class ClockAirAppTimeFormatter
{
    public static string FormatTime(DateTime time, ClockAirAppSettingsSnapshot settings, CultureInfo culture)
    {
        var use24Hour = UseTwentyFourHourClock(settings.TimeFormatMode, culture);
        var showSeconds = settings.ShowSeconds;
        var format = use24Hour
            ? showSeconds ? "HH:mm:ss" : "HH:mm"
            : showSeconds ? "h:mm:ss tt" : "h:mm tt";
        return time.ToString(format, culture);
    }

    public static string FormatDuration(TimeSpan duration, bool includeMilliseconds = false)
    {
        if (duration < TimeSpan.Zero)
        {
            duration = TimeSpan.Zero;
        }

        var totalHours = (int)duration.TotalHours;
        return includeMilliseconds
            ? string.Create(CultureInfo.InvariantCulture, $"{totalHours:D2}:{duration.Minutes:D2}:{duration.Seconds:D2}.{duration.Milliseconds / 10:D2}")
            : string.Create(CultureInfo.InvariantCulture, $"{totalHours:D2}:{duration.Minutes:D2}:{duration.Seconds:D2}");
    }

    public static string FormatUtcOffset(TimeSpan offset)
    {
        var sign = offset >= TimeSpan.Zero ? "+" : "-";
        var totalMinutes = Math.Abs((int)Math.Round(offset.TotalMinutes));
        var hours = totalMinutes / 60;
        var minutes = totalMinutes % 60;
        return $"UTC{sign}{hours:D2}:{minutes:D2}";
    }

    /// <summary>
    /// 城市名一律走 <see cref="ClockCityNames"/>：这张表与"哪张表配哪种语言"在两处界面上出现过
    /// 三份抄本，症状是同一颗时区在桌面上写"東京"、在 AirApp 里写 Tokyo。
    /// </summary>
    public static string ResolveCityName(TimeZoneInfo timeZone, string languageCode) =>
        ClockCityNames.ResolveByLanguage(languageCode, timeZone);

    public static bool UseTwentyFourHourClock(string? timeFormatMode, CultureInfo culture)
    {
        return ClockAirAppTimeFormatMode.Normalize(timeFormatMode) switch
        {
            ClockAirAppTimeFormatMode.TwentyFourHour => true,
            ClockAirAppTimeFormatMode.TwelveHour => false,
            _ => !culture.DateTimeFormat.ShortTimePattern.Contains('h')
        };
    }
}
