using LanMountainDesktop.AirAppSdk;
using LanMountainDesktop.Services.Settings;
using LanMountainDesktop.ViewModels;

namespace LanMountainDesktop.Views.SettingsPages;

[AirAppSettingsPageInfo(
    "launcher",
    "App Launcher",
    AirAppSettingsPageCategory.Components,
    IconKey = "AppsListDetail",
    SortOrder = 10,
    Scope = AirAppSettingsScope.Launcher,
    TitleLocalizationKey = "settings.launcher.title",
    DescriptionLocalizationKey = "settings.launcher.description")]
public partial class LauncherSettingsPage : AirAppSettingsPageBase
{
    public LauncherSettingsPage()
        : this(new LauncherSettingsPageViewModel(HostSettingsFacadeProvider.GetOrCreate()))
    {
    }

    public LauncherSettingsPage(LauncherSettingsPageViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = ViewModel;
        InitializeComponent();
    }

    public LauncherSettingsPageViewModel ViewModel { get; }
}
