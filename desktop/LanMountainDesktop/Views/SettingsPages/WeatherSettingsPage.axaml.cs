using Avalonia.Controls;
using LanMountainDesktop.PluginSdk;
using LanMountainDesktop.Services;
using LanMountainDesktop.Services.Settings;
using LanMountainDesktop.ViewModels;

namespace LanMountainDesktop.Views.SettingsPages;

[SettingsPageInfo(
    "weather",
    "Weather",
    SettingsPageCategory.Appearance,
    IconKey = "WeatherMoon",
    SortOrder = 18,
    TitleLocalizationKey = "settings.weather.title",
    DescriptionLocalizationKey = "settings.weather.description")]
public partial class WeatherSettingsPage : SettingsPageBase
{
    public WeatherSettingsPage()
        : this(Design.IsDesignMode ? CreateDesignTimeViewModel() : CreateDefaultViewModel())
    {
    }

    public WeatherSettingsPage(WeatherSettingsPageViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = ViewModel;
        InitializeComponent();
    }

    public WeatherSettingsPageViewModel ViewModel { get; }

    private static WeatherSettingsPageViewModel CreateDefaultViewModel(bool enableStartupPreviewRefresh = true)
    {
        var settingsFacade = HostSettingsFacadeProvider.GetOrCreate();
        var localizationService = new LocalizationService();
        var locationService = HostLocationServiceProvider.GetOrCreate();
        var weatherLocationRefreshService = new WeatherLocationRefreshService(
            settingsFacade,
            locationService,
            localizationService);
        return new WeatherSettingsPageViewModel(
            settingsFacade,
            localizationService,
            locationService,
            weatherLocationRefreshService,
            enableStartupPreviewRefresh);
    }

    private static WeatherSettingsPageViewModel CreateDesignTimeViewModel()
    {
        var viewModel = CreateDefaultViewModel(enableStartupPreviewRefresh: false);
        viewModel.ApplyDesignTimePreview();
        return viewModel;
    }
}
