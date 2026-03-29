using LanMountainDesktop.PluginSdk;
using LanMountainDesktop.Services.Settings;
using LanMountainDesktop.ViewModels;

namespace LanMountainDesktop.Views.SettingsPages;

[SettingsPageInfo(
    "status-bar",
    "Status Bar",
    SettingsPageCategory.Components,
    IconKey = "MatchAppLayout",
    SortOrder = 15,
    TitleLocalizationKey = "settings.status_bar.title",
    DescriptionLocalizationKey = "settings.status_bar.description")]
public partial class StatusBarSettingsPage : SettingsPageBase
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
