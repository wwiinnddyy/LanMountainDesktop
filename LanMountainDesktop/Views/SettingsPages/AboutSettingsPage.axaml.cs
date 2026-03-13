using LanMountainDesktop.PluginSdk;
using LanMountainDesktop.Services.Settings;
using LanMountainDesktop.ViewModels;

namespace LanMountainDesktop.Views.SettingsPages;

[SettingsPageInfo(
    "about",
    "About",
    SettingsPageCategory.About,
    IconKey = "Info",
    SortOrder = 40,
    TitleLocalizationKey = "settings.about.title",
    DescriptionLocalizationKey = "settings.about.description")]
public partial class AboutSettingsPage : SettingsPageBase
{
    public AboutSettingsPage()
        : this(new AboutSettingsPageViewModel(HostSettingsFacadeProvider.GetOrCreate()))
    {
    }

    public AboutSettingsPage(AboutSettingsPageViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = ViewModel;
        InitializeComponent();
    }

    public AboutSettingsPageViewModel ViewModel { get; }
}
