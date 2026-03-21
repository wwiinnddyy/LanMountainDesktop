using System;
using Avalonia.Controls;
using LanMountainDesktop.PluginSdk;
using LanMountainDesktop.Services;
using LanMountainDesktop.Services.PluginMarket;
using LanMountainDesktop.Services.Settings;
using LanMountainDesktop.ViewModels;

namespace LanMountainDesktop.Views.SettingsPages;

[SettingsPageInfo(
    "plugin-market",
    "Plugin Market",
    SettingsPageCategory.PluginMarket,
    IconKey = "ShoppingBag",
    SortOrder = 35,
    TitleLocalizationKey = "settings.plugin_market.title",
    DescriptionLocalizationKey = "settings.plugin_market.subtitle")]
public partial class PluginMarketSettingsPage : SettingsPageBase
{
    public PluginMarketSettingsPage()
        : this(Design.IsDesignMode ? CreateDesignTimeViewModel() : CreateDefaultViewModel())
    {
    }

    public PluginMarketSettingsPage(PluginMarketSettingsPageViewModel viewModel)
    {
        ViewModel = viewModel;
        ViewModel.RestartRequested += OnRestartRequested;
        ViewModel.DetailsRequested += OnDetailsRequested;
        DataContext = ViewModel;
        InitializeComponent();
    }

    public PluginMarketSettingsPageViewModel ViewModel { get; }

    public override async void OnNavigatedTo(object? parameter)
    {
        if (Design.IsDesignMode)
        {
            return;
        }

        await ViewModel.InitializeAsync();
    }

    private static PluginMarketSettingsPageViewModel CreateDefaultViewModel()
    {
        var settingsFacade = HostSettingsFacadeProvider.GetOrCreate();
        var localizationService = new LocalizationService();
        return new PluginMarketSettingsPageViewModel(
            settingsFacade,
            localizationService,
            new AirAppMarketIconService(),
            new AirAppMarketReadmeService());
    }

    private static PluginMarketSettingsPageViewModel CreateDesignTimeViewModel()
    {
        var settingsFacade = HostSettingsFacadeProvider.GetOrCreate();
        var localizationService = new LocalizationService();
        var viewModel = new PluginMarketSettingsPageViewModel(
            settingsFacade,
            localizationService,
            new AirAppMarketIconService(),
            new AirAppMarketReadmeService());

        var previewHostVersion = new Version(1, 2, 0);
        var items = new[]
        {
            CreateMarketItem(
                new PluginMarketPluginInfo(
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
                installedPlugin: null,
                previewHostVersion),
            CreateMarketItem(
                new PluginMarketPluginInfo(
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
                new InstalledPluginInfo(
                    new PluginManifest(
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
            CreateMarketItem(
                new PluginMarketPluginInfo(
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
                installedPlugin: null,
                previewHostVersion)
        };

        foreach (var item in items)
        {
            viewModel.MarketPlugins.Add(item);
            viewModel.FilteredPlugins.Add(item);
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

    private async void OnDetailsRequested(PluginMarketItemViewModel item)
    {
        var detailViewModel = ViewModel.CreateDetailViewModel(item);
        var drawer = new PluginMarketDetailDrawer(detailViewModel);
        OpenDrawer(drawer, detailViewModel.DrawerTitle);
        await detailViewModel.InitializeAsync();
    }

    private static PluginMarketItemViewModel CreateMarketItem(
        PluginMarketPluginInfo plugin,
        LocalizationService localizationService,
        InstalledPluginInfo? installedPlugin,
        Version hostVersion)
    {
        var languageCode = localizationService.NormalizeLanguageCode(
            HostSettingsFacadeProvider.GetOrCreate().Region.Get().LanguageCode);
        var item = new PluginMarketItemViewModel(plugin, localizationService, languageCode);
        item.ApplyInstallState(installedPlugin, hostVersion);
        return item;
    }
}
