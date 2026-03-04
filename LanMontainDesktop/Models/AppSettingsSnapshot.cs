using System.Collections.Generic;

namespace LanMontainDesktop.Models;

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

    public List<ImportedClassScheduleSnapshot> ImportedClassSchedules { get; set; } = [];

    public string ActiveImportedClassScheduleId { get; set; } = string.Empty;

    public bool StudyEnvironmentShowDisplayDb { get; set; } = true;

    public bool StudyEnvironmentShowDbfs { get; set; }
}
