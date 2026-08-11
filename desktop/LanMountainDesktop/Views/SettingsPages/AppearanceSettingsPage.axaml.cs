using LanMountainDesktop.AirAppSdk;
using LanMountainDesktop.Services;
using LanMountainDesktop.Services.Settings;
using LanMountainDesktop.ViewModels;

namespace LanMountainDesktop.Views.SettingsPages;

[AirAppSettingsPageInfo(
    "appearance",
    "Appearance",
    AirAppSettingsPageCategory.Appearance,
    IconKey = "DesignIdeas",
    SortOrder = 10,
    TitleLocalizationKey = "settings.appearance.title",
    DescriptionLocalizationKey = "settings.appearance.description")]
public partial class AppearanceSettingsPage : AirAppSettingsPageBase
{
    public AppearanceSettingsPage()
        : this(new AppearanceSettingsPageViewModel(
            HostSettingsFacadeProvider.GetOrCreate()))
    {
    }

    public AppearanceSettingsPage(AppearanceSettingsPageViewModel viewModel)
    {
        ViewModel = viewModel;
        ViewModel.RestartRequested += OnRestartRequested;
        DataContext = ViewModel;
        InitializeComponent();
    }

    public AppearanceSettingsPageViewModel ViewModel { get; }

    private void OnRestartRequested(string reason)
    {
        RequestRestart(reason);
    }
}
