using Avalonia.Controls;
using Avalonia.Platform.Storage;
using LanMountainDesktop.PluginSdk;
using LanMountainDesktop.Services.Settings;
using LanMountainDesktop.ViewModels;
using System.Linq;

namespace LanMountainDesktop.Views.SettingsPages;

[SettingsPageInfo(
    "wallpaper",
    "Wallpaper",
    SettingsPageCategory.Appearance,
    IconKey = "Image",
    SortOrder = 15,
    TitleLocalizationKey = "settings.wallpaper.title",
    DescriptionLocalizationKey = "settings.wallpaper.description")]
public partial class WallpaperSettingsPage : SettingsPageBase
{
    public WallpaperSettingsPage()
        : this(new WallpaperSettingsPageViewModel(HostSettingsFacadeProvider.GetOrCreate()))
    {
    }

    public WallpaperSettingsPage(WallpaperSettingsPageViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = ViewModel;
        InitializeComponent();
    }

    public WallpaperSettingsPageViewModel ViewModel { get; }

    private async void OnBrowseWallpaperClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storageProvider)
        {
            return;
        }

        var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = ViewModel.FilePickerTitle,
            AllowMultiple = false
        });

        var file = files.FirstOrDefault();
        var localPath = file?.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(localPath))
        {
            await ViewModel.ImportWallpaperAsync(localPath);
        }
    }
}
