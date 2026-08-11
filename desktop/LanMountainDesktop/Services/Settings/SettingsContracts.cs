using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LanMountainDesktop.Models;
using LanMountainDesktop.AirAppSdk;
using LanMountainDesktop.Services;
using LanMountainDesktop.Services.Update;
using LanMountainDesktop.Services.AirAppMarket;
using LanMountainDesktop.AirAppSdk;
using LanMountainDesktop.Shared.Contracts.Update;

namespace LanMountainDesktop.Services.Settings
{

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
    int SystemWallpaperRefreshIntervalSeconds = 300);
public sealed record ThemeAppearanceSettingsState(
    bool IsNightMode,
    string? ThemeColor,
    bool UseSystemChrome,
    string CornerRadiusStyle = GlobalAppearanceSettings.DefaultCornerRadiusStyle,
    string ThemeColorMode = ThemeAppearanceValues.ColorModeDefaultNeutral,
    string SystemMaterialMode = ThemeAppearanceValues.MaterialNone,
    string? SelectedWallpaperSeed = null,
    string ThemeMode = ThemeAppearanceValues.ThemeModeLight,
    string ThemeWallpaperColorSource = ThemeAppearanceValues.WallpaperColorSourceAuto,
    bool UseNativeWallpaperChangeEvents = true);
public sealed record StatusBarSettingsState(
    IReadOnlyList<string> TopStatusComponentIds,
    IReadOnlyList<string> PinnedTaskbarActions,
    bool EnableDynamicTaskbarActions,
    string TaskbarLayoutMode,
    string ClockDisplayFormat,
    bool ClockTransparentBackground,
    string ClockPosition,
    string ClockFontSize,
    bool ShowTextCapsule,
    string TextCapsuleContent,
    string TextCapsulePosition,
    bool TextCapsuleTransparentBackground,
    string TextCapsuleFontSize,
    bool ShowNetworkSpeed,
    string NetworkSpeedPosition,
    string NetworkSpeedDisplayMode,
    bool NetworkSpeedTransparentBackground,
    bool ShowNetworkTypeIcon,
    string NetworkSpeedFontSize,
    string SpacingMode,
    int CustomSpacingPercent,
    bool ShadowEnabled,
    string ShadowColor,
    double ShadowOpacity);

public sealed record TextCapsuleSettingsState(
    bool ShowTextCapsule,
    string Content,
    string Position,
    bool TransparentBackground);

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
    bool ForceUpdateReinstall,
    bool UseGhProxyMirror,
    string? PendingUpdateInstallerPath,
    string? PendingUpdateVersion,
    long? PendingUpdatePublishedAtUtcMs,
    long? LastUpdateCheckUtcMs,
    string? PendingUpdateSha256);
public sealed record AirAppManagementSettingsState(IReadOnlyList<string> DisabledPluginIds);
public enum AirAppPackageSourceKind
{
    ReleaseAsset = 0,
    RawFallback = 1,
    WorkspaceLocal = 2
}

public sealed record AirAppCatalogSourceInfo(
    string Id,
    string Name,
    string? Description,
    string? SourceUrl,
    string? CachePath,
    bool IsOfficial,
    int Priority);

public sealed record AirAppCatalogSharedContractInfo(
    string Id,
    string Version,
    string AssemblyName);

public sealed record AirAppCapabilityInfo(
    string Id,
    string? Version,
    string? AssemblyName);

public sealed record AirAppPackageSourceInfo(
    AirAppPackageSourceKind Kind,
    string Url,
    string Sha256,
    long PackageSizeBytes);

public sealed record AirAppCatalogManifestInfo(
    string Id,
    string Name,
    string Description,
    string Author,
    string Version,
    string ApiVersion,
    string EntranceAssembly,
    IReadOnlyList<AirAppCatalogSharedContractInfo> SharedContracts);

public sealed record AirAppCatalogCompatibilityInfo(
    string MinHostVersion,
    string ApiVersion);

public sealed record AirAppCatalogRepositoryInfo(
    string IconUrl,
    string ProjectUrl,
    string ReadmeUrl,
    string HomepageUrl,
    string RepositoryUrl,
    IReadOnlyList<string> Tags,
    string ReleaseNotes);

public sealed record AirAppCatalogPublicationInfo(
    string ReleaseTag,
    string ReleaseAssetName,
    DateTimeOffset PublishedAt,
    DateTimeOffset UpdatedAt,
    long PackageSizeBytes,
    string Sha256,
    string? Md5);

public sealed record AirAppCatalogItemInfo(
    AirAppCatalogManifestInfo Manifest,
    AirAppCatalogCompatibilityInfo Compatibility,
    AirAppCatalogRepositoryInfo Repository,
    AirAppCatalogPublicationInfo Publication,
    IReadOnlyList<AirAppPackageSourceInfo> PackageSources,
    IReadOnlyList<AirAppCapabilityInfo> Capabilities)
{
    public string Id => Manifest.Id;

    public string Name => Manifest.Name;

    public string Description => Manifest.Description;

    public string Author => Manifest.Author;

    public string Version => Manifest.Version;

    public string ApiVersion => Manifest.ApiVersion;

    public string MinHostVersion => Compatibility.MinHostVersion;

    public string DownloadUrl => PackageSources.FirstOrDefault()?.Url ?? string.Empty;

    public string Sha256 => Publication.Sha256;

    public long PackageSizeBytes => Publication.PackageSizeBytes;

    public string IconUrl => Repository.IconUrl;

    public string ProjectUrl => Repository.ProjectUrl;

    public string ReadmeUrl => Repository.ReadmeUrl;

    public string HomepageUrl => Repository.HomepageUrl;

    public string RepositoryUrl => Repository.RepositoryUrl;

    public IReadOnlyList<string> Tags => Repository.Tags;

    public IReadOnlyList<AirAppCatalogSharedContractInfo> SharedContracts => Manifest.SharedContracts;

    public DateTimeOffset PublishedAt => Publication.PublishedAt;

    public DateTimeOffset UpdatedAt => Publication.UpdatedAt;

    public string ReleaseTag => Publication.ReleaseTag;

    public string ReleaseAssetName => Publication.ReleaseAssetName;

    public string ReleaseNotes => Repository.ReleaseNotes;
}

