using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using LanMountainDesktop.Models;
using LanMountainDesktop.PluginSdk;
using LanMountainDesktop.Services;
using LanMountainDesktop.Settings.Core;
using LanMountainDesktop.Services.PluginMarket;

namespace LanMountainDesktop.Services.Settings;

internal sealed class GridSettingsService : IGridSettingsService
{
    private readonly ISettingsService _settingsService;
    private readonly DesktopGridLayoutService _gridLayoutService = new();

    public GridSettingsService(ISettingsService settingsService)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
    }

    public GridSettingsState Get()
    {
        var snapshot = _settingsService.Load();
        return new GridSettingsState(
            snapshot.GridShortSideCells,
            snapshot.GridSpacingPreset,
            snapshot.DesktopEdgeInsetPercent);
    }

    public void Save(GridSettingsState state)
    {
        var snapshot = _settingsService.Load();
        snapshot.GridShortSideCells = state.ShortSideCells;
        snapshot.GridSpacingPreset = state.SpacingPreset;
        snapshot.DesktopEdgeInsetPercent = state.EdgeInsetPercent;
        _settingsService.SaveSnapshot(
            SettingsScope.App,
            snapshot,
            changedKeys:
            [
                nameof(AppSettingsSnapshot.GridShortSideCells),
                nameof(AppSettingsSnapshot.GridSpacingPreset),
                nameof(AppSettingsSnapshot.DesktopEdgeInsetPercent)
            ]);
    }

    public string NormalizeSpacingPreset(string? value)
    {
        return _gridLayoutService.NormalizeSpacingPreset(value);
    }

    public double ResolveGapRatio(string? preset)
    {
        return _gridLayoutService.ResolveGapRatio(preset);
    }

    public double CalculateEdgeInset(double hostWidth, double hostHeight, int shortSideCells, int insetPercent)
    {
        return _gridLayoutService.CalculateEdgeInset(hostWidth, hostHeight, shortSideCells, insetPercent);
    }

    public DesktopGridMetrics CalculateGridMetrics(
        double hostWidth,
        double hostHeight,
        int shortSideCells,
        double gapRatio,
        double edgeInsetPx)
    {
        return _gridLayoutService.CalculateGridMetrics(
            hostWidth,
            hostHeight,
            shortSideCells,
            gapRatio,
            edgeInsetPx);
    }
}

internal sealed class WallpaperSettingsService : IWallpaperSettingsService
{
    private readonly ISettingsService _settingsService;

    public WallpaperSettingsService(ISettingsService settingsService)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
    }

    public WallpaperSettingsState Get()
    {
        var snapshot = _settingsService.Load();
        var normalizedType = snapshot.WallpaperType ?? "Image";
        return new WallpaperSettingsState(
            string.Equals(normalizedType, "SolidColor", StringComparison.OrdinalIgnoreCase)
                ? null
                : snapshot.WallpaperPath,
            normalizedType,
            snapshot.WallpaperColor,
            snapshot.WallpaperPlacement,
            CustomColor: null,
            SystemWallpaperRefreshIntervalSeconds: NormalizeRefreshInterval(snapshot.SystemWallpaperRefreshIntervalSeconds));
    }

    public void Save(WallpaperSettingsState state)
    {
        var snapshot = _settingsService.Load();
        var normalizedType = string.IsNullOrWhiteSpace(state.Type)
            ? "Image"
            : state.Type.Trim();
        var normalizedPath = string.IsNullOrWhiteSpace(state.WallpaperPath)
            ? null
            : state.WallpaperPath.Trim();
        var normalizedColor = string.IsNullOrWhiteSpace(state.Color)
            ? null
            : state.Color.Trim();

        if (string.Equals(normalizedType, "SolidColor", StringComparison.OrdinalIgnoreCase))
        {
            normalizedPath = null;
        }

        snapshot.WallpaperPath = normalizedPath;
        snapshot.WallpaperType = normalizedType;
        snapshot.WallpaperColor = normalizedColor;
        snapshot.WallpaperPlacement = string.IsNullOrWhiteSpace(state.Placement)
            ? "Fill"
            : state.Placement.Trim();
        snapshot.SystemWallpaperRefreshIntervalSeconds = NormalizeRefreshInterval(state.SystemWallpaperRefreshIntervalSeconds);
        _settingsService.SaveSnapshot(
            SettingsScope.App,
            snapshot,
            changedKeys:
            [
                nameof(AppSettingsSnapshot.WallpaperPath),
                nameof(AppSettingsSnapshot.WallpaperType),
                nameof(AppSettingsSnapshot.WallpaperColor),
                nameof(AppSettingsSnapshot.WallpaperPlacement),
                nameof(AppSettingsSnapshot.SystemWallpaperRefreshIntervalSeconds)
            ]);
    }

    private static int NormalizeRefreshInterval(int seconds)
    {
        return seconds switch
        {
            <= 0 => 300,
            < 30 => 30,
            > 86400 => 86400,
            _ => seconds
        };
    }
}

internal sealed class WallpaperMediaService : IWallpaperMediaService
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp"
    };

    private readonly string _wallpapersDirectory;

    public WallpaperMediaService()
    {
        var appDataRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LanMountainDesktop");
        _wallpapersDirectory = Path.Combine(appDataRoot, "Wallpapers");
    }

    public WallpaperMediaType DetectMediaType(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return WallpaperMediaType.None;
        }

        var extension = Path.GetExtension(path.Trim());
        if (string.IsNullOrWhiteSpace(extension))
        {
            return WallpaperMediaType.None;
        }

        if (ImageExtensions.Contains(extension))
        {
            return WallpaperMediaType.Image;
        }

        return WallpaperMediaType.None;
    }

    public async Task<string?> ImportAssetAsync(string sourcePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            return null;
        }

        var fullSourcePath = Path.GetFullPath(sourcePath);
        if (!File.Exists(fullSourcePath))
        {
            return null;
        }

        if (DetectMediaType(fullSourcePath) == WallpaperMediaType.None)
        {
            return null;
        }

        Directory.CreateDirectory(_wallpapersDirectory);

        var extension = Path.GetExtension(fullSourcePath);
        var baseName = Path.GetFileNameWithoutExtension(fullSourcePath);
        var normalizedBaseName = string.IsNullOrWhiteSpace(baseName)
            ? "wallpaper"
            : string.Concat(baseName.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));

        var destinationPath = Path.Combine(_wallpapersDirectory, $"{normalizedBaseName}{extension}");
        if (string.Equals(fullSourcePath, destinationPath, StringComparison.OrdinalIgnoreCase))
        {
            return destinationPath;
        }

        var suffix = 1;
        while (File.Exists(destinationPath))
        {
            destinationPath = Path.Combine(_wallpapersDirectory, $"{normalizedBaseName}_{suffix}{extension}");
            suffix++;
        }

        await using var source = File.OpenRead(fullSourcePath);
        await using var destination = File.Create(destinationPath);
        await source.CopyToAsync(destination, cancellationToken);
        return destinationPath;
    }
}

