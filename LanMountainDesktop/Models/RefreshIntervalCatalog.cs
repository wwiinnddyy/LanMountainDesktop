using System;
using System.Collections.Generic;
using System.Linq;

namespace LanMountainDesktop.Models;

public static class RefreshIntervalCatalog
{
    public static IReadOnlyList<int> SupportedIntervalsMinutes { get; } =
    [
        5,
        10,
        12,
        15,
        20,
        30,
        40,
        60,
        180,
        360,
        720,
        1440
    ];

    public static int Normalize(int minutes, int fallbackMinutes)
    {
        if (minutes <= 0)
        {
            return fallbackMinutes;
        }

        if (SupportedIntervalsMinutes.Contains(minutes))
        {
            return minutes;
        }

        return SupportedIntervalsMinutes
            .OrderBy(value => Math.Abs(value - minutes))
            .FirstOrDefault(fallbackMinutes);
    }

    public static string ToLocalizationKeySuffix(int minutes)
    {
        return minutes switch
        {
            5 => "5m",
            10 => "10m",
            12 => "12m",
            15 => "15m",
            20 => "20m",
            30 => "30m",
            40 => "40m",
            60 => "1h",
            180 => "3h",
            360 => "6h",
            720 => "12h",
            1440 => "24h",
            _ => $"{minutes}m"
        };
    }

    public static string ToEnglishFallbackLabel(int minutes)
    {
        return minutes switch
        {
            60 => "1 hour",
            180 => "3 hours",
            360 => "6 hours",
            720 => "12 hours",
            1440 => "24 hours",
            _ => $"{minutes} min"
        };
    }
}
