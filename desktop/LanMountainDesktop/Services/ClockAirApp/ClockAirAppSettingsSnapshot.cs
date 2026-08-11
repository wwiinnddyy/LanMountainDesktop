using System.Collections.Generic;
using System.Linq;
using LanMountainDesktop.Services;

namespace LanMountainDesktop.Services.ClockAirApp;

public sealed class ClockAirAppSettingsSnapshot
{
    public string TimeFormatMode { get; set; } = ClockAirAppTimeFormatMode.System;

    public bool ShowSeconds { get; set; } = true;

    public string StartupTab { get; set; } = ClockAirAppTabIds.Last;

    public string LastSelectedTab { get; set; } = ClockAirAppTabIds.WorldClock;

    public bool ActivateOnTimerFinished { get; set; } = true;

    public List<string> WorldClockTimeZoneIds { get; set; } =
    [
        "China Standard Time",
        "GMT Standard Time",
        "AUS Eastern Standard Time",
        "Eastern Standard Time"
    ];

    public ClockAirAppSettingsSnapshot Clone()
    {
        return new ClockAirAppSettingsSnapshot
        {
            TimeFormatMode = ClockAirAppTimeFormatMode.Normalize(TimeFormatMode),
            ShowSeconds = ShowSeconds,
            StartupTab = ClockAirAppTabIds.Normalize(StartupTab, ClockAirAppTabIds.Last),
            LastSelectedTab = ClockAirAppTabIds.Normalize(LastSelectedTab),
            ActivateOnTimerFinished = ActivateOnTimerFinished,
            WorldClockTimeZoneIds = WorldClockTimeZoneIds is { Count: > 0 }
                ? new List<string>(WorldClockTimeZoneIds.Where(static id => !string.IsNullOrWhiteSpace(id)).Select(static id => id.Trim()))
                : []
        };
    }

    public static ClockAirAppSettingsSnapshot Normalize(ClockAirAppSettingsSnapshot? snapshot)
    {
        var normalized = (snapshot ?? new ClockAirAppSettingsSnapshot()).Clone();
        if (normalized.WorldClockTimeZoneIds.Count == 0)
        {
            normalized.WorldClockTimeZoneIds =
            [
                "China Standard Time",
                "GMT Standard Time",
                "AUS Eastern Standard Time",
                "Eastern Standard Time"
            ];
        }

        normalized.WorldClockTimeZoneIds = normalized.WorldClockTimeZoneIds
            .Select(static id => WorldClockTimeZoneCatalog.ResolveTimeZoneOrLocal(id).Id)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return normalized;
    }
}