internal sealed class ThemeAppearanceService : IThemeAppearanceService
{
    private readonly ISettingsService _settingsService;
    private readonly MonetColorService _monetColorService = new();
    private readonly WallpaperMediaService _wallpaperMediaService = new();

    public ThemeAppearanceService(ISettingsService settingsService)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
    }

    public ThemeAppearanceSettingsState Get()
    {
        var snapshot = _settingsService.Load();
        var cornerRadiusStyle = GlobalAppearanceSettings.NormalizeCornerRadiusStyle(snapshot.CornerRadiusStyle);
        if (string.Equals(cornerRadiusStyle, GlobalAppearanceSettings.DefaultCornerRadiusStyle, StringComparison.OrdinalIgnoreCase) &&
            string.IsNullOrWhiteSpace(snapshot.CornerRadiusStyle) &&
            Math.Abs(snapshot.GlobalCornerRadiusScale - GlobalAppearanceSettings.DefaultCornerRadiusScale) > 0.01)
        {
            cornerRadiusStyle = GlobalAppearanceSettings.MigrateScaleToStyle(snapshot.GlobalCornerRadiusScale);
        }

        return new ThemeAppearanceSettingsState(
            snapshot.IsNightMode ?? false,
            snapshot.ThemeColor,
            snapshot.UseSystemChrome,
            cornerRadiusStyle,
            ThemeAppearanceValues.NormalizeThemeColorMode(snapshot.ThemeColorMode, snapshot.ThemeColor),
            ThemeAppearanceValues.NormalizeSystemMaterialMode(snapshot.SystemMaterialMode),
            snapshot.SelectedWallpaperSeed);
    }

    public void Save(ThemeAppearanceSettingsState state)
    {
        var snapshot = _settingsService.Load();
        var changedKeys = new List<string>();
        var normalizedThemeColor = string.IsNullOrWhiteSpace(state.ThemeColor) ? null : state.ThemeColor;
        var normalizedCornerRadiusStyle = GlobalAppearanceSettings.NormalizeCornerRadiusStyle(state.CornerRadiusStyle);
        var normalizedThemeColorMode = ThemeAppearanceValues.NormalizeThemeColorMode(state.ThemeColorMode, state.ThemeColor);
        var normalizedSystemMaterialMode = ThemeAppearanceValues.NormalizeSystemMaterialMode(state.SystemMaterialMode);
        var normalizedSelectedWallpaperSeed = string.IsNullOrWhiteSpace(state.SelectedWallpaperSeed)
            ? null
            : state.SelectedWallpaperSeed;

        if ((snapshot.IsNightMode ?? false) != state.IsNightMode)
        {
            snapshot.IsNightMode = state.IsNightMode;
            changedKeys.Add(nameof(AppSettingsSnapshot.IsNightMode));
        }

        if (!string.Equals(snapshot.ThemeColor, normalizedThemeColor, StringComparison.OrdinalIgnoreCase))
        {
            snapshot.ThemeColor = normalizedThemeColor;
            changedKeys.Add(nameof(AppSettingsSnapshot.ThemeColor));
        }

        if (snapshot.UseSystemChrome != state.UseSystemChrome)
        {
            snapshot.UseSystemChrome = state.UseSystemChrome;
            changedKeys.Add(nameof(AppSettingsSnapshot.UseSystemChrome));
        }

        if (!string.Equals(GlobalAppearanceSettings.NormalizeCornerRadiusStyle(snapshot.CornerRadiusStyle), normalizedCornerRadiusStyle, StringComparison.OrdinalIgnoreCase))
        {
            snapshot.CornerRadiusStyle = normalizedCornerRadiusStyle;
            changedKeys.Add(nameof(AppSettingsSnapshot.CornerRadiusStyle));
        }

        if (!string.Equals(snapshot.ThemeColorMode, normalizedThemeColorMode, StringComparison.OrdinalIgnoreCase))
        {
            snapshot.ThemeColorMode = normalizedThemeColorMode;
            changedKeys.Add(nameof(AppSettingsSnapshot.ThemeColorMode));
        }

        if (!string.Equals(snapshot.SystemMaterialMode, normalizedSystemMaterialMode, StringComparison.OrdinalIgnoreCase))
        {
            snapshot.SystemMaterialMode = normalizedSystemMaterialMode;
            changedKeys.Add(nameof(AppSettingsSnapshot.SystemMaterialMode));
        }

        if (!string.Equals(snapshot.SelectedWallpaperSeed, normalizedSelectedWallpaperSeed, StringComparison.OrdinalIgnoreCase))
        {
            snapshot.SelectedWallpaperSeed = normalizedSelectedWallpaperSeed;
            changedKeys.Add(nameof(AppSettingsSnapshot.SelectedWallpaperSeed));
        }

        if (changedKeys.Count == 0)
        {
            return;
        }

        _settingsService.SaveSnapshot(
            SettingsScope.App,
            snapshot,
            changedKeys: changedKeys);
    }

    public MonetPalette BuildPalette(bool nightMode, string? wallpaperPath, string? preferredSeedColor = null)
    {
        Bitmap? bitmap = null;
        Color? preferredSeed = null;

        if (!string.IsNullOrWhiteSpace(preferredSeedColor) && Color.TryParse(preferredSeedColor, out var parsedSeed))
        {
            preferredSeed = parsedSeed;
        }

        try
        {
            if (_wallpaperMediaService.DetectMediaType(wallpaperPath) == WallpaperMediaType.Image &&
                !string.IsNullOrWhiteSpace(wallpaperPath) &&
                File.Exists(wallpaperPath))
            {
                bitmap = new Bitmap(wallpaperPath);
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn(
                "Settings.Theme",
                $"Failed to load wallpaper bitmap for palette generation. Path='{wallpaperPath}'.",
                ex);
        }

        try
        {
            return _monetColorService.BuildPalette(bitmap, nightMode, preferredSeed);
        }
        finally
        {
            bitmap?.Dispose();
        }
    }
}

