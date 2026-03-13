using Avalonia.Controls;
using Avalonia.Platform.Storage;
using LanMountainDesktop.PluginSdk;
using LanMountainDesktop.Services.Settings;
using LanMountainDesktop.ViewModels;
using System.Linq;

namespace LanMountainDesktop.Views.SettingsPages;

[SettingsPageInfo(
    "appearance",
    "Appearance",
    SettingsPageCategory.Appearance,
    IconKey = "DesignIdeas",
    SortOrder = 10,
    TitleLocalizationKey = "settings.appearance.title",
    DescriptionLocalizationKey = "settings.appearance.description")]
public partial class AppearanceSettingsPage : SettingsPageBase
{
    public AppearanceSettingsPage()
        : this(new AppearanceSettingsPageViewModel(HostSettingsFacadeProvider.GetOrCreate()))
    {
    }

    public AppearanceSettingsPage(AppearanceSettingsPageViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = ViewModel;
        InitializeComponent();
    }

    public AppearanceSettingsPageViewModel ViewModel { get; }

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
