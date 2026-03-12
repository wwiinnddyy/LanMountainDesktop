using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using LanMountainDesktop.Models;
using LanMountainDesktop.PluginSdk;
using LanMountainDesktop.Services.PluginMarket;

namespace LanMountainDesktop.Services.Settings;

internal sealed class GridSettingsService : IGridSettingsService
{
    private readonly AppSettingsService _appSettingsService = new();

    public GridSettingsState Get()
    {
        var snapshot = _appSettingsService.Load();
        return new GridSettingsState(
            snapshot.GridShortSideCells,
            snapshot.GridSpacingPreset,
            snapshot.DesktopEdgeInsetPercent);
    }

    public void Save(GridSettingsState state)
    {
        var snapshot = _appSettingsService.Load();
        snapshot.GridShortSideCells = state.ShortSideCells;
        snapshot.GridSpacingPreset = state.SpacingPreset;
        snapshot.DesktopEdgeInsetPercent = state.EdgeInsetPercent;
        _appSettingsService.Save(snapshot);
    }
}

internal sealed class WallpaperSettingsService : IWallpaperSettingsService
{
    private readonly AppSettingsService _appSettingsService = new();

    public WallpaperSettingsState Get()
    {
        var snapshot = _appSettingsService.Load();
        return new WallpaperSettingsState(snapshot.WallpaperPath, snapshot.WallpaperPlacement);
    }

    public void Save(WallpaperSettingsState state)
    {
        var snapshot = _appSettingsService.Load();
        snapshot.WallpaperPath = state.WallpaperPath;
        snapshot.WallpaperPlacement = string.IsNullOrWhiteSpace(state.Placement)
            ? "Fill"
            : state.Placement.Trim();
        _appSettingsService.Save(snapshot);
    }
}

internal sealed class WallpaperMediaService : IWallpaperMediaService
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp"
    };

    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mkv", ".webm", ".avi", ".mov", ".m4v"
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

        if (VideoExtensions.Contains(extension))
        {
            return WallpaperMediaType.Video;
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
    private readonly AppSettingsService _appSettingsService = new();
    private readonly MonetColorService _monetColorService = new();
    private readonly WallpaperMediaService _wallpaperMediaService = new();

    public ThemeAppearanceSettingsState Get()
    {
        var snapshot = _appSettingsService.Load();
        return new ThemeAppearanceSettingsState(
            snapshot.IsNightMode ?? false,
            snapshot.ThemeColor);
    }

    public void Save(ThemeAppearanceSettingsState state)
    {
        var snapshot = _appSettingsService.Load();
        snapshot.IsNightMode = state.IsNightMode;
        snapshot.ThemeColor = state.ThemeColor;
        _appSettingsService.Save(snapshot);
    }

    public MonetPalette BuildPalette(bool nightMode, string? wallpaperPath)
    {
        Bitmap? bitmap = null;

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
            return _monetColorService.BuildPalette(bitmap, nightMode);
        }
        finally
        {
            bitmap?.Dispose();
        }
    }
}

internal sealed class StatusBarSettingsService : IStatusBarSettingsService
{
    private readonly AppSettingsService _appSettingsService = new();

    public StatusBarSettingsState Get()
    {
        var snapshot = _appSettingsService.Load();
        return new StatusBarSettingsState(
            snapshot.TopStatusComponentIds?.ToArray() ?? [],
            snapshot.PinnedTaskbarActions?.ToArray() ?? [],
            snapshot.EnableDynamicTaskbarActions,
            snapshot.TaskbarLayoutMode,
            snapshot.ClockDisplayFormat,
            snapshot.StatusBarSpacingMode,
            snapshot.StatusBarCustomSpacingPercent);
    }

    public void Save(StatusBarSettingsState state)
    {
        var snapshot = _appSettingsService.Load();
        snapshot.TopStatusComponentIds = state.TopStatusComponentIds?.ToList() ?? [];
        snapshot.PinnedTaskbarActions = state.PinnedTaskbarActions?.ToList() ?? [];
        snapshot.EnableDynamicTaskbarActions = state.EnableDynamicTaskbarActions;
        snapshot.TaskbarLayoutMode = state.TaskbarLayoutMode;
        snapshot.ClockDisplayFormat = state.ClockDisplayFormat;
        snapshot.StatusBarSpacingMode = state.SpacingMode;
        snapshot.StatusBarCustomSpacingPercent = state.CustomSpacingPercent;
        _appSettingsService.Save(snapshot);
    }
}

internal sealed class WeatherProviderAdapter : IWeatherProvider
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
}

internal sealed class WeatherSettingsService : IWeatherSettingsService
{
    private readonly AppSettingsService _appSettingsService = new();

