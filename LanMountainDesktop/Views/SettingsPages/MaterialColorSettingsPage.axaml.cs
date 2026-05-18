using Avalonia.Controls;
using Avalonia.Interactivity;
using LanMountainDesktop.PluginSdk;
using LanMountainDesktop.Services;
using LanMountainDesktop.Services.Settings;
using LanMountainDesktop.ViewModels;

namespace LanMountainDesktop.Views.SettingsPages;

[SettingsPageInfo(
    "material-color",
    "Material & Color",
    SettingsPageCategory.Appearance,
    IconKey = "Color",
    SortOrder = 8,
    TitleLocalizationKey = "settings.material_color.title",
    DescriptionLocalizationKey = "settings.material_color.description")]
public partial class MaterialColorSettingsPage : SettingsPageBase
{
    public MaterialColorSettingsPage()
        : this(new MaterialColorSettingsPageViewModel(
            HostSettingsFacadeProvider.GetOrCreate(),
            HostMaterialColorProvider.GetOrCreate()))
    {
    }

    public MaterialColorSettingsPage(MaterialColorSettingsPageViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = ViewModel;
        InitializeComponent();
    }

    public MaterialColorSettingsPageViewModel ViewModel { get; }

    private void OnWallpaperSeedCandidateClick(object? sender, RoutedEventArgs e)
    {
        _ = e;

        if (sender is Button { DataContext: ThemeSeedCandidateOption option })
        {
            ViewModel.SelectWallpaperSeed(option.Value);
        }
    }
}
