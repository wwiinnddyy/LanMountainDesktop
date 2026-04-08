using System;
using System.Collections.Generic;

namespace LanMountainDesktop.Models;

public sealed class ComponentSettingsSnapshot
{
    public string DailyArtworkMirrorSource { get; set; } = DailyArtworkMirrorSources.Overseas;

    public string? ColorSchemeSource { get; set; }

    public List<ImportedClassScheduleSnapshot> ImportedClassSchedules { get; set; } = [];

    public string ActiveImportedClassScheduleId { get; set; } = string.Empty;

    public DateOnly? SemesterStartDate { get; set; }

    public int SemesterWeekCycle { get; set; } = 1;

    public bool StudyEnvironmentShowDisplayDb { get; set; } = true;

    public bool StudyEnvironmentShowDbfs { get; set; }

    public string DesktopClockTimeZoneId { get; set; } = "China Standard Time";

    public string DesktopClockSecondHandMode { get; set; } = "Tick";

    public List<string> WorldClockTimeZoneIds { get; set; } =
    [
        "China Standard Time",
        "GMT Standard Time",
        "AUS Eastern Standard Time",
        "Eastern Standard Time"
    ];

    public string WorldClockSecondHandMode { get; set; } = "Tick";

    public bool CnrDailyNewsAutoRotateEnabled { get; set; } = true;

    public int CnrDailyNewsAutoRotateIntervalMinutes { get; set; } = 60;

    public bool IfengNewsAutoRefreshEnabled { get; set; } = true;

    public int IfengNewsAutoRefreshIntervalMinutes { get; set; } = 20;

    public string IfengNewsChannelType { get; set; } = IfengNewsChannelTypes.Comprehensive;

    public bool DailyWordAutoRefreshEnabled { get; set; } = true;

    public int DailyWordAutoRefreshIntervalMinutes { get; set; } = 360;

    public bool BilibiliHotSearchAutoRefreshEnabled { get; set; } = true;

    public int BilibiliHotSearchAutoRefreshIntervalMinutes { get; set; } = 15;

    public bool BaiduHotSearchAutoRefreshEnabled { get; set; } = true;

    public int BaiduHotSearchAutoRefreshIntervalMinutes { get; set; } = 15;

    public string BaiduHotSearchSourceType { get; set; } = BaiduHotSearchSourceTypes.Official;

    public bool WeatherAutoRefreshEnabled { get; set; } = true;

    public int WeatherAutoRefreshIntervalMinutes { get; set; } = 12;

    public int WhiteboardNoteRetentionDays { get; set; } = 15;

    public bool Stcn24ForumAutoRefreshEnabled { get; set; } = true;

    public int Stcn24ForumAutoRefreshIntervalMinutes { get; set; } = 20;

    public string Stcn24ForumSourceType { get; set; } = Stcn24ForumSourceTypes.LatestCreated;

    public List<string>? OfficeRecentDocumentsEnabledSources { get; set; }

    // 智教Hub组件配置
    public string ZhiJiaoHubSource { get; set; } = ZhiJiaoHubSources.ClassIsland;

    public string ZhiJiaoHubMirrorSource { get; set; } = ZhiJiaoHubMirrorSources.Direct;

    public bool ZhiJiaoHubAutoRefreshEnabled { get; set; } = true;

    public int ZhiJiaoHubAutoRefreshIntervalMinutes { get; set; } = 30;

    public int ZhiJiaoHubCurrentImageIndex { get; set; } = 0;

    #region Notification Box Component Settings (消息盒子组件设置)

    /// <summary>
    /// 组件内最大显示通知数量
    /// </summary>
    public int NotificationBoxMaxDisplayCount { get; set; } = 50;

    /// <summary>
    /// 排序方式：TimeDesc(时间倒序), TimeAsc(时间正序), AppGroup(按应用分组)
    /// </summary>
    public string NotificationBoxSortOrder { get; set; } = "TimeDesc";

    /// <summary>
    /// 是否显示应用图标
    /// </summary>
    public bool NotificationBoxShowAppIcon { get; set; } = true;

    /// <summary>
    /// 是否显示时间戳
    /// </summary>
    public bool NotificationBoxShowTimestamp { get; set; } = true;

    /// <summary>
    /// 时间格式：Relative(相对时间，如"5分钟前"), Absolute(绝对时间)
    /// </summary>
    public string NotificationBoxTimeFormat { get; set; } = "Relative";

    /// <summary>
    /// 是否按应用分组显示
    /// </summary>
    public bool NotificationBoxGroupByApp { get; set; } = false;

    /// <summary>
    /// 是否显示清除按钮
    /// </summary>
    public bool NotificationBoxShowClearButton { get; set; } = true;

    #endregion

    #region Shortcut Component Settings (快捷方式组件设置)

    /// <summary>
    /// 快捷方式目标路径
    /// </summary>
    public string? ShortcutTargetPath { get; set; }

    /// <summary>
    /// 点击模式：Single(单击打开) 或 Double(双击打开)
    /// </summary>
    public string ShortcutClickMode { get; set; } = "Double";