internal sealed class StatusBarSettingsService : IStatusBarSettingsService
{
    private readonly ISettingsService _settingsService;

    public StatusBarSettingsService(ISettingsService settingsService)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
    }

    public StatusBarSettingsState Get()
    {
        var snapshot = _settingsService.Load();
        return new StatusBarSettingsState(
            snapshot.TopStatusComponentIds?.ToArray() ?? [],
            snapshot.PinnedTaskbarActions?.ToArray() ?? [],
            snapshot.EnableDynamicTaskbarActions,
            snapshot.TaskbarLayoutMode,
            snapshot.ClockDisplayFormat,
            snapshot.StatusBarClockTransparentBackground,
            snapshot.ClockPosition,
            snapshot.ClockFontSize,
            snapshot.ShowTextCapsule,
            snapshot.TextCapsuleContent,
            snapshot.TextCapsulePosition,
            snapshot.TextCapsuleTransparentBackground,
            snapshot.TextCapsuleFontSize,
            snapshot.ShowNetworkSpeed,
            snapshot.NetworkSpeedPosition,
            snapshot.NetworkSpeedDisplayMode,
            snapshot.NetworkSpeedTransparentBackground,
            snapshot.ShowNetworkTypeIcon,
            snapshot.NetworkSpeedFontSize,
            snapshot.StatusBarSpacingMode,
            snapshot.StatusBarCustomSpacingPercent,
            snapshot.StatusBarShadowEnabled,
            snapshot.StatusBarShadowColor,
            snapshot.StatusBarShadowOpacity);
    }

    public void Save(StatusBarSettingsState state)
    {
        var snapshot = _settingsService.Load();
        snapshot.TopStatusComponentIds = state.TopStatusComponentIds?.ToList() ?? [];
        snapshot.PinnedTaskbarActions = state.PinnedTaskbarActions?.ToList() ?? [];
        snapshot.EnableDynamicTaskbarActions = state.EnableDynamicTaskbarActions;
        snapshot.TaskbarLayoutMode = state.TaskbarLayoutMode;
        snapshot.ClockDisplayFormat = state.ClockDisplayFormat;
        snapshot.StatusBarClockTransparentBackground = state.ClockTransparentBackground;
        snapshot.ClockPosition = state.ClockPosition;
        snapshot.ClockFontSize = state.ClockFontSize;
        snapshot.ShowTextCapsule = state.ShowTextCapsule;
        snapshot.TextCapsuleContent = state.TextCapsuleContent;
        snapshot.TextCapsulePosition = state.TextCapsulePosition;
        snapshot.TextCapsuleTransparentBackground = state.TextCapsuleTransparentBackground;
        snapshot.TextCapsuleFontSize = state.TextCapsuleFontSize;
        snapshot.ShowNetworkSpeed = state.ShowNetworkSpeed;
        snapshot.NetworkSpeedPosition = state.NetworkSpeedPosition;
        snapshot.NetworkSpeedDisplayMode = state.NetworkSpeedDisplayMode;
        snapshot.NetworkSpeedTransparentBackground = state.NetworkSpeedTransparentBackground;
        snapshot.ShowNetworkTypeIcon = state.ShowNetworkTypeIcon;
        snapshot.NetworkSpeedFontSize = state.NetworkSpeedFontSize;
        snapshot.StatusBarSpacingMode = state.SpacingMode;
        snapshot.StatusBarCustomSpacingPercent = state.CustomSpacingPercent;
        snapshot.StatusBarShadowEnabled = state.ShadowEnabled;
        snapshot.StatusBarShadowColor = state.ShadowColor;
        snapshot.StatusBarShadowOpacity = state.ShadowOpacity;
        _settingsService.SaveSnapshot(
            SettingsScope.App,
            snapshot,
            changedKeys:
            [
                nameof(AppSettingsSnapshot.TopStatusComponentIds),
                nameof(AppSettingsSnapshot.PinnedTaskbarActions),
                nameof(AppSettingsSnapshot.EnableDynamicTaskbarActions),
                nameof(AppSettingsSnapshot.TaskbarLayoutMode),
                nameof(AppSettingsSnapshot.ClockDisplayFormat),
                nameof(AppSettingsSnapshot.StatusBarClockTransparentBackground),
                nameof(AppSettingsSnapshot.ClockPosition),
                nameof(AppSettingsSnapshot.ClockFontSize),
                nameof(AppSettingsSnapshot.ShowTextCapsule),
                nameof(AppSettingsSnapshot.TextCapsuleContent),
                nameof(AppSettingsSnapshot.TextCapsulePosition),
                nameof(AppSettingsSnapshot.TextCapsuleTransparentBackground),
                nameof(AppSettingsSnapshot.TextCapsuleFontSize),
                nameof(AppSettingsSnapshot.ShowNetworkSpeed),
                nameof(AppSettingsSnapshot.NetworkSpeedPosition),
                nameof(AppSettingsSnapshot.NetworkSpeedDisplayMode),
                nameof(AppSettingsSnapshot.NetworkSpeedTransparentBackground),
                nameof(AppSettingsSnapshot.ShowNetworkTypeIcon),
                nameof(AppSettingsSnapshot.NetworkSpeedFontSize),
                nameof(AppSettingsSnapshot.StatusBarSpacingMode),
                nameof(AppSettingsSnapshot.StatusBarCustomSpacingPercent),
                nameof(AppSettingsSnapshot.StatusBarShadowEnabled),
                nameof(AppSettingsSnapshot.StatusBarShadowColor),
                nameof(AppSettingsSnapshot.StatusBarShadowOpacity)
            ]);
    }
}

internal sealed class TextCapsuleSettingsService : ITextCapsuleSettingsService
{
    private readonly ISettingsService _settingsService;

