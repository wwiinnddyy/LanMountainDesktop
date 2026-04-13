using LanMountainDesktop.PluginSdk;
using LanMountainDesktop.Services.Settings;
using LanMountainDesktop.ViewModels;

namespace LanMountainDesktop.Views.SettingsPages;

[SettingsPageInfo(
    "dev",
    "开发者",
    SettingsPageCategory.Dev,
    IconKey = "DeveloperBoard",
    SortOrder = 0,
    TitleLocalizationKey = "settings.dev.title",
    DescriptionLocalizationKey = "settings.dev.description")]
public partial class DevSettingsPage : SettingsPageBase
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
