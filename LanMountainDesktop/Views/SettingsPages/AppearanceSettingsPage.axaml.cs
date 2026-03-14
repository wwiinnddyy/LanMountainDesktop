using LanMountainDesktop.PluginSdk;
using LanMountainDesktop.Services.Settings;
using LanMountainDesktop.ViewModels;

namespace LanMountainDesktop.Views.SettingsPages;

[SettingsPageInfo(
    "appearance",
    "Appearance",
    SettingsPageCategory.Appearance,
    IconKey = "DesignIdeas",
    SortOrder = 10,
    TitleLocalizationKey = "settings.appearance.title",
    DescriptionLocalizationKey = "settings.appearance.description")]
public partial class AppearanceSettingsPage : SettingsPageBase
{
    public AppearanceSettingsPage()
        : this(new AppearanceSettingsPageViewModel(HostSettingsFacadeProvider.GetOrCreate()))
    {
    }

    public AppearanceSettingsPage(AppearanceSettingsPageViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = ViewModel;
        InitializeComponent();
    }

    public AppearanceSettingsPageViewModel ViewModel { get; }
}