    public TextCapsuleSettingsService(ISettingsService settingsService)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
    }

    public TextCapsuleSettingsState Get()
    {
        var snapshot = _settingsService.Load();
        return new TextCapsuleSettingsState(
            snapshot.ShowTextCapsule,
            snapshot.TextCapsuleContent,
            snapshot.TextCapsulePosition,
            snapshot.TextCapsuleTransparentBackground);
    }

    public void Save(TextCapsuleSettingsState state)
    {
        var snapshot = _settingsService.Load();
        snapshot.ShowTextCapsule = state.ShowTextCapsule;
        snapshot.TextCapsuleContent = state.Content;
        snapshot.TextCapsulePosition = state.Position;
        snapshot.TextCapsuleTransparentBackground = state.TransparentBackground;
        _settingsService.SaveSnapshot(
            SettingsScope.App,
            snapshot,
            changedKeys:
            [
                nameof(AppSettingsSnapshot.ShowTextCapsule),
                nameof(AppSettingsSnapshot.TextCapsuleContent),
                nameof(AppSettingsSnapshot.TextCapsulePosition),
                nameof(AppSettingsSnapshot.TextCapsuleTransparentBackground)
            ]);
    }
}

internal sealed class WeatherProviderAdapter : IWeatherProvider, IWeatherInfoService, IDisposable
{
    private readonly IWeatherDataService _weatherDataService = new XiaomiWeatherService();

    public Task<WeatherQueryResult<IReadOnlyList<WeatherLocation>>> SearchLocationsAsync(
        string keyword,
        string? locale = null,
        CancellationToken cancellationToken = default)
    {
        return _weatherDataService.SearchLocationsAsync(keyword, locale, cancellationToken);
    }

    public Task<WeatherQueryResult<WeatherSnapshot>> GetWeatherAsync(
        WeatherQuery query,
        CancellationToken cancellationToken = default)
    {
        return _weatherDataService.GetWeatherAsync(query, cancellationToken);
    }

    public Task<WeatherQueryResult<WeatherLocation>> ResolveLocationAsync(
        double latitude,
        double longitude,
        string? locale = null,
        CancellationToken cancellationToken = default)
    {
        return _weatherDataService.ResolveLocationAsync(latitude, longitude, locale, cancellationToken);
    }

    public void Dispose()
    {
        if (_weatherDataService is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}

internal sealed class WeatherSettingsService : IWeatherSettingsService, IDisposable
{
    private readonly ISettingsService _settingsService;
    private readonly WeatherProviderAdapter _weatherProvider = new();

    public WeatherSettingsService(ISettingsService settingsService)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
    }

    public WeatherSettingsState Get()
    {
        var snapshot = _settingsService.Load();
        return new WeatherSettingsState(
            snapshot.WeatherLocationMode,
            snapshot.WeatherLocationKey,
            snapshot.WeatherLocationName,
            snapshot.WeatherLatitude,
            snapshot.WeatherLongitude,
            snapshot.WeatherAutoRefreshLocation,
            snapshot.WeatherExcludedAlerts,
            NormalizeIconPackId(snapshot.WeatherIconPackId),
            snapshot.WeatherNoTlsRequests,
            snapshot.WeatherLocationQuery);
    }

    public void Save(WeatherSettingsState state)
    {
        var snapshot = _settingsService.Load();
        snapshot.WeatherLocationMode = state.LocationMode;
        snapshot.WeatherLocationKey = state.LocationKey;
        snapshot.WeatherLocationName = state.LocationName;
        snapshot.WeatherLatitude = state.Latitude;
        snapshot.WeatherLongitude = state.Longitude;
        snapshot.WeatherAutoRefreshLocation = state.AutoRefreshLocation;
        snapshot.WeatherExcludedAlerts = state.ExcludedAlerts;
        snapshot.WeatherIconPackId = NormalizeIconPackId(state.IconPackId);
        snapshot.WeatherNoTlsRequests = state.NoTlsRequests;
        snapshot.WeatherLocationQuery = state.LocationQuery;
        _settingsService.SaveSnapshot(
            SettingsScope.App,
            snapshot,
            changedKeys:
            [
                nameof(AppSettingsSnapshot.WeatherLocationMode),
                nameof(AppSettingsSnapshot.WeatherLocationKey),
                nameof(AppSettingsSnapshot.WeatherLocationName),
                nameof(AppSettingsSnapshot.WeatherLatitude),
                nameof(AppSettingsSnapshot.WeatherLongitude),
                nameof(AppSettingsSnapshot.WeatherAutoRefreshLocation),
                nameof(AppSettingsSnapshot.WeatherExcludedAlerts),
                nameof(AppSettingsSnapshot.WeatherIconPackId),
                nameof(AppSettingsSnapshot.WeatherNoTlsRequests),
                nameof(AppSettingsSnapshot.WeatherLocationQuery)
            ]);
    }

    public Task<WeatherQueryResult<IReadOnlyList<WeatherLocation>>> SearchLocationsAsync(
        string keyword,
        string? locale = null,
        CancellationToken cancellationToken = default)
    {
        return _weatherProvider.SearchLocationsAsync(keyword, locale, cancellationToken);
    }

    public Task<WeatherQueryResult<WeatherLocation>> ResolveLocationAsync(
        double latitude,
        double longitude,
        string? locale = null,
        CancellationToken cancellationToken = default)
    {
        return _weatherProvider.ResolveLocationAsync(latitude, longitude, locale, cancellationToken);
    }

    public IWeatherInfoService GetWeatherInfoService()
    {
        return _weatherProvider;
    }

    public void Dispose()
    {
        _weatherProvider.Dispose();
    }

    private static string NormalizeIconPackId(string? iconPackId)
    {
        return string.IsNullOrWhiteSpace(iconPackId)
            ? "HyperOS3"
            : "HyperOS3";
    }
}

internal sealed class RegionSettingsService : IRegionSettingsService
{
    private readonly ISettingsService _settingsService;
    private readonly TimeZoneService _timeZoneService = new();

    public RegionSettingsService(ISettingsService settingsService)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        ApplyTimeZone(_settingsService.Load().TimeZoneId);
    }

    public RegionSettingsState Get()
    {
        var snapshot = _settingsService.Load();
        return new RegionSettingsState(snapshot.LanguageCode, snapshot.TimeZoneId);
    }

