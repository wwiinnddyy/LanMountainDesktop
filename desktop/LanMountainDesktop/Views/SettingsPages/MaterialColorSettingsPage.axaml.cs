using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using LanMountainDesktop.AirAppSdk;
using LanMountainDesktop.Services;
using LanMountainDesktop.Services.Settings;
using LanMountainDesktop.ViewModels;

namespace LanMountainDesktop.Views.SettingsPages;

[AirAppSettingsPageInfo(
    "material-color",
    "Material & Color",
    AirAppSettingsPageCategory.Appearance,
    IconKey = "Color",
    SortOrder = 8,
    TitleLocalizationKey = "settings.material_color.title",
    DescriptionLocalizationKey = "settings.material_color.description")]
public partial class MaterialColorSettingsPage : AirAppSettingsPageBase
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
        ViewModel.RestartRequested += OnRestartRequested;
        DataContext = ViewModel;
        InitializeComponent();
        // Control.OnDetachedFromVisualTreeCore 在这个 Avalonia 版本里是 sealed，只能挂实例事件。
        DetachedFromVisualTree += (_, _) => ViewModel.Detach();
    }

    public MaterialColorSettingsPageViewModel ViewModel { get; }

    private void OnRestartRequested(string reason)
    {
        RequestRestart(reason);
    }

    private void OnWallpaperSeedCandidateClick(object? sender, RoutedEventArgs e)
    {
        _ = e;

        if (sender is Button { DataContext: ThemeSeedCandidateOption option })
        {
            ViewModel.SelectWallpaperSeed(option.Value);
        }
    }
}
