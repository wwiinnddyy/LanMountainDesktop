using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LanMountainDesktop.Models;
using LanMountainDesktop.PluginSdk;
using LanMountainDesktop.Services;
using LanMountainDesktop.Settings.Core;

namespace LanMountainDesktop.Services.Settings;

public enum WallpaperMediaType
{
    None,
    Image
}

public sealed record GridSettingsState(int ShortSideCells, string SpacingPreset, int EdgeInsetPercent);
public sealed record WallpaperSettingsState(
    string? WallpaperPath, 
    string Type, 
    string? Color, 
    string Placement, 
    string? CustomColor = null,
    int SystemWallpaperRefreshIntervalSeconds = 300);
public sealed record ThemeAppearanceSettingsState(
    bool IsNightMode,
    string? ThemeColor,
    bool UseSystemChrome,
    double GlobalCornerRadiusScale = GlobalAppearanceSettings.DefaultCornerRadiusScale,
    string ThemeColorMode = ThemeAppearanceValues.ColorModeDefaultNeutral,
    string SystemMaterialMode = ThemeAppearanceValues.MaterialNone,
    string? SelectedWallpaperSeed = null);
public sealed record StatusBarSettingsState(
    IReadOnlyList<string> TopStatusComponentIds,
    IReadOnlyList<string> PinnedTaskbarActions,
    bool EnableDynamicTaskbarActions,
    string TaskbarLayoutMode,
    string ClockDisplayFormat,
    bool ClockTransparentBackground,
    string SpacingMode,
    int CustomSpacingPercent);
public sealed record WeatherSettingsState(
    string LocationMode,
    string LocationKey,
    string LocationName,
    double Latitude,
    double Longitude,
    bool AutoRefreshLocation,
    string ExcludedAlerts,
    string IconPackId,
    bool NoTlsRequests,
    string LocationQuery);
public sealed record RegionSettingsState(string LanguageCode, string? TimeZoneId);
public sealed record PrivacySettingsState(
    bool UploadAnonymousCrashData,
    bool UploadAnonymousUsageData);
public sealed record UpdateSettingsState(
    bool IncludePrereleaseUpdates,
    string UpdateChannel,
    string UpdateMode,
    string UpdateDownloadSource,
    int UpdateDownloadThreads,
    string? PendingUpdateInstallerPath,
    string? PendingUpdateVersion,
    long? PendingUpdatePublishedAtUtcMs,
    long? LastUpdateCheckUtcMs);
public sealed record PluginManagementSettingsState(IReadOnlyList<string> DisabledPluginIds);
public sealed record PluginMarketDependencyInfo(
    string Id,
    string Version,
    string AssemblyName);
public sealed record PluginMarketPluginInfo(
    string Id,
    string Name,
    string Description,
    string Author,
    string Version,
    string ApiVersion,
    string MinHostVersion,
    string DownloadUrl,
    string ReleaseTag,
    string ReleaseAssetName,
    string IconUrl,
    string ReadmeUrl,
    string HomepageUrl,
    string RepositoryUrl,
    IReadOnlyList<string> Tags,
    IReadOnlyList<PluginMarketDependencyInfo> Dependencies,
    DateTimeOffset PublishedAt,
    DateTimeOffset UpdatedAt);
public sealed record PluginMarketIndexResult(
    bool Success,
    IReadOnlyList<PluginMarketPluginInfo> Plugins,
    string? Source,
    string? SourceLocation,
    string? WarningMessage,
    string? ErrorMessage);
public sealed record PluginMarketInstallResult(
    bool Success,
    string? PluginId,
    string? PluginName,
    string? ErrorMessage);

public interface IGridSettingsService
{
    GridSettingsState Get();
    void Save(GridSettingsState state);
    string NormalizeSpacingPreset(string? value);
    double ResolveGapRatio(string? preset);
    double CalculateEdgeInset(double hostWidth, double hostHeight, int shortSideCells, int insetPercent);
    DesktopGridMetrics CalculateGridMetrics(
        double hostWidth,
        double hostHeight,
        int shortSideCells,
        double gapRatio,
        double edgeInsetPx);
}

public interface IWallpaperSettingsService
{
    WallpaperSettingsState Get();
    void Save(WallpaperSettingsState state);
}