    public void Save(RegionSettingsState state)
    {
        var snapshot = _settingsService.Load();
        snapshot.LanguageCode = string.IsNullOrWhiteSpace(state.LanguageCode)
            ? "zh-CN"
            : state.LanguageCode.Trim();
        snapshot.TimeZoneId = string.IsNullOrWhiteSpace(state.TimeZoneId)
            ? null
            : state.TimeZoneId.Trim();
        _settingsService.SaveSnapshot(
            SettingsScope.App,
            snapshot,
            changedKeys:
            [
                nameof(AppSettingsSnapshot.LanguageCode),
                nameof(AppSettingsSnapshot.TimeZoneId)
            ]);
        ApplyTimeZone(snapshot.TimeZoneId);
    }

    public TimeZoneService GetTimeZoneService()
    {
        return _timeZoneService;
    }

    private void ApplyTimeZone(string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            _timeZoneService.CurrentTimeZone = TimeZoneInfo.Local;
            return;
        }

        if (!_timeZoneService.SetTimeZoneById(timeZoneId))
        {
            _timeZoneService.CurrentTimeZone = TimeZoneInfo.Local;
        }
    }
}

internal sealed class PrivacySettingsService : IPrivacySettingsService
{
    private readonly ISettingsService _settingsService;

    public PrivacySettingsService(ISettingsService settingsService)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
    }

    public PrivacySettingsState Get()
    {
        var snapshot = _settingsService.Load();
        return new PrivacySettingsState(
            snapshot.UploadAnonymousCrashData,
            snapshot.UploadAnonymousUsageData);
    }

    public void Save(PrivacySettingsState state)
    {
        var snapshot = _settingsService.Load();
        var changedKeys = new List<string>();

        if (snapshot.UploadAnonymousCrashData != state.UploadAnonymousCrashData)
        {
            snapshot.UploadAnonymousCrashData = state.UploadAnonymousCrashData;
            changedKeys.Add(nameof(AppSettingsSnapshot.UploadAnonymousCrashData));
        }

        if (snapshot.UploadAnonymousUsageData != state.UploadAnonymousUsageData)
        {
            snapshot.UploadAnonymousUsageData = state.UploadAnonymousUsageData;
            changedKeys.Add(nameof(AppSettingsSnapshot.UploadAnonymousUsageData));
        }

        if (changedKeys.Count == 0)
        {
            return;
        }

        AppLogger.Info(
            "PrivacySettings",
            $"Saving: UploadAnonymousCrashData={state.UploadAnonymousCrashData}, UploadAnonymousUsageData={state.UploadAnonymousUsageData}");
        _settingsService.SaveSnapshot(
            SettingsScope.App,
            snapshot,
            changedKeys: changedKeys);
    }
}

internal sealed class UpdateSettingsService : IUpdateSettingsService, IDisposable
{
    private readonly ISettingsService _settingsService;
    private readonly GitHubReleaseUpdateService _githubReleaseUpdateService = new("wwiinnddyy", "LanMountainDesktop");
    private readonly PlondsReleaseUpdateService _plondsReleaseUpdateService = new();

