using LanMountainDesktop.PluginSdk;
using LanMountainDesktop.Services.Settings;
using LanMountainDesktop.ViewModels;

namespace LanMountainDesktop.Views.SettingsPages;

[SettingsPageInfo(
    "launcher",
    "App Launcher",
    SettingsPageCategory.Components,
    IconKey = "AppsListDetail",
    SortOrder = 10,
    Scope = SettingsScope.Launcher,
    TitleLocalizationKey = "settings.launcher.title",
    DescriptionLocalizationKey = "settings.launcher.description")]
public partial class LauncherSettingsPage : SettingsPageBase
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
