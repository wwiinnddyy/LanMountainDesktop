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
        : this(CreateDefaultViewModel())
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
}
