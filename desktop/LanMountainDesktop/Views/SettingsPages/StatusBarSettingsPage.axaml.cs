using LanMountainDesktop.AirAppSdk;
using LanMountainDesktop.Services.Settings;
using LanMountainDesktop.ViewModels;

namespace LanMountainDesktop.Views.SettingsPages;

[AirAppSettingsPageInfo(
    "status-bar",
    "Status Bar",
    AirAppSettingsPageCategory.Components,
    IconKey = "MatchAppLayout",
    SortOrder = 15,
    TitleLocalizationKey = "settings.status_bar.title",
    DescriptionLocalizationKey = "settings.status_bar.description")]
public partial class StatusBarSettingsPage : AirAppSettingsPageBase
{
    public StatusBarSettingsPage()
        : this(new StatusBarSettingsPageViewModel(HostSettingsFacadeProvider.GetOrCreate()))
    {
    }

    public StatusBarSettingsPage(StatusBarSettingsPageViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = ViewModel;
        InitializeComponent();
    }

    public StatusBarSettingsPageViewModel ViewModel { get; }
}
