using LanMountainDesktop.AirAppSdk;
using LanMountainDesktop.Services.Settings;
using LanMountainDesktop.ViewModels;

namespace LanMountainDesktop.Views.SettingsPages;

[AirAppSettingsPageInfo(
    "dev",
    "Developer",
    AirAppSettingsPageCategory.Dev,
    IconKey = "DeveloperBoard",
    SortOrder = 0,
    TitleLocalizationKey = "settings.dev.title",
    DescriptionLocalizationKey = "settings.dev.description")]
public partial class DevSettingsPage : AirAppSettingsPageBase
{
    public DevSettingsPage()
        : this(new DevSettingsPageViewModel(HostSettingsFacadeProvider.GetOrCreate()))
    {
    }

    public DevSettingsPage(DevSettingsPageViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = ViewModel;
        InitializeComponent();
    }

    public DevSettingsPageViewModel ViewModel { get; }
}
