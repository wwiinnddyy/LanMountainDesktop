using System;
using Avalonia.Controls;
using LanMountainDesktop.AirAppSdk;
using LanMountainDesktop.Services;
using LanMountainDesktop.Services.AirAppMarket;
using LanMountainDesktop.Services.Settings;
using LanMountainDesktop.ViewModels;

namespace LanMountainDesktop.Views.SettingsPages;

[AirAppSettingsPageInfo(
    "plugin-catalog",
    "AirApp Catalog",
    AirAppSettingsPageCategory.AirAppCatalog,
    IconKey = "ShoppingBag",
    SortOrder = 35,
    TitleLocalizationKey = "settings.plugin_catalog.title",
    DescriptionLocalizationKey = "settings.plugin_catalog.subtitle")]
public partial class AirAppCatalogSettingsPage : AirAppSettingsPageBase
{
    public AirAppCatalogSettingsPage()
        : this(Design.IsDesignMode ? CreateDesignTimeViewModel() : CreateDefaultViewModel())
    {
    }

    public AirAppCatalogSettingsPage(AirAppCatalogSettingsPageViewModel viewModel)
    {
        ViewModel = viewModel;
        ViewModel.RestartRequested += OnRestartRequested;
        ViewModel.DetailsRequested += OnDetailsRequested;
        DataContext = ViewModel;
        InitializeComponent();
    }

    public AirAppCatalogSettingsPageViewModel ViewModel { get; }

    public override async void OnNavigatedTo(object? parameter)
    {
        if (Design.IsDesignMode)
        {
            return;
        }

        await ViewModel.InitializeAsync();
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        // The settings window may keep pages alive while navigating between them; release the
        // icon bitmaps and HTTP clients held by the view model when this page leaves the tree.
        if (!Design.IsDesignMode)
        {
            ViewModel.Dispose();
        }
    }

    private static AirAppCatalogSettingsPageViewModel CreateDefaultViewModel()
    {
        var settingsFacade = HostSettingsFacadeProvider.GetOrCreate();
        var localizationService = new LocalizationService();
        var assetCache = new AirAppMarketAssetCacheService(AppDataPathProvider.GetAirAppMarketDirectory());
        return new AirAppCatalogSettingsPageViewModel(
            settingsFacade,
            localizationService,
            new AirAppMarketIconService(assetCache),
            new AirAppMarketReadmeService(assetCache));
    }

    private static AirAppCatalogSettingsPageViewModel CreateDesignTimeViewModel()
    {
        var settingsFacade = HostSettingsFacadeProvider.GetOrCreate();
        var localizationService = new LocalizationService();
        var assetCache = new AirAppMarketAssetCacheService(AppDataPathProvider.GetAirAppMarketDirectory());
        var viewModel = new AirAppCatalogSettingsPageViewModel(
            settingsFacade,
            localizationService,
            new AirAppMarketIconService(assetCache),
            new AirAppMarketReadmeService(assetCache));

        var previewHostVersion = new Version(1, 2, 0);
        var items = new[]
        {
            CreateCatalogItemViewModel(
                CreateCatalogItem(
                    "news-tiles",
                    "News Tiles",
                    "Brings editorial news cards and ticker rows to the desktop.",
                    "LanMountain Labs",
                    "1.2.0",
                    "1.0.0",
                    "1.0.0",
                    "https://example.com/news-tiles.zip",
                    "v1.2.0",
                    "news-tiles.zip",
                    string.Empty,
                    "https://example.com/news-tiles/readme",
                    "https://example.com/news-tiles",
                    "https://example.com/news-tiles/repo",
                    ["news", "widgets"],
                    [],
                    DateTimeOffset.Now.AddDays(-8),
                    DateTimeOffset.Now.AddDays(-2)),
                localizationService,
                installedAirApp: null,
                previewHostVersion),
            CreateCatalogItemViewModel(
                CreateCatalogItem(
                    "workspace-pulse",
                    "Workspace Pulse",
                    "Tracks active projects and shows a compact productivity summary.",
                    "Studio North",
                    "2.4.0",
                    "1.0.0",
                    "1.0.0",
                    "https://example.com/workspace-pulse.zip",
                    "v2.4.0",
                    "workspace-pulse.zip",
                    string.Empty,
                    "https://example.com/workspace-pulse/readme",
                    "https://example.com/workspace-pulse",
                    "https://example.com/workspace-pulse/repo",
                    ["dashboard", "productivity"],
                    [],
                    DateTimeOffset.Now.AddDays(-30),
                    DateTimeOffset.Now.AddDays(-1)),
                localizationService,
                new AirAppInstalledInfo(
                    new AirAppManifest(
                        "workspace-pulse",
                        "Workspace Pulse",
                        "WorkspacePulse.dll",
                        "Tracks active projects and shows a compact productivity summary.",
                        "Studio North",
                        "2.1.0"),
                    true,
                    true,
                    true,
                    null),
                previewHostVersion),
            CreateCatalogItemViewModel(
                CreateCatalogItem(
                    "glass-panels",
                    "Glass Panels",
                    "Adds experimental acrylic surfaces for plugin-powered widgets.",
                    "Aster Team",
                    "0.8.0",
                    "1.0.0",
                    "9.0.0",
                    "https://example.com/glass-panels.zip",
                    "v0.8.0",
                    "glass-panels.zip",
                    string.Empty,
                    "https://example.com/glass-panels/readme",
                    "https://example.com/glass-panels",
                    "https://example.com/glass-panels/repo",
                    ["theme", "experimental"],
                    [],
                    DateTimeOffset.Now.AddDays(-12),
                    DateTimeOffset.Now.AddDays(-3)),
                localizationService,
                installedAirApp: null,
                previewHostVersion)
        };

        foreach (var item in items)
        {
            viewModel.CatalogAirApps.Add(item);
            viewModel.FilteredAirApps.Add(item);
        }

        viewModel.ShowEmptyState = false;
        viewModel.EmptyStateText = string.Empty;
        viewModel.StatusMessage = "Showing 3 mocked marketplace plugins for Avalonia design mode.";
        return viewModel;
    }

