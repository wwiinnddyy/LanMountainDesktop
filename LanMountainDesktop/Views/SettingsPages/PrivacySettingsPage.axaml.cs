using LanMountainDesktop.PluginSdk;
using LanMountainDesktop.Services.Settings;
using LanMountainDesktop.ViewModels;

namespace LanMountainDesktop.Views.SettingsPages;

[SettingsPageInfo(
    "privacy",
    "Privacy",
    SettingsPageCategory.About,
    IconKey = "Shield",
    SortOrder = 34,
    TitleLocalizationKey = "settings.privacy.title",
    DescriptionLocalizationKey = "settings.privacy.description")]
public partial class PrivacySettingsPage : SettingsPageBase
{
    public PrivacySettingsPage()
        : this(new PrivacySettingsPageViewModel(HostSettingsFacadeProvider.GetOrCreate()))
    {
    }

    public PrivacySettingsPage(PrivacySettingsPageViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = ViewModel;
        InitializeComponent();
    }

    public PrivacySettingsPageViewModel ViewModel { get; }
}