    public UpdateSettingsService(ISettingsService settingsService)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
    }

    public UpdateSettingsState Get()
    {
        var snapshot = _settingsService.Load();
        var normalizedChannel = UpdateSettingsValues.NormalizeChannel(
            snapshot.UpdateChannel,
            snapshot.IncludePrereleaseUpdates);
        return new UpdateSettingsState(
            string.Equals(normalizedChannel, UpdateSettingsValues.ChannelPreview, StringComparison.OrdinalIgnoreCase),
            normalizedChannel,
            UpdateSettingsValues.NormalizeMode(snapshot.UpdateMode),
            UpdateSettingsValues.NormalizeDownloadSource(snapshot.UpdateDownloadSource),
            UpdateSettingsValues.NormalizeDownloadThreads(snapshot.UpdateDownloadThreads),
            snapshot.PendingUpdateInstallerPath,
            snapshot.PendingUpdateVersion,
            snapshot.PendingUpdatePublishedAtUtcMs,
            snapshot.LastUpdateCheckUtcMs,
            snapshot.PendingUpdateSha256);
    }

    public void Save(UpdateSettingsState state)
    {
        var snapshot = _settingsService.Load();
        var normalizedChannel = UpdateSettingsValues.NormalizeChannel(
            state.UpdateChannel,
            state.IncludePrereleaseUpdates);
        snapshot.IncludePrereleaseUpdates = string.Equals(
            normalizedChannel,
            UpdateSettingsValues.ChannelPreview,
            StringComparison.OrdinalIgnoreCase);
        snapshot.UpdateChannel = normalizedChannel;
        snapshot.UpdateMode = UpdateSettingsValues.NormalizeMode(state.UpdateMode);
        snapshot.UpdateDownloadSource = UpdateSettingsValues.NormalizeDownloadSource(state.UpdateDownloadSource);
        snapshot.UpdateDownloadThreads = UpdateSettingsValues.NormalizeDownloadThreads(state.UpdateDownloadThreads);
        snapshot.PendingUpdateInstallerPath = string.IsNullOrWhiteSpace(state.PendingUpdateInstallerPath)
            ? null
            : state.PendingUpdateInstallerPath.Trim();
        snapshot.PendingUpdateVersion = string.IsNullOrWhiteSpace(state.PendingUpdateVersion)
            ? null
            : state.PendingUpdateVersion.Trim();
        snapshot.PendingUpdatePublishedAtUtcMs = state.PendingUpdatePublishedAtUtcMs is > 0
            ? state.PendingUpdatePublishedAtUtcMs
            : null;
        snapshot.LastUpdateCheckUtcMs = state.LastUpdateCheckUtcMs is > 0
            ? state.LastUpdateCheckUtcMs
            : null;
        snapshot.PendingUpdateSha256 = string.IsNullOrWhiteSpace(state.PendingUpdateSha256)
            ? null
            : state.PendingUpdateSha256.Trim().ToLowerInvariant();
        _settingsService.SaveSnapshot(
            SettingsScope.App,
            snapshot,
            changedKeys:
            [
                nameof(AppSettingsSnapshot.IncludePrereleaseUpdates),
                nameof(AppSettingsSnapshot.IncludePrereleaseUpdates),
                nameof(AppSettingsSnapshot.UpdateChannel),
                nameof(AppSettingsSnapshot.UpdateMode),
                nameof(AppSettingsSnapshot.UpdateDownloadSource),
                nameof(AppSettingsSnapshot.UpdateDownloadThreads),
                nameof(AppSettingsSnapshot.PendingUpdateInstallerPath),
                nameof(AppSettingsSnapshot.PendingUpdateVersion),
                nameof(AppSettingsSnapshot.PendingUpdatePublishedAtUtcMs),
                nameof(AppSettingsSnapshot.LastUpdateCheckUtcMs),
                nameof(AppSettingsSnapshot.PendingUpdateSha256)
            ]);
    }

    public Task<UpdateCheckResult> CheckForUpdatesAsync(
        Version currentVersion,
        bool includePrerelease,
        CancellationToken cancellationToken = default)
    {
        return CheckForUpdatesCoreAsync(currentVersion, includePrerelease, isForce: false, cancellationToken);
    }

    public Task<UpdateCheckResult> ForceCheckForUpdatesAsync(
        Version currentVersion,
        bool includePrerelease,
        CancellationToken cancellationToken = default)
    {
        return CheckForUpdatesCoreAsync(currentVersion, includePrerelease, isForce: true, cancellationToken);
    }

    public async Task<PlondsUpdatePayload?> GetPlondsUpdatePayloadAsync(
        Version currentVersion,
        bool includePrerelease,
        bool isForce = false,
        CancellationToken cancellationToken = default)
    {
        var result = isForce
            ? await _plondsReleaseUpdateService.ForceCheckForUpdatesAsync(currentVersion, includePrerelease, cancellationToken)
            : await _plondsReleaseUpdateService.CheckForUpdatesAsync(currentVersion, includePrerelease, cancellationToken);
        return result.Success ? result.PlondsPayload : null;
    }

    public Task<UpdateDownloadResult> DownloadAssetAsync(
        GitHubReleaseAsset asset,
        string destinationFilePath,
        string downloadSource,
        int maxParallelSegments,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return _githubReleaseUpdateService.DownloadAssetAsync(
            asset,
            destinationFilePath,
            downloadSource,
            maxParallelSegments,
            progress,
            cancellationToken);
    }

    public Task<UpdateDownloadResult> RedownloadAssetAsync(
        GitHubReleaseAsset asset,
        string destinationFilePath,
        string downloadSource,
        int maxParallelSegments,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return _githubReleaseUpdateService.RedownloadAssetAsync(
            asset,
            destinationFilePath,
            downloadSource,
            maxParallelSegments,
            progress,
            cancellationToken);
    }

    public void Dispose()
    {
        _githubReleaseUpdateService.Dispose();
        _plondsReleaseUpdateService.Dispose();
    }

    private async Task<UpdateCheckResult> CheckForUpdatesCoreAsync(
        Version currentVersion,
        bool includePrerelease,
        bool isForce,
        CancellationToken cancellationToken)
    {
        var source = UpdateSettingsValues.NormalizeDownloadSource(_settingsService.Load().UpdateDownloadSource);
        if (string.Equals(source, UpdateSettingsValues.DownloadSourcePlonds, StringComparison.OrdinalIgnoreCase))
        {
            var plondsResult = isForce
                ? await _plondsReleaseUpdateService.ForceCheckForUpdatesAsync(currentVersion, includePrerelease, cancellationToken)
                : await _plondsReleaseUpdateService.CheckForUpdatesAsync(currentVersion, includePrerelease, cancellationToken);

            if (plondsResult.Success)
            {
                return plondsResult;
            }

            AppLogger.Warn(
                "UpdateSettings",
                $"PLONDS update check failed and will fallback to GitHub. Error: {plondsResult.ErrorMessage}");
        }

        return isForce
            ? await _githubReleaseUpdateService.ForceCheckForUpdatesAsync(currentVersion, includePrerelease, cancellationToken)
            : await _githubReleaseUpdateService.CheckForUpdatesAsync(currentVersion, includePrerelease, cancellationToken);
    }
}

internal sealed class LauncherCatalogService : ILauncherCatalogService
{
    private readonly WindowsStartMenuService _startMenuService = new();

    public StartMenuFolderNode LoadCatalog()
    {
        return _startMenuService.Load();
    }
}

internal sealed class LauncherPolicyService : ILauncherPolicyService
{
    private readonly LauncherSettingsService _launcherSettingsService = new();

    public LauncherSettingsSnapshot Get()
    {
        return _launcherSettingsService.Load();
    }

    public void Save(LauncherSettingsSnapshot snapshot)
    {
        _launcherSettingsService.Save(snapshot ?? new LauncherSettingsSnapshot());
    }
}

internal sealed class PluginManagementSettingsService : IPluginManagementSettingsService
{
    private readonly ISettingsService _settingsService;
    private PluginRuntimeService? _pluginRuntimeService;

    public PluginManagementSettingsService(ISettingsService settingsService, PluginRuntimeService? pluginRuntimeService)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _pluginRuntimeService = pluginRuntimeService;
    }

    public void SetPluginRuntime(PluginRuntimeService? pluginRuntimeService)
    {
        _pluginRuntimeService = pluginRuntimeService;
    }

    public PluginManagementSettingsState Get()
    {
        var snapshot = _settingsService.Load();
        return new PluginManagementSettingsState(snapshot.DisabledPluginIds?.ToArray() ?? []);
    }

    public void Save(PluginManagementSettingsState state)
    {
        var snapshot = _settingsService.Load();
        snapshot.DisabledPluginIds = state.DisabledPluginIds?.ToList() ?? [];
        _settingsService.SaveSnapshot(
            SettingsScope.App,
            snapshot,
            changedKeys: [nameof(AppSettingsSnapshot.DisabledPluginIds)]);
    }

    public IReadOnlyList<InstalledPluginInfo> GetInstalledPlugins()
    {
        return _pluginRuntimeService?.GetInstalledPluginsSnapshot() ?? [];
    }

    public bool SetPluginEnabled(string pluginId, bool isEnabled)
    {
        return _pluginRuntimeService?.SetPluginEnabled(pluginId, isEnabled) ?? false;
    }

    public bool DeleteInstalledPlugin(string pluginId)
    {
        return _pluginRuntimeService?.DeleteInstalledPlugin(pluginId) ?? false;
    }
}

internal sealed class PluginCatalogSettingsService : IPluginCatalogSettingsService, IDisposable
{
    private PluginRuntimeService? _pluginRuntimeService;
    private AirAppMarketIndexService _indexService;
    private AirAppMarketInstallService? _installService;
    private readonly Dictionary<string, AirAppMarketPluginEntry> _cachedPlugins = new(StringComparer.OrdinalIgnoreCase);

