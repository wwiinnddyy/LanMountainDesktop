using System;
using System.Collections.Generic;
using Avalonia.Media.Imaging;
using LanMountainDesktop.Models;
using LanMountainDesktop.Services;
using LanMountainDesktop.Views.Components;

namespace LanMountainDesktop.Views;

public partial class SettingsWindow
{
    private const int StatusBarRowIndex = 0;
    private const string UpdateChannelStable = "Stable";
    private const string UpdateChannelPreview = "Preview";
    private const string AppCodeName = "Administrate";
    private const string AppFontName = "MiSans";
    private const string FallbackAppVersion = "1.0.0";

    private static readonly IReadOnlyDictionary<string, string> ZhTimeZoneNames =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["China Standard Time"] = "中国标准时间",
            ["Asia/Shanghai"] = "中国标准时间",
            ["Tokyo Standard Time"] = "日本标准时间",
            ["Asia/Tokyo"] = "日本标准时间",
            ["Pacific Standard Time"] = "太平洋标准时间",
            ["America/Los_Angeles"] = "太平洋标准时间",
            ["Eastern Standard Time"] = "美国东部标准时间",
            ["America/New_York"] = "美国东部标准时间",
            ["Central European Standard Time"] = "中欧标准时间",
            ["Europe/Berlin"] = "中欧标准时间",
            ["GMT Standard Time"] = "格林威治标准时间",
            ["Europe/London"] = "格林威治标准时间",
            ["UTC"] = "协调世界时",
            ["Etc/UTC"] = "协调世界时"
        };

    private enum LauncherEntryKind
    {
        Folder,
        Shortcut
    }

    private sealed record LauncherHiddenItemToken(LauncherEntryKind Kind, string Key);

    private sealed record LauncherHiddenItemView(
        LauncherEntryKind Kind,
        string Key,
        string DisplayName,
        string Monogram,
        Bitmap? IconBitmap);

    private readonly Dictionary<string, Bitmap> _launcherIconCache = new(StringComparer.OrdinalIgnoreCase);

    private ClockDisplayFormat _clockDisplayFormat = ClockDisplayFormat.HourMinuteSecond;
    private bool _suppressStatusBarToggleEvents;

    private bool _autoCheckUpdates = true;
    private string _updateChannel = UpdateChannelStable;
    private bool _suppressUpdateOptionEvents;
    private bool _isCheckingUpdates;
    private bool _isDownloadingUpdate;
    private string _latestReleaseVersionText = "-";
    private DateTimeOffset? _latestReleasePublishedAt;
    private string _updateStatusText = string.Empty;
    private string _updateDownloadProgressText = string.Empty;
    private double _updateDownloadProgressPercent;
    private GitHubReleaseAsset? _latestReleaseInstallerAsset;
    private string? _downloadedUpdateInstallerPath;
    private IDisposable? _persistSettingsDebounceTimer;

    private bool IncludePrereleaseUpdates => string.Equals(
        _updateChannel,
        UpdateChannelPreview,
        StringComparison.OrdinalIgnoreCase);
}