public sealed record AirAppCatalogIndexResult(
    bool Success,
    IReadOnlyList<AirAppCatalogItemInfo> AirApps,
    IReadOnlyList<AirAppCatalogSourceInfo> Sources,
    string? Source,
    string? SourceLocation,
    string? WarningMessage,
    string? ErrorMessage);

public sealed record AirAppInstallDiagnostic(
    string Code,
    string Message,
    string? Details = null);

public sealed record AirAppCatalogInstallResult(
    bool Success,
    string? AirAppId,
    string? AirAppName,
    AirAppManifest? InstalledManifest,
    IReadOnlyList<AirAppInstallDiagnostic> Diagnostics,
    string? ErrorMessage);

public interface IAirAppCatalogSourceProvider
{
    Task<AirAppCatalogIndexResult> LoadCatalogAsync(CancellationToken cancellationToken = default);
}

public interface IAirAppCatalogService : IAirAppCatalogSourceProvider
{
    Task<AirAppCatalogInstallResult> InstallAsync(string pluginId, CancellationToken cancellationToken = default);
}

public interface IPackageSourceResolver
{
    IReadOnlyList<AirAppPackageSourceInfo> ResolveSources(AirAppCatalogItemInfo item);
}

public interface IAirAppCompatibilityEvaluator
{
    AirAppInstallDiagnostic? Evaluate(AirAppCatalogItemInfo item, Version? hostVersion);
}

public interface IAirAppInstallOrchestrator
{
    Task<AirAppCatalogInstallResult> InstallAsync(AirAppCatalogItemInfo item, CancellationToken cancellationToken = default);
}

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

public interface ITextCapsuleSettingsService
{
    TextCapsuleSettingsState Get();
    void Save(TextCapsuleSettingsState state);
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
    UpdatePhase CurrentPhase { get; }
    event Action<UpdatePhase>? PhaseChanged;
    event Action<UpdateProgressReport>? ProgressChanged;

    UpdateSettingsState Get();
    void Save(UpdateSettingsState state);
    Task<UpdateCheckReport> CheckAsync(CancellationToken cancellationToken = default);
    Task<LanMountainDesktop.Services.Update.DownloadResult> DownloadAsync(CancellationToken cancellationToken = default);
    Task<InstallResult> InstallAsync(CancellationToken cancellationToken = default);
    Task RollbackAsync(CancellationToken cancellationToken = default);
    Task PauseAsync();
    Task<LanMountainDesktop.Services.Update.DownloadResult> ResumeAsync(CancellationToken cancellationToken = default);
    Task CancelAsync();
    Task AutoCheckIfEnabledAsync(CancellationToken cancellationToken = default);
    bool TryApplyOnExit();
    Task<UpdateCheckResult> CheckForUpdatesAsync(Version currentVersion, bool includePrerelease, CancellationToken cancellationToken = default);
    Task<UpdateCheckResult> ForceCheckForUpdatesAsync(Version currentVersion, bool includePrerelease, CancellationToken cancellationToken = default);
    Task<UpdateDownloadResult> DownloadAssetAsync(
        GitHubReleaseAsset asset,
        string destinationFilePath,
        string downloadSource,
        int maxParallelSegments,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);
    Task<UpdateDownloadResult> RedownloadAssetAsync(
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

public interface IAirAppManagementSettingsService
{
    AirAppManagementSettingsState Get();
    void Save(AirAppManagementSettingsState state);
    IReadOnlyList<AirAppInstalledInfo> GetInstalledAirApps();
    bool SetAirAppEnabled(string pluginId, bool isEnabled);
    bool DeleteInstalledAirApp(string pluginId);
}

public interface IAirAppCatalogSettingsService : IAirAppCatalogSourceProvider
{
    new Task<AirAppCatalogIndexResult> LoadCatalogAsync(CancellationToken cancellationToken = default);
    Task<AirAppCatalogInstallResult> InstallAsync(string pluginId, CancellationToken cancellationToken = default);
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
    ITextCapsuleSettingsService TextCapsule { get; }
    IWeatherSettingsService Weather { get; }
    IRegionSettingsService Region { get; }
    IPrivacySettingsService Privacy { get; }
    IUpdateSettingsService Update { get; }
    ILauncherCatalogService LauncherCatalog { get; }
    ILauncherPolicyService LauncherPolicy { get; }
    IAirAppManagementSettingsService AirAppManagement { get; }
    IAirAppCatalogSettingsService AirAppCatalog { get; }
    IApplicationInfoService ApplicationInfo { get; }
}

}

namespace LanMountainDesktop.Services.AirAppMarket
{
    internal enum AirAppPackageSourceKind
    {
        ReleaseAsset = 0,
        RawFallback = 1,
        WorkspaceLocal = 2
    }
}