    public PluginCatalogSettingsService(PluginRuntimeService? pluginRuntimeService)
    {
        _pluginRuntimeService = pluginRuntimeService;

        var dataRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LanMountainDesktop",
            "PluginMarket");
        var cacheService = new AirAppMarketCacheService(dataRoot);
        _indexService = new AirAppMarketIndexService(cacheService);
        if (_pluginRuntimeService is not null)
        {
            _installService = new AirAppMarketInstallService(_pluginRuntimeService, dataRoot);
        }
    }

    public void SetPluginRuntime(PluginRuntimeService? pluginRuntimeService)
    {
        _pluginRuntimeService = pluginRuntimeService;
        _installService?.Dispose();
        _installService = null;

        if (_pluginRuntimeService is null)
        {
            return;
        }

        var dataRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LanMountainDesktop",
            "PluginMarket");
        _installService = new AirAppMarketInstallService(_pluginRuntimeService, dataRoot);
    }

    public Task<PluginCatalogIndexResult> LoadCatalogAsync(CancellationToken cancellationToken = default)
    {
        return LoadCatalogCoreAsync(cancellationToken);
    }

    public Task<PluginCatalogInstallResult> InstallAsync(
        string pluginId,
        CancellationToken cancellationToken = default)
    {
        return InstallCatalogCoreAsync(pluginId, cancellationToken);
    }

    private async Task<PluginCatalogIndexResult> LoadCatalogCoreAsync(CancellationToken cancellationToken = default)
    {
        var result = await _indexService.LoadAsync(cancellationToken).ConfigureAwait(false);
        var sources = BuildCatalogSources(result.Source?.ToString(), result.SourceLocation, result.WarningMessage);
        if (!result.Success || result.Document is null)
        {
            _cachedPlugins.Clear();
            return new PluginCatalogIndexResult(
                false,
                [],
                sources,
                result.Source?.ToString(),
                result.SourceLocation,
                result.WarningMessage,
                result.ErrorMessage);
        }

        _cachedPlugins.Clear();
        var plugins = result.Document.Plugins
            .Select(entry =>
            {
                _cachedPlugins[entry.Id] = entry;
                return MapCatalogItem(entry);
            })
            .ToArray();

        return new PluginCatalogIndexResult(
            true,
            plugins,
            sources,
            result.Source?.ToString(),
            result.SourceLocation,
            result.WarningMessage,
            null);
    }

    private async Task<PluginCatalogInstallResult> InstallCatalogCoreAsync(
        string pluginId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(pluginId))
        {
            return new PluginCatalogInstallResult(
                false,
                null,
                null,
                null,
                [new PluginInstallDiagnostic("invalid_request", "Plugin id is required.")],
                "Plugin id is required.");
        }

        if (_installService is null || _pluginRuntimeService is null)
        {
            return new PluginCatalogInstallResult(
                false,
                pluginId,
                null,
                null,
                [new PluginInstallDiagnostic("runtime_unavailable", "Plugin runtime is unavailable.")],
                "Plugin runtime is unavailable.");
        }

        if (!_cachedPlugins.TryGetValue(pluginId, out var entry))
        {
            var load = await LoadCatalogCoreAsync(cancellationToken).ConfigureAwait(false);
            if (!load.Success)
            {
                return new PluginCatalogInstallResult(
                    false,
                    pluginId,
                    null,
                    null,
                    [new PluginInstallDiagnostic("catalog_load_failed", load.ErrorMessage ?? "Failed to load the plugin catalog.")],
                    load.ErrorMessage);
            }

            if (!_cachedPlugins.TryGetValue(pluginId, out entry))
            {
                return new PluginCatalogInstallResult(
                    false,
                    pluginId,
                    null,
                    null,
                    [new PluginInstallDiagnostic("not_found", "Plugin was not found in the official catalog.")],
                    "Plugin was not found in the official catalog.");
            }
        }

        var result = await _installService.InstallAsync(entry, cancellationToken).ConfigureAwait(false);
        if (!result.Success)
        {
            return new PluginCatalogInstallResult(
                false,
                entry.Id,
                entry.Name,
                null,
                [new PluginInstallDiagnostic("install_failed", result.ErrorMessage ?? "Plugin install failed.")],
                result.ErrorMessage);
        }

        return new PluginCatalogInstallResult(
            true,
            result.Manifest?.Id ?? entry.Id,
            result.Manifest?.Name ?? entry.Name,
            result.Manifest,
            [],
            null);
    }

    private static PluginCatalogItemInfo MapCatalogItem(AirAppMarketPluginEntry entry)
    {
        var manifest = new PluginCatalogManifestInfo(
            entry.Id,
            entry.Name,
            entry.Description,
            entry.Author,
            entry.Version,
            entry.ApiVersion,
            string.Empty,
            entry.SharedContracts
                .Select(contract => new PluginCatalogSharedContractInfo(
                    contract.Id,
                    contract.Version,
                    contract.AssemblyName))
                .ToArray());

        var compatibility = new PluginCatalogCompatibilityInfo(
            entry.MinHostVersion,
            entry.ApiVersion);

        var repository = new PluginCatalogRepositoryInfo(
            entry.IconUrl,
            entry.ProjectUrl,
            entry.ReadmeUrl,
            entry.HomepageUrl,
            entry.RepositoryUrl,
            entry.Tags.ToArray(),
            entry.ReleaseNotes);

        var publication = new PluginCatalogPublicationInfo(
            entry.ReleaseTag,
            entry.ReleaseAssetName,
            entry.PublishedAt,
            entry.UpdatedAt,
            entry.PackageSizeBytes,
            entry.Sha256,
            null);

        var sources = BuildPackageSources(entry);

        return new PluginCatalogItemInfo(
            manifest,
            compatibility,
            repository,
            publication,
            sources,
            []);
    }

    private static IReadOnlyList<PluginPackageSourceInfo> BuildPackageSources(AirAppMarketPluginEntry entry)
    {
        var sources = entry.GetPackageSourcesInInstallOrder();
        if (sources.Count == 0)
        {
            return [];
        }

        return sources
            .Select(source => new PluginPackageSourceInfo(
                source.SourceKind switch
                {
                    LanMountainDesktop.Services.PluginMarket.PluginPackageSourceKind.ReleaseAsset => PluginPackageSourceKind.ReleaseAsset,
                    LanMountainDesktop.Services.PluginMarket.PluginPackageSourceKind.RawFallback => PluginPackageSourceKind.RawFallback,
                    LanMountainDesktop.Services.PluginMarket.PluginPackageSourceKind.WorkspaceLocal => PluginPackageSourceKind.WorkspaceLocal,
                    _ => PluginPackageSourceKind.RawFallback
                },
                source.Url,
                entry.Sha256,
                entry.PackageSizeBytes))
            .ToArray();
    }

    private static IReadOnlyList<PluginCatalogSourceInfo> BuildCatalogSources(
        string? sourceId,
        string? sourceLocation,
        string? warningMessage)
    {
        if (string.IsNullOrWhiteSpace(sourceId) && string.IsNullOrWhiteSpace(sourceLocation))
        {
            return [];
        }

        var normalizedSourceId = string.IsNullOrWhiteSpace(sourceId)
            ? "plugin-catalog"
            : sourceId.Trim();

        return
        [
            new PluginCatalogSourceInfo(
                normalizedSourceId,
                normalizedSourceId,
                string.IsNullOrWhiteSpace(warningMessage) ? null : warningMessage.Trim(),
                string.IsNullOrWhiteSpace(sourceLocation) ? null : sourceLocation.Trim(),
                null,
                true,
                0)
        ];
    }

    public void Dispose()
    {
        _indexService.Dispose();
        _installService?.Dispose();
    }
}

