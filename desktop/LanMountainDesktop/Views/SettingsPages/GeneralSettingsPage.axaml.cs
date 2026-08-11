using LanMountainDesktop.PluginSdk;
using LanMountainDesktop.Services.Settings;
using LanMountainDesktop.ViewModels;

namespace LanMountainDesktop.Views.SettingsPages;

[SettingsPageInfo(
    "general",
    "General",
    SettingsPageCategory.General,
    IconKey = "Settings",
    SortOrder = 0,
    TitleLocalizationKey = "settings.general.title",
    DescriptionLocalizationKey = "settings.general.description")]
public partial class GeneralSettingsPage : SettingsPageBase
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
