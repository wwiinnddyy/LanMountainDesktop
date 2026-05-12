using LanMountainDesktop.PluginSdk;
using LanMountainDesktop.Services.Settings;
using LanMountainDesktop.Services.Update;
using LanMountainDesktop.ViewModels;

namespace LanMountainDesktop.Views.SettingsPages;

[SettingsPageInfo(
    "update",
    "Update",
    SettingsPageCategory.About,
    IconKey = "ArrowSync",
    SortOrder = 35,
    TitleLocalizationKey = "settings.update.title",
    DescriptionLocalizationKey = "settings.update.description")]
public partial class UpdateSettingsPage : SettingsPageBase
{
    public UpdateSettingsPage()
        : this(new UpdateSettingsViewModel(
            HostUpdateOrchestratorProvider.GetOrCreate(),
            HostSettingsFacadeProvider.GetOrCreate()))
    {
    }

    public UpdateSettingsPage(UpdateSettingsViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = ViewModel;
        InitializeComponent();
    }

    public UpdateSettingsViewModel ViewModel { get; }
}
