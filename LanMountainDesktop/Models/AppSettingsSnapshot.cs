using System.Collections.Generic;

namespace LanMountainDesktop.Models;

public sealed class AppSettingsSnapshot
{
    public int GridShortSideCells { get; set; } = 12;

    public string GridSpacingPreset { get; set; } = "Relaxed";

    public int DesktopEdgeInsetPercent { get; set; } = 18;

    public bool? IsNightMode { get; set; }

    public string? ThemeColor { get; set; }

    public string? WallpaperPath { get; set; }

    public string WallpaperPlacement { get; set; } = "Fill";

    public int SettingsTabIndex { get; set; } = 0;

    public string LanguageCode { get; set; } = "zh-CN";

    public string? TimeZoneId { get; set; }

    public string WeatherLocationMode { get; set; } = "CitySearch";

    public string WeatherLocationKey { get; set; } = string.Empty;

    public string WeatherLocationName { get; set; } = string.Empty;

    public double WeatherLatitude { get; set; } = 39.9042;

    public double WeatherLongitude { get; set; } = 116.4074;

    public bool WeatherAutoRefreshLocation { get; set; }

    public string WeatherLocationQuery { get; set; } = string.Empty;

    public string WeatherExcludedAlerts { get; set; } = string.Empty;

    public string WeatherIconPackId { get; set; } = "FluentRegular";

    public bool WeatherNoTlsRequests { get; set; }

    public bool AutoStartWithWindows { get; set; }

    public bool AutoCheckUpdates { get; set; } = true;

    public bool IncludePrereleaseUpdates { get; set; }

    public string UpdateChannel { get; set; } = string.Empty;

    public List<string> TopStatusComponentIds { get; set; } = [];

    public List<string> PinnedTaskbarActions { get; set; } =
    [
        TaskbarActionId.MinimizeToWindows.ToString(),
        TaskbarActionId.OpenSettings.ToString()
    ];

    public bool EnableDynamicTaskbarActions { get; set; } = true;

    public string TaskbarLayoutMode { get; set; } = "BottomFullRowMacStyle";

    public string ClockDisplayFormat { get; set; } = "HourMinuteSecond";

    public string StatusBarSpacingMode { get; set; } = "Relaxed";

    public int StatusBarCustomSpacingPercent { get; set; } = 12;

    public int DesktopPageCount { get; set; } = 1;

    public int CurrentDesktopSurfaceIndex { get; set; } = 0;

    public List<DesktopComponentPlacementSnapshot> DesktopComponentPlacements { get; set; } = [];

    public List<string> HiddenLauncherFolderPaths { get; set; } = [];

    public List<string> HiddenLauncherAppPaths { get; set; } = [];

    public AppSettingsSnapshot Clone()
    {
        var clone = (AppSettingsSnapshot)MemberwiseClone();

        clone.TopStatusComponentIds = TopStatusComponentIds is { Count: > 0 }
            ? new List<string>(TopStatusComponentIds)
            : [];
        clone.PinnedTaskbarActions = PinnedTaskbarActions is { Count: > 0 }
            ? new List<string>(PinnedTaskbarActions)
            : [];

        var placements = new List<DesktopComponentPlacementSnapshot>(DesktopComponentPlacements?.Count ?? 0);
        if (DesktopComponentPlacements is not null)
        {
            foreach (var placement in DesktopComponentPlacements)
            {
                if (placement is null)
                {
                    continue;
                }

                placements.Add(new DesktopComponentPlacementSnapshot
                {
                    PlacementId = placement.PlacementId,
                    PageIndex = placement.PageIndex,
                    ComponentId = placement.ComponentId,
                    Row = placement.Row,
                    Column = placement.Column,
                    WidthCells = placement.WidthCells,
                    HeightCells = placement.HeightCells
                });
            }
        }
        clone.DesktopComponentPlacements = placements;
        clone.HiddenLauncherFolderPaths = HiddenLauncherFolderPaths is { Count: > 0 }
            ? new List<string>(HiddenLauncherFolderPaths)
            : [];
        clone.HiddenLauncherAppPaths = HiddenLauncherAppPaths is { Count: > 0 }
            ? new List<string>(HiddenLauncherAppPaths)
            : [];

        return clone;
    }
}