internal sealed class ApplicationInfoService : IApplicationInfoService
{
    private const string DefaultCodename = "Administrate";

    public string GetAppVersionText()
    {
        // 浼樺厛浠庣幆澧冨彉閲忚鍙栵紙Launcher 浼犻€掞級
        var envVersion = Environment.GetEnvironmentVariable(LanMountainDesktop.Shared.Contracts.Launcher.LauncherIpcConstants.VersionEnvVar);
        if (!string.IsNullOrWhiteSpace(envVersion))
        {
            return envVersion;
        }

        // Fallback: read from application assembly.
        var assembly = typeof(App).Assembly;
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            var normalizedInformationalVersion = informationalVersion.Split('+', 2)[0].Trim();
            if (!string.IsNullOrWhiteSpace(normalizedInformationalVersion))
            {
                return normalizedInformationalVersion;
            }
        }

        var version = assembly.GetName().Version;
        if (version is null)
        {
            return "0.0.0";
        }

        if (version.Revision >= 0)
        {
            return version.ToString(4);
        }

        if (version.Build >= 0)
        {
            return version.ToString(3);
        }

        if (version.Minor >= 0)
        {
            return version.ToString(2);
        }

        return version.ToString();
    }

    public string GetAppCodenameText()
    {
        // 浼樺厛浠庣幆澧冨彉閲忚鍙栵紙Launcher 浼犻€掞級
        var envCodename = Environment.GetEnvironmentVariable(LanMountainDesktop.Shared.Contracts.Launcher.LauncherIpcConstants.CodenameEnvVar);
        if (!string.IsNullOrWhiteSpace(envCodename))
        {
            return envCodename;
        }

        // Fallback: use default codename.
        return DefaultCodename;
    }

    public AppRenderBackendInfo GetRenderBackendInfo()
    {
        return AppRenderBackendDiagnostics.Detect();
    }
}

internal sealed class SettingsFacadeService : ISettingsFacadeService, IDisposable
{
    private readonly UpdateSettingsService _updateSettingsService;
    private readonly PluginCatalogSettingsService _pluginCatalogSettingsService;
    private readonly PluginManagementSettingsService _pluginManagementSettingsService;
    private readonly WeatherSettingsService _weatherSettingsService;

    public SettingsFacadeService(PluginRuntimeService? pluginRuntimeService = null)
    {
        Settings = new SettingsService();
        Catalog = new SettingsCatalogService();
        Grid = new GridSettingsService(Settings);
        Wallpaper = new WallpaperSettingsService(Settings);
        WallpaperMedia = new WallpaperMediaService();
        Theme = new ThemeAppearanceService(Settings);
        StatusBar = new StatusBarSettingsService(Settings);
        TextCapsule = new TextCapsuleSettingsService(Settings);
        _weatherSettingsService = new WeatherSettingsService(Settings);
        Weather = _weatherSettingsService;
        Region = new RegionSettingsService(Settings);
        Privacy = new PrivacySettingsService(Settings);
        _updateSettingsService = new UpdateSettingsService(Settings);
        Update = _updateSettingsService;
        LauncherCatalog = new LauncherCatalogService();
        LauncherPolicy = new LauncherPolicyService();
        _pluginManagementSettingsService = new PluginManagementSettingsService(Settings, pluginRuntimeService);
        PluginManagement = _pluginManagementSettingsService;
        _pluginCatalogSettingsService = new PluginCatalogSettingsService(pluginRuntimeService);
        PluginCatalog = _pluginCatalogSettingsService;
        ApplicationInfo = new ApplicationInfoService();
    }

    public ISettingsService Settings { get; }

    public ISettingsCatalog Catalog { get; }

    public IGridSettingsService Grid { get; }

    public IWallpaperSettingsService Wallpaper { get; }

    public IWallpaperMediaService WallpaperMedia { get; }

    public IThemeAppearanceService Theme { get; }

    public IStatusBarSettingsService StatusBar { get; }

    public ITextCapsuleSettingsService TextCapsule { get; }

    public IWeatherSettingsService Weather { get; }

    public IRegionSettingsService Region { get; }

    public IPrivacySettingsService Privacy { get; }

    public IUpdateSettingsService Update { get; }

    public ILauncherCatalogService LauncherCatalog { get; }

    public ILauncherPolicyService LauncherPolicy { get; }

    public IPluginManagementSettingsService PluginManagement { get; }

    public IPluginCatalogSettingsService PluginCatalog { get; }

    public IApplicationInfoService ApplicationInfo { get; }

    public void BindPluginRuntime(PluginRuntimeService? pluginRuntimeService)
    {
        _pluginManagementSettingsService.SetPluginRuntime(pluginRuntimeService);
        _pluginCatalogSettingsService.SetPluginRuntime(pluginRuntimeService);
    }

    public void Dispose()
    {
        _weatherSettingsService.Dispose();
        _updateSettingsService.Dispose();
        _pluginCatalogSettingsService.Dispose();
    }
}
