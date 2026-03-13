using LanMountainDesktop.PluginSdk;
using LanMountainDesktop.Services.Settings;
using LanMountainDesktop.ViewModels;

namespace LanMountainDesktop.Views.SettingsPages;

[SettingsPageInfo(
    "plugins",
    "Plugins",
    SettingsPageCategory.Plugins,
    IconKey = "PuzzlePiece",
    SortOrder = 30,
    TitleLocalizationKey = "settings.plugins.title",
    DescriptionLocalizationKey = "settings.plugins.description")]
public partial class PluginsSettingsPage : SettingsPageBase
{
    public PluginsSettingsPage()
        : this(new PluginsSettingsPageViewModel(HostSettingsFacadeProvider.GetOrCreate()))
    {
    }

    public PluginsSettingsPage(PluginsSettingsPageViewModel viewModel)
    {
        ViewModel = viewModel;
        ViewModel.RestartRequested += OnRestartRequested;
        DataContext = ViewModel;
        InitializeComponent();
    }

    public PluginsSettingsPageViewModel ViewModel { get; }

    public override async void OnNavigatedTo(object? parameter)
    {
        await ViewModel.InitializeAsync();
    }

    private void OnRestartRequested()
    {
        RequestRestart(ViewModel.RestartRequiredMessage);
    }
}
