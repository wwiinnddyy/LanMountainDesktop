using System.Collections.Generic;

namespace LanMountainDesktop.Models;

public sealed class AppSettingsSnapshot
{
    public int GridShortSideCells { get; set; } = 12;

    public string GridSpacingPreset { get; set; } = "Relaxed";

    public int DesktopEdgeInsetPercent { get; set; } = 18;

    public bool? IsNightMode { get; set; }

    public string? ThemeColor { get; set; }

    public bool UseSystemChrome { get; set; }

    public string ThemeColorMode { get; set; } = "default_neutral";

    public string SystemMaterialMode { get; set; } = "none";

    public string? SelectedWallpaperSeed { get; set; }

    public string? WallpaperPath { get; set; }

    public string WallpaperType { get; set; } = "Image";

    public string? WallpaperColor { get; set; }

    public string WallpaperPlacement { get; set; } = "Fill";

    public int SettingsTabIndex { get; set; } = 0;

    public string? SettingsTabTag { get; set; }

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

    public string WeatherIconPackId { get; set; } = "HyperOS3";

    public bool WeatherNoTlsRequests { get; set; }

    public bool AutoStartWithWindows { get; set; }

    public string AppRenderMode { get; set; } = "Default";

    public bool IncludePrereleaseUpdates { get; set; }

    public bool UploadAnonymousCrashData { get; set; }

    public bool UploadAnonymousUsageData { get; set; }

    public string? DeviceId { get; set; }

    public string? PersistentUserId { get; set; }

    public string UpdateChannel { get; set; } = "stable";

    public string UpdateMode { get; set; } = "download_then_confirm";

    public string UpdateDownloadSource { get; set; } = "github";

    public int UpdateDownloadThreads { get; set; } = 4;

    public string? PendingUpdateInstallerPath { get; set; }

    public string? PendingUpdateVersion { get; set; }

    public long? PendingUpdatePublishedAtUtcMs { get; set; }

    public long? LastUpdateCheckUtcMs { get; set; }

    public List<string> TopStatusComponentIds { get; set; } = [];

    public List<string> PinnedTaskbarActions { get; set; } =
    [
        TaskbarActionId.MinimizeToWindows.ToString()
    ];

    public bool EnableDynamicTaskbarActions { get; set; } = true;

    public string TaskbarLayoutMode { get; set; } = "BottomFullRowMacStyle";

    public string ClockDisplayFormat { get; set; } = "HourMinuteSecond";

    public bool StatusBarClockTransparentBackground { get; set; }

    public string StatusBarSpacingMode { get; set; } = "Relaxed";

    public int StatusBarCustomSpacingPercent { get; set; } = 12;

    public List<string> DisabledPluginIds { get; set; } = [];

    public AppSettingsSnapshot Clone()
    {
        var clone = (AppSettingsSnapshot)MemberwiseClone();

        clone.TopStatusComponentIds = TopStatusComponentIds is { Count: > 0 }
            ? new List<string>(TopStatusComponentIds)
            : [];
        clone.PinnedTaskbarActions = PinnedTaskbarActions is { Count: > 0 }
            ? new List<string>(PinnedTaskbarActions)
            : [];
        clone.DisabledPluginIds = DisabledPluginIds is { Count: > 0 }
            ? new List<string>(DisabledPluginIds)
            : [];

        return clone;
    }
}
