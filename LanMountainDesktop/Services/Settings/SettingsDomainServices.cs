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
using LanMountainDesktop.Services.Plonds;
using LanMountainDesktop.Services.Update;
using LanMountainDesktop.Settings.Core;
using LanMountainDesktop.Services.PluginMarket;
using LanMountainDesktop.Shared.Contracts.Update;

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
        _wallpapersDirectory = AppDataPathProvider.GetWallpapersDirectory();
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
            snapshot.SelectedWallpaperSeed,
            NormalizeThemeMode(snapshot.ThemeMode),
            ThemeAppearanceValues.NormalizeWallpaperColorSource(snapshot.ThemeWallpaperColorSource),
            snapshot.UseNativeWallpaperChangeEvents);
    }

    private static string NormalizeThemeMode(string? value)
    {
        if (string.Equals(value, ThemeAppearanceValues.ThemeModeDark, StringComparison.OrdinalIgnoreCase))
        {
            return ThemeAppearanceValues.ThemeModeDark;
        }
        if (string.Equals(value, ThemeAppearanceValues.ThemeModeFollowSystem, StringComparison.OrdinalIgnoreCase))
        {
            return ThemeAppearanceValues.ThemeModeFollowSystem;
        }
        return ThemeAppearanceValues.ThemeModeLight;
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
        var normalizedWallpaperColorSource = ThemeAppearanceValues.NormalizeWallpaperColorSource(state.ThemeWallpaperColorSource);

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
            if (OperatingSystem.IsWindows())
            {
                LanMountainDesktop.Platform.Windows.ChromePatchState.UseSystemChrome = state.UseSystemChrome;
            }
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

        if (!string.Equals(snapshot.ThemeWallpaperColorSource, normalizedWallpaperColorSource, StringComparison.OrdinalIgnoreCase))
        {
            snapshot.ThemeWallpaperColorSource = normalizedWallpaperColorSource;
            changedKeys.Add(nameof(AppSettingsSnapshot.ThemeWallpaperColorSource));
        }

        if (snapshot.UseNativeWallpaperChangeEvents != state.UseNativeWallpaperChangeEvents)
        {
            snapshot.UseNativeWallpaperChangeEvents = state.UseNativeWallpaperChangeEvents;
            changedKeys.Add(nameof(AppSettingsSnapshot.UseNativeWallpaperChangeEvents));
        }

        var normalizedThemeMode = NormalizeThemeMode(state.ThemeMode);
        if (!string.Equals(snapshot.ThemeMode, normalizedThemeMode, StringComparison.OrdinalIgnoreCase))
        {
            snapshot.ThemeMode = normalizedThemeMode;
            changedKeys.Add(nameof(AppSettingsSnapshot.ThemeMode));
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
        return WeatherVisualStyleCatalog.Normalize(iconPackId);
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
    private readonly IPlondsService _plondsService;
    private readonly PlondsPreparedPackageInstaller _plondsInstaller = new();
    private readonly UpdateInstallGateway _plondsUpdateInstallGateway = new();
    private readonly Lazy<UpdateOrchestrator> _orchestrator;
    private PlondsLatestResult? _pendingPlondsLatest;
    private PlondsManifestCandidate? _pendingPlondsCleanInstallCandidate;
    private UpdateManifest? _pendingPlondsInstallerManifest;
    private PlondsPreparedPackage? _pendingPlondsPackage;
    private UpdatePhase _plondsPhase = UpdatePhase.Idle;
    private bool _orchestratorEventsSubscribed;

    public UpdateSettingsService(
        ISettingsService settingsService,
        Func<UpdateOrchestrator>? orchestratorFactory = null,
        IPlondsService? plondsService = null)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _plondsService = plondsService ?? PlondsClientServiceFactory.CreateDefault();
        _orchestrator = new Lazy<UpdateOrchestrator>(
            orchestratorFactory ?? HostUpdateOrchestratorProvider.GetOrCreate,
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public UpdatePhase CurrentPhase => IsPlondsSelected()
        ? _plondsPhase
        : (_orchestrator.IsValueCreated ? _orchestrator.Value.CurrentPhase : UpdatePhase.Idle);

    public event Action<UpdatePhase>? PhaseChanged
    {
        add
        {
            _phaseChanged += value;
        }
        remove
        {
            _phaseChanged -= value;
        }
    }

    public event Action<UpdateProgressReport>? ProgressChanged
    {
        add
        {
            _progressChanged += value;
        }
        remove
        {
            _progressChanged -= value;
        }
    }

    private event Action<UpdatePhase>? _phaseChanged;
    private event Action<UpdateProgressReport>? _progressChanged;

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
            snapshot.ForceUpdateReinstall,
            snapshot.UseGhProxyMirror,
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
        snapshot.ForceUpdateReinstall = state.ForceUpdateReinstall;
        snapshot.UseGhProxyMirror = state.UseGhProxyMirror;
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
                nameof(AppSettingsSnapshot.UpdateChannel),
                nameof(AppSettingsSnapshot.UpdateMode),
                nameof(AppSettingsSnapshot.UpdateDownloadSource),
                nameof(AppSettingsSnapshot.UpdateDownloadThreads),
                nameof(AppSettingsSnapshot.ForceUpdateReinstall),
                nameof(AppSettingsSnapshot.UseGhProxyMirror),
                nameof(AppSettingsSnapshot.PendingUpdateInstallerPath),
                nameof(AppSettingsSnapshot.PendingUpdateVersion),
                nameof(AppSettingsSnapshot.PendingUpdatePublishedAtUtcMs),
                nameof(AppSettingsSnapshot.LastUpdateCheckUtcMs),
                nameof(AppSettingsSnapshot.PendingUpdateSha256)
            ]);
    }

    public Task<UpdateCheckReport> CheckAsync(CancellationToken cancellationToken = default)
    {
        return IsPlondsSelected()
            ? CheckPlondsAsync(cancellationToken)
            : GetOrchestrator().CheckAsync(cancellationToken);
    }

    public Task<LanMountainDesktop.Services.Update.DownloadResult> DownloadAsync(CancellationToken cancellationToken = default)
    {
        return IsPlondsSelected()
            ? DownloadPlondsAsync(cancellationToken)
            : GetOrchestrator().DownloadAsync(cancellationToken);
    }

    public Task<InstallResult> InstallAsync(CancellationToken cancellationToken = default)
    {
        return IsPlondsSelected()
            ? InstallPlondsAsync(cancellationToken)
            : GetOrchestrator().InstallAsync(cancellationToken);
    }

    public Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        return GetOrchestrator().RollbackAsync(cancellationToken);
    }

    public Task PauseAsync()
    {
        return IsPlondsSelected()
            ? PausePlondsAsync()
            : GetOrchestrator().PauseAsync();
    }

    public Task<LanMountainDesktop.Services.Update.DownloadResult> ResumeAsync(CancellationToken cancellationToken = default)
    {
        return IsPlondsSelected()
            ? ResumePlondsAsync(cancellationToken)
            : GetOrchestrator().ResumeAsync(cancellationToken);
    }

    public Task CancelAsync()
    {
        if (IsPlondsSelected())
        {
            _pendingPlondsLatest = null;
            _pendingPlondsCleanInstallCandidate = null;
            _pendingPlondsInstallerManifest = null;
            _pendingPlondsPackage = null;
            TransitionPlonds(UpdatePhase.Idle);
            return Task.CompletedTask;
        }

        return GetOrchestrator().CancelAsync();
    }

    public Task AutoCheckIfEnabledAsync(CancellationToken cancellationToken = default)
    {
        if (IsPlondsSelected())
        {
            return AutoCheckPlondsIfEnabledAsync(cancellationToken);
        }

        return GetOrchestrator().AutoCheckIfEnabledAsync(cancellationToken);
    }

    public bool TryApplyOnExit()
    {
        if (IsPlondsSelected())
        {
            return TryApplyPlondsOnExit();
        }

        return GetOrchestrator().TryApplyOnExit();
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
        if (_orchestrator.IsValueCreated && _orchestratorEventsSubscribed)
        {
            _orchestrator.Value.PhaseChanged -= OnOrchestratorPhaseChanged;
            _orchestrator.Value.ProgressChanged -= OnOrchestratorProgressChanged;
        }
    }

    private async Task<UpdateCheckResult> CheckForUpdatesCoreAsync(
        Version currentVersion,
        bool includePrerelease,
        bool isForce,
        CancellationToken cancellationToken)
    {
        if (IsGitHubSelected())
        {
            return isForce
                ? await _githubReleaseUpdateService.ForceCheckForUpdatesAsync(currentVersion, includePrerelease, cancellationToken)
                : await _githubReleaseUpdateService.CheckForUpdatesAsync(currentVersion, includePrerelease, cancellationToken);
        }

        var result = await _plondsService.FindLatestAsync(currentVersion, cancellationToken).ConfigureAwait(false);
        return new UpdateCheckResult(
            Success: result.Success,
            IsUpdateAvailable: isForce || result.IsUpdateAvailable,
            CurrentVersionText: currentVersion.ToString(),
            LatestVersionText: result.LatestVersion?.ToString() ?? "-",
            Release: null,
            PreferredAsset: null,
            ErrorMessage: result.ErrorMessage,
            ForceMode: isForce);
    }

    private async Task<UpdateCheckReport> CheckPlondsAsync(CancellationToken cancellationToken)
    {
        if (!_plondsPhase.CanCheck())
        {
            return new UpdateCheckReport(false, null, null, null, null, null, null, null, null, $"Cannot check in phase {_plondsPhase}.");
        }

        TransitionPlonds(UpdatePhase.Checking);
        var currentVersionText = LanMountainDesktop.Shared.Contracts.Launcher.AppVersionProvider.ResolveForCurrentProcess().Version;
        if (!TryParseVersion(currentVersionText, out var currentVersion))
        {
            TransitionPlonds(UpdatePhase.Failed);
            return new UpdateCheckReport(false, null, currentVersionText, null, null, null, null, null, null, $"Invalid current version text: {currentVersionText}");
        }

        var latest = await _plondsService.FindLatestAsync(currentVersion, cancellationToken).ConfigureAwait(false);
        if (!latest.Success)
        {
            _pendingPlondsLatest = null;
            _pendingPlondsCleanInstallCandidate = null;
            _pendingPlondsInstallerManifest = null;
            _pendingPlondsPackage = null;
            TransitionPlonds(UpdatePhase.Idle);
            SaveLastChecked();
            return new UpdateCheckReport(false, null, currentVersionText, null, null, null, null, null, null, latest.ErrorMessage);
        }

        _pendingPlondsLatest = latest.IsUpdateAvailable ? latest : null;
        _pendingPlondsCleanInstallCandidate = _pendingPlondsLatest?.Candidates
            .FirstOrDefault(candidate => candidate.Manifest.RequiresCleanInstall);
        _pendingPlondsInstallerManifest = null;
        _pendingPlondsPackage = null;
        TransitionPlonds(UpdatePhase.Checked);
        SaveLastChecked();

        var payloadKind = latest.IsUpdateAvailable
            ? _pendingPlondsCleanInstallCandidate is not null
                ? UpdatePayloadKind.FullInstaller
                : UpdatePayloadKind.DeltaPlonds
            : (UpdatePayloadKind?)null;

        return new UpdateCheckReport(
            latest.IsUpdateAvailable,
            latest.LatestVersion?.ToString(),
            currentVersionText,
            payloadKind,
            latest.Candidates.FirstOrDefault()?.Source.Id,
            Get().UpdateChannel,
            DateTimeOffset.UtcNow,
            null,
            null,
            null);
    }

    private async Task<LanMountainDesktop.Services.Update.DownloadResult> DownloadPlondsAsync(CancellationToken cancellationToken)
    {
        if (_plondsPhase is not (UpdatePhase.Checked or UpdatePhase.PausedDownloading))
        {
            return new LanMountainDesktop.Services.Update.DownloadResult(false, null, $"Cannot download in phase {_plondsPhase}.", false);
        }

        if (_pendingPlondsLatest is null || !_pendingPlondsLatest.IsUpdateAvailable)
        {
            return new LanMountainDesktop.Services.Update.DownloadResult(false, null, "No PLONDS update is pending.", false);
        }

        var currentVersion = _pendingPlondsLatest.CurrentVersion;
        if (_pendingPlondsCleanInstallCandidate is not null)
        {
            return await DownloadPlondsCleanInstallAsync(_pendingPlondsCleanInstallCandidate, cancellationToken).ConfigureAwait(false);
        }

        TransitionPlonds(UpdatePhase.Downloading);
        var result = await _plondsService.FindAndPrepareLatestAsync(currentVersion, cancellationToken).ConfigureAwait(false);
        if (!result.Success || result.Package is null)
        {
            TransitionPlonds(UpdatePhase.Failed);
            return new LanMountainDesktop.Services.Update.DownloadResult(false, null, result.ErrorMessage ?? "PLONDS package preparation failed.", false);
        }

        _pendingPlondsPackage = result.Package;
        TransitionPlonds(UpdatePhase.Downloaded);
        SavePendingPlondsPackage(result.Package);
        return new LanMountainDesktop.Services.Update.DownloadResult(true, result.Package.ManifestPath, null, true);
    }

    private async Task<LanMountainDesktop.Services.Update.DownloadResult> DownloadPlondsCleanInstallAsync(
        PlondsManifestCandidate candidate,
        CancellationToken cancellationToken)
    {
        TransitionPlonds(UpdatePhase.Downloading);

        var manifest = await ResolveGitHubInstallerManifestForPlondsAsync(candidate.Manifest, cancellationToken).ConfigureAwait(false);
        if (manifest is null)
        {
            TransitionPlonds(UpdatePhase.Failed);
            return new LanMountainDesktop.Services.Update.DownloadResult(
                false,
                null,
                $"PLONDS {candidate.Manifest.CurrentVersion} requires clean install, but no matching GitHub installer release was found.",
                false);
        }

        var mirror = manifest.InstallerMirrors?
            .FirstOrDefault(item => !string.IsNullOrWhiteSpace(item.Url));
        if (mirror is null || string.IsNullOrWhiteSpace(mirror.Url))
        {
            TransitionPlonds(UpdatePhase.Failed);
            return new LanMountainDesktop.Services.Update.DownloadResult(
                false,
                null,
                $"PLONDS {candidate.Manifest.CurrentVersion} requires clean install, but GitHub release has no usable installer asset.",
                false);
        }

        var fileName = string.IsNullOrWhiteSpace(mirror.Name)
            ? $"{manifest.DistributionId}-{manifest.ToVersion}-installer.exe"
            : mirror.Name!;
        var asset = new GitHubReleaseAsset(fileName, mirror.Url!, mirror.Size, mirror.Sha256);
        var destinationPath = CreateInstallerDestinationPath(manifest, fileName);
        var maxThreads = UpdateSettingsValues.NormalizeDownloadThreads(Get().UpdateDownloadThreads);
        var progress = new Progress<double>(fraction =>
        {
            var downloadReport = new DownloadProgressReport(
                fileName,
                0,
                Math.Max(0, mirror.Size),
                0,
                fraction >= 1 ? 1 : 0,
                1,
                Math.Clamp(fraction, 0, 1));

            _progressChanged?.Invoke(new UpdateProgressReport(
                UpdatePhase.Downloading,
                $"Downloading {fileName}",
                Math.Clamp(fraction, 0, 1),
                downloadReport,
                null));
        });

        var result = await _githubReleaseUpdateService.DownloadAssetAsync(
                asset,
                destinationPath,
                UpdateSettingsValues.DownloadSourceGitHub,
                maxThreads,
                progress,
                cancellationToken)
            .ConfigureAwait(false);

        if (!result.Success || string.IsNullOrWhiteSpace(result.FilePath))
        {
            TransitionPlonds(UpdatePhase.Failed);
            return new LanMountainDesktop.Services.Update.DownloadResult(
                false,
                result.FilePath,
                result.ErrorMessage ?? "Failed to download GitHub installer for PLONDS clean install.",
                result.HashVerified);
        }

        var launcherRoot = UpdatePaths.ResolveLauncherRoot(AppContext.BaseDirectory);
        DeploymentLockService.WriteLock(launcherRoot, new DeploymentLock(
            SchemaVersion: 1,
            Kind: "full",
            TargetVersion: manifest.ToVersion,
            PayloadPath: result.FilePath,
            PayloadSha256: result.ExpectedHash,
            CreatedAtUtc: DateTimeOffset.UtcNow));

        _pendingPlondsInstallerManifest = manifest;
        _pendingPlondsPackage = null;
        TransitionPlonds(UpdatePhase.Downloaded);
        SavePendingPlondsInstaller(manifest, result.FilePath, result.ExpectedHash);
        return new LanMountainDesktop.Services.Update.DownloadResult(true, result.FilePath, null, result.HashVerified);
    }

    private Task PausePlondsAsync()
    {
        if (_plondsPhase.CanPause())
        {
            TransitionPlonds(UpdatePhase.PausedDownloading);
        }

        return Task.CompletedTask;
    }

    private async Task<LanMountainDesktop.Services.Update.DownloadResult> ResumePlondsAsync(CancellationToken cancellationToken)
    {
        return _plondsPhase is UpdatePhase.PausedDownloading
            ? await DownloadPlondsAsync(cancellationToken).ConfigureAwait(false)
            : new LanMountainDesktop.Services.Update.DownloadResult(false, null, $"Cannot resume in phase {_plondsPhase}.", false);
    }

    private async Task<InstallResult> InstallPlondsAsync(CancellationToken cancellationToken)
    {
        if (!_plondsPhase.CanInstall())
        {
            return new InstallResult(false, $"Cannot install in phase {_plondsPhase}.", false, "invalid_phase");
        }

        if (_pendingPlondsInstallerManifest is not null)
        {
            return await InstallPlondsCleanInstallAsync(cancellationToken).ConfigureAwait(false);
        }

        if (_pendingPlondsPackage is null)
        {
            return new InstallResult(false, "No PLONDS package has been prepared.", false, "staging_incomplete");
        }

        TransitionPlonds(UpdatePhase.Installing);
        var launcherRoot = UpdatePaths.ResolveLauncherRoot(AppContext.BaseDirectory);
        var progress = new Progress<InstallProgressReport>(report =>
        {
            _progressChanged?.Invoke(new UpdateProgressReport(
                UpdatePhase.Installing,
                report.Message,
                report.ProgressPercent / 100.0,
                null,
                report));
        });

        var install = await _plondsInstaller.InstallAsync(_pendingPlondsPackage, launcherRoot, progress, cancellationToken).ConfigureAwait(false);
        if (!install.Success)
        {
            TransitionPlonds(UpdatePhase.Failed);
            return new InstallResult(false, install.ErrorMessage, false, install.ErrorCode);
        }

        TransitionPlonds(UpdatePhase.Installed);
        return new InstallResult(true, null, false);
    }

    private async Task<InstallResult> InstallPlondsCleanInstallAsync(CancellationToken cancellationToken)
    {
        TransitionPlonds(UpdatePhase.Installing);
        var launcherRoot = UpdatePaths.ResolveLauncherRoot(AppContext.BaseDirectory);
        var progress = new Progress<InstallProgressReport>(report =>
        {
            _progressChanged?.Invoke(new UpdateProgressReport(
                UpdatePhase.Installing,
                report.Message,
                report.ProgressPercent / 100.0,
                null,
                report));
        });

        var install = await _plondsUpdateInstallGateway.InstallAsync(
                UpdatePayloadKind.FullInstaller,
                launcherRoot,
                progress,
                cancellationToken)
            .ConfigureAwait(false);

        if (!install.Success)
        {
            TransitionPlonds(UpdatePhase.Failed);
            return install;
        }

        TransitionPlonds(UpdatePhase.Installed);
        return install;
    }

    private async Task AutoCheckPlondsIfEnabledAsync(CancellationToken cancellationToken)
    {
        var settings = Get();
        if (string.Equals(UpdateSettingsValues.NormalizeMode(settings.UpdateMode), UpdateSettingsValues.ModeManual, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var report = await CheckPlondsAsync(cancellationToken).ConfigureAwait(false);
        if (report.IsUpdateAvailable && _plondsPhase.CanDownload())
        {
            await DownloadPlondsAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private bool IsPlondsSelected()
    {
        return !IsGitHubSelected();
    }

    private bool IsGitHubSelected()
    {
        var source = UpdateSettingsValues.NormalizeDownloadSource(Get().UpdateDownloadSource);
        return string.Equals(source, UpdateSettingsValues.DownloadSourceGitHub, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(source, UpdateSettingsValues.DownloadSourceGhProxy, StringComparison.OrdinalIgnoreCase);
    }

    private void TransitionPlonds(UpdatePhase phase)
    {
        if (_plondsPhase == phase)
        {
            return;
        }

        _plondsPhase = phase;
        _phaseChanged?.Invoke(phase);
        _progressChanged?.Invoke(new UpdateProgressReport(phase, string.Empty, 0, null, null));
    }

    private UpdateOrchestrator GetOrchestrator()
    {
        var orchestrator = _orchestrator.Value;
        if (!_orchestratorEventsSubscribed)
        {
            orchestrator.PhaseChanged += OnOrchestratorPhaseChanged;
            orchestrator.ProgressChanged += OnOrchestratorProgressChanged;
            _orchestratorEventsSubscribed = true;
        }

        return orchestrator;
    }

    private void OnOrchestratorPhaseChanged(UpdatePhase phase)
    {
        _phaseChanged?.Invoke(phase);
    }

    private void OnOrchestratorProgressChanged(UpdateProgressReport report)
    {
        _progressChanged?.Invoke(report);
    }

    private void SaveLastChecked()
    {
        var state = Get();
        Save(state with { LastUpdateCheckUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() });
    }

    private void SavePendingPlondsPackage(PlondsPreparedPackage package)
    {
        var state = Get();
        Save(state with
        {
            PendingUpdateInstallerPath = package.ManifestPath,
            PendingUpdateVersion = package.Version.ToString(),
            PendingUpdatePublishedAtUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            PendingUpdateSha256 = null,
            LastUpdateCheckUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        });
    }

    private void SavePendingPlondsInstaller(UpdateManifest manifest, string installerPath, string? sha256)
    {
        var state = Get();
        Save(state with
        {
            PendingUpdateInstallerPath = installerPath,
            PendingUpdateVersion = manifest.ToVersion,
            PendingUpdatePublishedAtUtcMs = manifest.PublishedAt.ToUnixTimeMilliseconds(),
            PendingUpdateSha256 = string.IsNullOrWhiteSpace(sha256) ? null : sha256.Trim().ToLowerInvariant(),
            LastUpdateCheckUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        });
    }

    private bool TryApplyPlondsOnExit()
    {
        var settings = Get();
        if (!string.Equals(
                UpdateSettingsValues.NormalizeMode(settings.UpdateMode),
                UpdateSettingsValues.ModeSilentOnExit,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var launcherRoot = UpdatePaths.ResolveLauncherRoot(AppContext.BaseDirectory);
        try
        {
            if (_pendingPlondsPackage is not null)
            {
                AppLogger.Info("UpdateWorkflow", "PLONDS package pending. Applying from Host on exit.");
                var result = _plondsInstaller.InstallAsync(
                        _pendingPlondsPackage,
                        launcherRoot,
                        progress: null,
                        CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();
                return result.Success;
            }

            var deploymentLock = DeploymentLockService.ReadLock(launcherRoot);
            if (!string.Equals(deploymentLock?.Kind, "full", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(settings.PendingUpdateInstallerPath) ||
                !File.Exists(settings.PendingUpdateInstallerPath))
            {
                return false;
            }

            AppLogger.Info("UpdateWorkflow", "PLONDS clean-install installer pending. Launching from Host Update on exit.");
            var install = _plondsUpdateInstallGateway.InstallAsync(
                    UpdatePayloadKind.FullInstaller,
                    launcherRoot,
                    progress: null,
                    CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            return install.Success;
        }
        catch (Exception ex)
        {
            AppLogger.Warn("UpdateWorkflow", "Failed to apply pending PLONDS update on exit.", ex);
            return false;
        }
    }

    private async Task<UpdateManifest?> ResolveGitHubInstallerManifestForPlondsAsync(
        PlondsClientManifest plondsManifest,
        CancellationToken cancellationToken)
    {
        foreach (var tag in BuildReleaseTagCandidates(plondsManifest.CurrentVersion))
        {
            try
            {
                var release = await _githubReleaseUpdateService
                    .GetReleaseByTagAsync(tag, cancellationToken)
                    .ConfigureAwait(false);
                if (release is null)
                {
                    continue;
                }

                return UpdateManifestMapper.FromFullInstaller(release, Get().UpdateChannel, ResolveCurrentPlatform());
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                AppLogger.Warn("UpdateWorkflow", $"Failed to resolve GitHub installer release '{tag}' for PLONDS clean install: {ex.Message}");
            }
        }

        return null;
    }

    private static IEnumerable<string> BuildReleaseTagCandidates(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            yield break;
        }

        var trimmed = version.Trim();
        if (trimmed.StartsWith("v", StringComparison.OrdinalIgnoreCase))
        {
            yield return trimmed;
            yield return trimmed[1..];
            yield break;
        }

        yield return $"v{trimmed}";
        yield return trimmed;
    }

    private static string CreateInstallerDestinationPath(UpdateManifest manifest, string fileName)
    {
        var safeFileName = string.Join(
            "_",
            fileName.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Trim();
        if (string.IsNullOrWhiteSpace(safeFileName))
        {
            safeFileName = $"{manifest.DistributionId}-{manifest.ToVersion}-installer.exe";
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LanMountainDesktop",
            "Updates",
            safeFileName);
    }

    private static string ResolveCurrentPlatform()
    {
        var os = OperatingSystem.IsWindows()
            ? "windows"
            : OperatingSystem.IsLinux()
                ? "linux"
                : OperatingSystem.IsMacOS()
                    ? "macos"
                    : "unknown";
        var arch = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture switch
        {
            System.Runtime.InteropServices.Architecture.Arm64 => "arm64",
            System.Runtime.InteropServices.Architecture.X86 => "x86",
            _ => "x64"
        };
        return $"{os}-{arch}";
    }

    private static bool TryParseVersion(string? value, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Trim().TrimStart('v', 'V');
        var separatorIndex = normalized.IndexOfAny(['-', '+', ' ']);
        if (separatorIndex > 0)
        {
            normalized = normalized[..separatorIndex];
        }

        return Version.TryParse(normalized, out version!);
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

        var dataRoot = AppDataPathProvider.GetPluginMarketDirectory();
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

        var dataRoot = AppDataPathProvider.GetPluginMarketDirectory();
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
            BuildCapabilities(entry));
    }

    private static IReadOnlyList<PluginCapabilityInfo> BuildCapabilities(AirAppMarketPluginEntry entry)
    {
        if (entry.Capabilities is null)
        {
            return [];
        }

        var capabilities = new List<PluginCapabilityInfo>();
        capabilities.AddRange(entry.Capabilities.SharedContracts.Select(contract =>
            new PluginCapabilityInfo(contract.Id, contract.Version, contract.AssemblyName)));
        capabilities.AddRange(entry.Capabilities.DesktopComponents.Select(id =>
            new PluginCapabilityInfo(id, null, null)));
        capabilities.AddRange(entry.Capabilities.SettingsSections.Select(id =>
            new PluginCapabilityInfo(id, null, null)));
        capabilities.AddRange(entry.Capabilities.Exports.Select(id =>
            new PluginCapabilityInfo(id, null, null)));
        capabilities.AddRange(entry.Capabilities.MessageTypes.Select(id =>
            new PluginCapabilityInfo(id, null, null)));

        return capabilities
            .DistinctBy(capability => $"{capability.Id}@{capability.Version}@{capability.AssemblyName}")
            .ToArray();
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
        return LanMountainDesktop.Shared.Contracts.Launcher.AppVersionProvider
            .ResolveForCurrentProcess()
            .Version;

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
        return LanMountainDesktop.Shared.Contracts.Launcher.AppVersionProvider
            .ResolveForCurrentProcess()
            .Codename;

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