public interface IWallpaperMediaService
{
    WallpaperMediaType DetectMediaType(string? path);
    Task<string?> ImportAssetAsync(string sourcePath, CancellationToken cancellationToken = default);
}

public interface IThemeAppearanceService
{
    ThemeAppearanceSettingsState Get();
    void Save(ThemeAppearanceSettingsState state);
    MonetPalette BuildPalette(bool nightMode, string? wallpaperPath, string? preferredSeedColor = null);
}

public interface IStatusBarSettingsService
{
    StatusBarSettingsState Get();
    void Save(StatusBarSettingsState state);
}

public interface IWeatherProvider
{
    Task<WeatherQueryResult<IReadOnlyList<WeatherLocation>>> SearchLocationsAsync(
        string keyword,
        string? locale = null,
        CancellationToken cancellationToken = default);

    Task<WeatherQueryResult<WeatherSnapshot>> GetWeatherAsync(
        WeatherQuery query,
        CancellationToken cancellationToken = default);

    Task<WeatherQueryResult<WeatherLocation>> ResolveLocationAsync(
        double latitude,
        double longitude,
        string? locale = null,
        CancellationToken cancellationToken = default);
}

public interface IWeatherSettingsService
{
    WeatherSettingsState Get();
    void Save(WeatherSettingsState state);
    Task<WeatherQueryResult<IReadOnlyList<WeatherLocation>>> SearchLocationsAsync(
        string keyword,
        string? locale = null,
        CancellationToken cancellationToken = default);
    Task<WeatherQueryResult<WeatherLocation>> ResolveLocationAsync(
        double latitude,
        double longitude,
        string? locale = null,
        CancellationToken cancellationToken = default);
    IWeatherInfoService GetWeatherInfoService();
}

public interface IRegionSettingsService
{
    RegionSettingsState Get();
    void Save(RegionSettingsState state);
    TimeZoneService GetTimeZoneService();
}

public interface IPrivacySettingsService
{
    PrivacySettingsState Get();
    void Save(PrivacySettingsState state);
}

public interface IUpdateSettingsService
{
    UpdateSettingsState Get();
    void Save(UpdateSettingsState state);
    Task<UpdateCheckResult> CheckForUpdatesAsync(Version currentVersion, bool includePrerelease, CancellationToken cancellationToken = default);
    Task<UpdateDownloadResult> DownloadAssetAsync(
        GitHubReleaseAsset asset,
        string destinationFilePath,
        string downloadSource,
        int maxParallelSegments,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);
}

public interface ILauncherCatalogService
{
    StartMenuFolderNode LoadCatalog();
}

public interface ILauncherPolicyService
{
    LauncherSettingsSnapshot Get();
    void Save(LauncherSettingsSnapshot snapshot);
}

public interface IPluginManagementSettingsService
{
    PluginManagementSettingsState Get();
    void Save(PluginManagementSettingsState state);
    IReadOnlyList<InstalledPluginInfo> GetInstalledPlugins();
    bool SetPluginEnabled(string pluginId, bool isEnabled);
    bool DeleteInstalledPlugin(string pluginId);
}

public interface IPluginMarketSettingsService
{
    Task<PluginMarketIndexResult> LoadIndexAsync(CancellationToken cancellationToken = default);
    Task<PluginMarketInstallResult> InstallAsync(string pluginId, CancellationToken cancellationToken = default);
}

public interface IApplicationInfoService
{
    string GetAppVersionText();
    string GetAppCodenameText();
    AppRenderBackendInfo GetRenderBackendInfo();
}

public interface ISettingsFacadeService
{
    ISettingsService Settings { get; }
    ISettingsCatalog Catalog { get; }
    IGridSettingsService Grid { get; }
    IWallpaperSettingsService Wallpaper { get; }
    IWallpaperMediaService WallpaperMedia { get; }
    IThemeAppearanceService Theme { get; }
    IStatusBarSettingsService StatusBar { get; }
    IWeatherSettingsService Weather { get; }
    IRegionSettingsService Region { get; }
    IPrivacySettingsService Privacy { get; }
    IUpdateSettingsService Update { get; }
    ILauncherCatalogService LauncherCatalog { get; }
    ILauncherPolicyService LauncherPolicy { get; }
    IPluginManagementSettingsService PluginManagement { get; }
    IPluginMarketSettingsService PluginMarket { get; }
    IApplicationInfoService ApplicationInfo { get; }
}