    /// <summary>
    /// 是否显示背景
    /// </summary>
    public bool ShortcutShowBackground { get; set; } = true;

    #endregion

    public ComponentSettingsSnapshot Clone()
    {
        var clone = (ComponentSettingsSnapshot)MemberwiseClone();

        var schedules = new List<ImportedClassScheduleSnapshot>(ImportedClassSchedules?.Count ?? 0);
        if (ImportedClassSchedules is not null)
        {
            foreach (var schedule in ImportedClassSchedules)
            {
                if (schedule is null)
                {
                    continue;
                }

                schedules.Add(new ImportedClassScheduleSnapshot
                {
                    Id = schedule.Id,
                    DisplayName = schedule.DisplayName,
                    FilePath = schedule.FilePath
                });
            }
        }

        clone.ImportedClassSchedules = schedules;
        clone.WorldClockTimeZoneIds = WorldClockTimeZoneIds is { Count: > 0 }
            ? new List<string>(WorldClockTimeZoneIds)
            : [];
        clone.OfficeRecentDocumentsEnabledSources = OfficeRecentDocumentsEnabledSources is not null
            ? new List<string>(OfficeRecentDocumentsEnabledSources)
            : null;

        return clone;
    }
}

// 智教Hub数据源常量
public static class ZhiJiaoHubSources
{
    public const string ClassIsland = "classisland";
    public const string Sectl = "sectl";
    public const string RinLit = "rinlit";
    public const string Jiangtokoto = "jiangtokoto";

    public static string Normalize(string? value)
    {
        return value?.ToLowerInvariant() switch
        {
            "sectl" => Sectl,
            "rinlit" => RinLit,
            "jiangtokoto" => Jiangtokoto,
            _ => ClassIsland
        };
    }

    public static string GetDisplayName(string source)
    {
        return source?.ToLowerInvariant() switch
        {
            Sectl => "SECTL 图库",
            RinLit => "Rin's 图库",
            Jiangtokoto => "Jiangtokoto 表情包",
            _ => "ClassIsland 图库"
        };
    }
}

// 智教Hub数据源配置
public sealed class ZhiJiaoHubSourceConfig
{
    public string Owner { get; init; } = string.Empty;
    public string Repo { get; init; } = string.Empty;
    public string Path { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public bool UseJsonIndex { get; init; } = false;
    public string? JsonIndexPath { get; init; } = null;
    public string ApiUrl => $"https://api.github.com/repos/{Owner}/{Repo}/contents/{Path}";
    public string RawUrlTemplate => $"https://raw.githubusercontent.com/{Owner}/{Repo}/main/{Path}/{{0}}";
    public string? JsonIndexUrl => JsonIndexPath != null
        ? $"https://raw.githubusercontent.com/{Owner}/{Repo}/main/{JsonIndexPath}"
        : null;

    public static ZhiJiaoHubSourceConfig GetConfig(string source)
    {
        return source?.ToLowerInvariant() switch
        {
            ZhiJiaoHubSources.Sectl => new ZhiJiaoHubSourceConfig
            {
                Owner = "SECTL",
                Repo = "SECTL-hub",
                Path = "docs/.vuepress/public/images",
                DisplayName = "SECTL 图库"
            },
            ZhiJiaoHubSources.RinLit => new ZhiJiaoHubSourceConfig
            {
                Owner = "RinLit-233-shiroko",
                Repo = "Rin-sHub",
                Path = "updates/images",
                DisplayName = "Rin's 图库",
                UseJsonIndex = true,
                JsonIndexPath = "updates/images.json"
            },
            ZhiJiaoHubSources.Jiangtokoto => new ZhiJiaoHubSourceConfig
            {
                Owner = "unDefFtr",
                Repo = "jiangtokoto-images",
                Path = "images",
                DisplayName = "Jiangtokoto 表情包"
            },
            _ => new ZhiJiaoHubSourceConfig
            {
                Owner = "ClassIsland",
                Repo = "classisland-hub",
                Path = "images",
                DisplayName = "ClassIsland 图库"
            }
        };
    }
}

// 智教Hub镜像加速源常量
public static class ZhiJiaoHubMirrorSources
{
    public const string Direct = "direct";
    public const string GhProxy = "gh-proxy";

    public const string GhProxyBaseUrl = "https://gh-proxy.com/";

    public static string Normalize(string? value)
    {
        return string.Equals(value, GhProxy, StringComparison.OrdinalIgnoreCase)
            ? GhProxy
            : Direct;
    }

    public static string ApplyMirror(string url, string? mirrorSource)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return url;
        }

        if (!string.Equals(Normalize(mirrorSource), GhProxy, StringComparison.OrdinalIgnoreCase))
        {
            return url;
        }

        if (url.StartsWith("https://raw.githubusercontent.com/", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase))
        {
            return GhProxyBaseUrl.TrimEnd('/') + "/" + url;
        }

        return url;
    }
}