    private void OnRestartRequested(string? reason)
    {
        RequestRestart(reason ?? ViewModel.RestartRequiredMessage);
    }

    private async void OnDetailsRequested(AirAppCatalogItemViewModel item)
    {
        var detailViewModel = ViewModel.CreateDetailViewModel(item);
        var drawer = new AirAppCatalogDetailDrawer(detailViewModel);
        OpenDrawer(drawer, detailViewModel.DrawerTitle);
        await detailViewModel.InitializeAsync();
    }

    private static AirAppCatalogItemViewModel CreateCatalogItemViewModel(
        AirAppCatalogItemInfo plugin,
        LocalizationService localizationService,
        AirAppInstalledInfo? installedAirApp,
        Version hostVersion)
    {
        var languageCode = localizationService.NormalizeLanguageCode(
            HostSettingsFacadeProvider.GetOrCreate().Region.Get().LanguageCode);
        var item = new AirAppCatalogItemViewModel(plugin, localizationService, languageCode);
        item.ApplyInstallState(installedAirApp, hostVersion);
        return item;
    }

    private static AirAppCatalogItemInfo CreateCatalogItem(
        string id,
        string name,
        string description,
        string author,
        string version,
        string apiVersion,
        string minHostVersion,
        string downloadUrl,
        string releaseTag,
        string releaseAssetName,
        string iconUrl,
        string readmeUrl,
        string homepageUrl,
        string repositoryUrl,
        string[] tags,
        AirAppCatalogSharedContractInfo[] sharedContracts,
        DateTimeOffset publishedAt,
        DateTimeOffset updatedAt)
    {
        return new AirAppCatalogItemInfo(
            new AirAppCatalogManifestInfo(
                id,
                name,
                description,
                author,
                version,
                apiVersion,
                string.Empty,
                sharedContracts),
            new AirAppCatalogCompatibilityInfo(
                minHostVersion,
                apiVersion),
            new AirAppCatalogRepositoryInfo(
                iconUrl,
                homepageUrl,
                readmeUrl,
                homepageUrl,
                repositoryUrl,
                tags,
                string.Empty),
            new AirAppCatalogPublicationInfo(
                releaseTag,
                releaseAssetName,
                publishedAt,
                updatedAt,
                0,
                string.Empty,
                null),
            string.IsNullOrWhiteSpace(downloadUrl)
                ? []
                : [
                    new AirAppPackageSourceInfo(
                        string.IsNullOrWhiteSpace(releaseTag)
                            ? LanMountainDesktop.Services.Settings.AirAppPackageSourceKind.RawFallback
                            : LanMountainDesktop.Services.Settings.AirAppPackageSourceKind.ReleaseAsset,
                        downloadUrl,
                        string.Empty,
                        0)
                ],
            []);
    }
}
