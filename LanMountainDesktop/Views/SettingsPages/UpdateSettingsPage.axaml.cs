using LanMountainDesktop.PluginSdk;
using LanMountainDesktop.Services.Settings;
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
        : this(new UpdateSettingsPageViewModel(HostSettingsFacadeProvider.GetOrCreate()))
    {
    }

    public UpdateSettingsPage(UpdateSettingsPageViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = ViewModel;
        InitializeComponent();
    }

    public UpdateSettingsPageViewModel ViewModel { get; }
}
