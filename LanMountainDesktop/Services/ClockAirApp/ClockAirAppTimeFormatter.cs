using System;
using System.Collections.Generic;
using System.Globalization;

namespace LanMountainDesktop.Services.ClockAirApp;

public static class ClockAirAppTimeFormatter
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> CityNames =
        new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["zh-CN"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["China Standard Time"] = "北京",
                ["Asia/Shanghai"] = "北京",
                ["GMT Standard Time"] = "伦敦",
                ["Europe/London"] = "伦敦",
                ["AUS Eastern Standard Time"] = "悉尼",
                ["Australia/Sydney"] = "悉尼",
                ["Eastern Standard Time"] = "纽约",
                ["America/New_York"] = "纽约",
                ["Tokyo Standard Time"] = "东京",
                ["Asia/Tokyo"] = "东京",
                ["UTC"] = "UTC",
                ["Etc/UTC"] = "UTC"
            },
            ["en-US"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["China Standard Time"] = "Beijing",
                ["Asia/Shanghai"] = "Beijing",
                ["GMT Standard Time"] = "London",
                ["Europe/London"] = "London",
                ["AUS Eastern Standard Time"] = "Sydney",
                ["Australia/Sydney"] = "Sydney",
                ["Eastern Standard Time"] = "New York",
                ["America/New_York"] = "New York",
                ["Tokyo Standard Time"] = "Tokyo",
                ["Asia/Tokyo"] = "Tokyo",
                ["UTC"] = "UTC",
                ["Etc/UTC"] = "UTC"
            },
            ["ja-JP"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["China Standard Time"] = "北京",
                ["Asia/Shanghai"] = "北京",
                ["GMT Standard Time"] = "ロンドン",
                ["Europe/London"] = "ロンドン",
                ["AUS Eastern Standard Time"] = "シドニー",
                ["Australia/Sydney"] = "シドニー",
                ["Eastern Standard Time"] = "ニューヨーク",
                ["America/New_York"] = "ニューヨーク",
                ["Tokyo Standard Time"] = "東京",
                ["Asia/Tokyo"] = "東京",
                ["UTC"] = "UTC",
                ["Etc/UTC"] = "UTC"
            },
            ["ko-KR"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["China Standard Time"] = "베이징",
                ["Asia/Shanghai"] = "베이징",
                ["GMT Standard Time"] = "런던",
                ["Europe/London"] = "런던",
                ["AUS Eastern Standard Time"] = "시드니",
                ["Australia/Sydney"] = "시드니",
                ["Eastern Standard Time"] = "뉴욕",
                ["America/New_York"] = "뉴욕",
                ["Tokyo Standard Time"] = "도쿄",
                ["Asia/Tokyo"] = "도쿄",
                ["UTC"] = "UTC",
                ["Etc/UTC"] = "UTC"
            }
        };

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

    public static string ResolveCityName(TimeZoneInfo timeZone, string languageCode)
    {
        var normalizedLanguage = NormalizeLanguage(languageCode);
        if (CityNames.TryGetValue(normalizedLanguage, out var cityNames) &&
            cityNames.TryGetValue(timeZone.Id, out var cityName))
        {
            return cityName;
        }

        var normalized = timeZone.Id;
        var slashIndex = normalized.LastIndexOf('/');
        if (slashIndex >= 0 && slashIndex < normalized.Length - 1)
        {
            normalized = normalized[(slashIndex + 1)..];
        }

        normalized = normalized.Replace('_', ' ').Trim();
        normalized = normalized
            .Replace("Standard Time", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("Daylight Time", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("Time", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Trim();

        return string.IsNullOrWhiteSpace(normalized) ? timeZone.Id : normalized;
    }

    public static bool UseTwentyFourHourClock(string? timeFormatMode, CultureInfo culture)
    {
        return ClockAirAppTimeFormatMode.Normalize(timeFormatMode) switch
        {
            ClockAirAppTimeFormatMode.TwentyFourHour => true,
            ClockAirAppTimeFormatMode.TwelveHour => false,
            _ => !culture.DateTimeFormat.ShortTimePattern.Contains('h')
        };
    }

    private static string NormalizeLanguage(string? languageCode)
    {
        return languageCode?.Trim().ToLowerInvariant() switch
        {
            "en" or "en-us" => "en-US",
            "ja" or "ja-jp" => "ja-JP",
            "ko" or "ko-kr" => "ko-KR",
            _ => "zh-CN"
        };
    }
}
