using LanMountainDesktop.AirAppSdk;
using LanMountainDesktop.Services.Settings;
using LanMountainDesktop.ViewModels;

namespace LanMountainDesktop.Views.SettingsPages;

[AirAppSettingsPageInfo(
    "general",
    "General",
    AirAppSettingsPageCategory.General,
    IconKey = "Settings",
    SortOrder = 0,
    TitleLocalizationKey = "settings.general.title",
    DescriptionLocalizationKey = "settings.general.description")]
public partial class GeneralSettingsPage : AirAppSettingsPageBase
{
    public GeneralSettingsPage()
        : this(new GeneralSettingsPageViewModel(HostSettingsFacadeProvider.GetOrCreate()))
    {
    }

    public GeneralSettingsPage(GeneralSettingsPageViewModel viewModel)
    {
        ViewModel = viewModel;
        ViewModel.RestartRequested += OnRestartRequested;
        DataContext = ViewModel;
        InitializeComponent();
    }

    public GeneralSettingsPageViewModel ViewModel { get; }

    private void OnRestartRequested()
    {
        RequestRestart(ViewModel.RenderModeRestartMessage);
    }
}