    public WeatherSettingsState Get()
    {
        var snapshot = _appSettingsService.Load();
        return new WeatherSettingsState(
            snapshot.WeatherLocationMode,
            snapshot.WeatherLocationKey,
            snapshot.WeatherLocationName,
            snapshot.WeatherLatitude,
            snapshot.WeatherLongitude,
            snapshot.WeatherAutoRefreshLocation,
            snapshot.WeatherExcludedAlerts,
            snapshot.WeatherIconPackId,
            snapshot.WeatherNoTlsRequests,
            snapshot.WeatherLocationQuery);
    }

    public void Save(WeatherSettingsState state)
    {
        var snapshot = _appSettingsService.Load();
        snapshot.WeatherLocationMode = state.LocationMode;
        snapshot.WeatherLocationKey = state.LocationKey;
        snapshot.WeatherLocationName = state.LocationName;
        snapshot.WeatherLatitude = state.Latitude;
        snapshot.WeatherLongitude = state.Longitude;
        snapshot.WeatherAutoRefreshLocation = state.AutoRefreshLocation;
        snapshot.WeatherExcludedAlerts = state.ExcludedAlerts;
        snapshot.WeatherIconPackId = state.IconPackId;
        snapshot.WeatherNoTlsRequests = state.NoTlsRequests;
        snapshot.WeatherLocationQuery = state.LocationQuery;
        _appSettingsService.Save(snapshot);
    }
}

internal sealed class RegionSettingsService : IRegionSettingsService
{
    private readonly AppSettingsService _appSettingsService = new();

    public RegionSettingsState Get()
    {
        var snapshot = _appSettingsService.Load();
        return new RegionSettingsState(snapshot.LanguageCode, snapshot.TimeZoneId);
    }

    public void Save(RegionSettingsState state)
    {
        var snapshot = _appSettingsService.Load();
        snapshot.LanguageCode = string.IsNullOrWhiteSpace(state.LanguageCode)
            ? "zh-CN"
            : state.LanguageCode.Trim();
        snapshot.TimeZoneId = string.IsNullOrWhiteSpace(state.TimeZoneId)
            ? null
            : state.TimeZoneId.Trim();
        _appSettingsService.Save(snapshot);
    }
}

internal sealed class UpdateSettingsService : IUpdateSettingsService, IDisposable
{
    private readonly AppSettingsService _appSettingsService = new();
    private readonly GitHubReleaseUpdateService _releaseUpdateService = new("wwiinnddyy", "LanMountainDesktop");

    public UpdateSettingsState Get()
    {
        var snapshot = _appSettingsService.Load();
        return new UpdateSettingsState(
            snapshot.AutoCheckUpdates,
            snapshot.IncludePrereleaseUpdates,
            snapshot.UpdateChannel);
    }

    public void Save(UpdateSettingsState state)
    {
        var snapshot = _appSettingsService.Load();
        snapshot.AutoCheckUpdates = state.AutoCheckUpdates;
        snapshot.IncludePrereleaseUpdates = state.IncludePrereleaseUpdates;
        snapshot.UpdateChannel = state.UpdateChannel;
        _appSettingsService.Save(snapshot);
    }

    public Task<UpdateCheckResult> CheckForUpdatesAsync(
        Version currentVersion,
        bool includePrerelease,
        CancellationToken cancellationToken = default)
    {
        return _releaseUpdateService.CheckForUpdatesAsync(currentVersion, includePrerelease, cancellationToken);
    }

    public Task<UpdateDownloadResult> DownloadAssetAsync(
        GitHubReleaseAsset asset,
        string destinationFilePath,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return _releaseUpdateService.DownloadAssetAsync(asset, destinationFilePath, progress, cancellationToken);
    }

