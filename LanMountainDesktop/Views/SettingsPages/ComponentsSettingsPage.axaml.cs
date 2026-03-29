using LanMountainDesktop.PluginSdk;
using LanMountainDesktop.Services.Settings;
using LanMountainDesktop.ViewModels;

namespace LanMountainDesktop.Views.SettingsPages;

[SettingsPageInfo(
    "components",
    "Components",
    SettingsPageCategory.Components,
    IconKey = "AppFolder",
    SortOrder = 20,
    TitleLocalizationKey = "settings.components.title",
    DescriptionLocalizationKey = "settings.components.description")]
public partial class ComponentsSettingsPage : SettingsPageBase
{
    public ComponentsSettingsPage()
        : this(new ComponentsSettingsPageViewModel(HostSettingsFacadeProvider.GetOrCreate()))
    {
    }

    public ComponentsSettingsPage(ComponentsSettingsPageViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = ViewModel;
        InitializeComponent();
    }

    public ComponentsSettingsPageViewModel ViewModel { get; }
}