    public void Dispose()
    {
        _releaseUpdateService.Dispose();
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
    private readonly AppSettingsService _appSettingsService = new();
    private readonly PluginRuntimeService? _pluginRuntimeService;

    public PluginManagementSettingsService(PluginRuntimeService? pluginRuntimeService)
    {
        _pluginRuntimeService = pluginRuntimeService;
    }

    public PluginManagementSettingsState Get()
    {
        var snapshot = _appSettingsService.Load();
        return new PluginManagementSettingsState(snapshot.DisabledPluginIds?.ToArray() ?? []);
    }

    public void Save(PluginManagementSettingsState state)
    {
        var snapshot = _appSettingsService.Load();
        snapshot.DisabledPluginIds = state.DisabledPluginIds?.ToList() ?? [];
        _appSettingsService.Save(snapshot);
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

internal sealed class PluginMarketSettingsService : IPluginMarketSettingsService, IDisposable
{
    private readonly PluginRuntimeService? _pluginRuntimeService;
    private readonly AirAppMarketIndexService _indexService;
    private readonly AirAppMarketInstallService? _installService;
    private readonly Dictionary<string, AirAppMarketPluginEntry> _cachedPlugins = new(StringComparer.OrdinalIgnoreCase);

    public PluginMarketSettingsService(PluginRuntimeService? pluginRuntimeService)
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

    public async Task<PluginMarketIndexResult> LoadIndexAsync(CancellationToken cancellationToken = default)
    {
        var result = await _indexService.LoadAsync(cancellationToken);
        if (!result.Success || result.Document is null)
        {
            return new PluginMarketIndexResult(
                false,
                [],
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
                return new PluginMarketPluginInfo(
                    entry.Id,
                    entry.Name,
                    entry.Description,
                    entry.Author,
                    entry.Version,
                    entry.ApiVersion,
                    entry.MinHostVersion,
                    entry.DownloadUrl,
                    entry.ReleaseTag,
                    entry.ReleaseAssetName,
                    entry.IconUrl,
                    entry.ReadmeUrl,
                    entry.HomepageUrl,
                    entry.RepositoryUrl,
                    entry.Tags,
                    entry.PublishedAt,
                    entry.UpdatedAt);
            })
            .ToArray();

        return new PluginMarketIndexResult(
            true,
            plugins,
            result.Source?.ToString(),
            result.SourceLocation,
            result.WarningMessage,
            null);
    }

    public async Task<PluginMarketInstallResult> InstallAsync(
        string pluginId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(pluginId))
        {
            return new PluginMarketInstallResult(false, null, null, "Plugin id is required.");
        }

        if (_installService is null || _pluginRuntimeService is null)
        {
            return new PluginMarketInstallResult(
                false,
                pluginId,
                null,
                "Plugin runtime is unavailable.");
        }

        if (!_cachedPlugins.TryGetValue(pluginId, out var entry))
        {
            var load = await LoadIndexAsync(cancellationToken);
            if (!load.Success)
            {
                return new PluginMarketInstallResult(false, pluginId, null, load.ErrorMessage);
            }

            if (!_cachedPlugins.TryGetValue(pluginId, out entry))
            {
                return new PluginMarketInstallResult(false, pluginId, null, "Plugin was not found in market index.");
            }
        }

        var result = await _installService.InstallAsync(entry, cancellationToken);
        if (!result.Success)
        {
            return new PluginMarketInstallResult(false, entry.Id, entry.Name, result.ErrorMessage);
        }

        return new PluginMarketInstallResult(true, result.Manifest?.Id ?? entry.Id, result.Manifest?.Name ?? entry.Name, null);
    }

    public void Dispose()
    {
        _indexService.Dispose();
        _installService?.Dispose();
    }
}

internal sealed class ApplicationInfoService : IApplicationInfoService
{
    public string GetAppVersionText()
    {
        var version = typeof(App).Assembly.GetName().Version;
        return version is null
            ? "0.0.0"
            : new Version(
                Math.Max(0, version.Major),
                Math.Max(0, version.Minor),
                Math.Max(0, version.Build)).ToString(3);
    }

    public AppRenderBackendInfo GetRenderBackendInfo()
    {
        return AppRenderBackendDiagnostics.Detect();
    }
}

internal sealed class SettingsFacadeService : ISettingsFacadeService, IDisposable
{
    private readonly UpdateSettingsService _updateSettingsService;
    private readonly PluginMarketSettingsService _pluginMarketSettingsService;

    public SettingsFacadeService(PluginRuntimeService? pluginRuntimeService = null)
    {
        Settings = new SettingsService();
        Catalog = new SettingsCatalogService();
        Grid = new GridSettingsService();
        Wallpaper = new WallpaperSettingsService();
        WallpaperMedia = new WallpaperMediaService();
        Theme = new ThemeAppearanceService();
        StatusBar = new StatusBarSettingsService();
        Weather = new WeatherSettingsService();
        Region = new RegionSettingsService();
        _updateSettingsService = new UpdateSettingsService();
        Update = _updateSettingsService;
        LauncherCatalog = new LauncherCatalogService();
        LauncherPolicy = new LauncherPolicyService();
        PluginManagement = new PluginManagementSettingsService(pluginRuntimeService);
        _pluginMarketSettingsService = new PluginMarketSettingsService(pluginRuntimeService);
        PluginMarket = _pluginMarketSettingsService;
        ApplicationInfo = new ApplicationInfoService();
    }

    public ISettingsService Settings { get; }

    public ISettingsCatalog Catalog { get; }

    public IGridSettingsService Grid { get; }

    public IWallpaperSettingsService Wallpaper { get; }

    public IWallpaperMediaService WallpaperMedia { get; }

    public IThemeAppearanceService Theme { get; }

    public IStatusBarSettingsService StatusBar { get; }

    public IWeatherSettingsService Weather { get; }

    public IRegionSettingsService Region { get; }

    public IUpdateSettingsService Update { get; }

    public ILauncherCatalogService LauncherCatalog { get; }

    public ILauncherPolicyService LauncherPolicy { get; }

    public IPluginManagementSettingsService PluginManagement { get; }

    public IPluginMarketSettingsService PluginMarket { get; }

    public IApplicationInfoService ApplicationInfo { get; }

    public void Dispose()
    {
        _updateSettingsService.Dispose();
        _pluginMarketSettingsService.Dispose();
    }
}
