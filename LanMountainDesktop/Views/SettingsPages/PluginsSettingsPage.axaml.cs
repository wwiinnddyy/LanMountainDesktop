using Avalonia.Controls;
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
        : this(Design.IsDesignMode ? CreateDesignTimeViewModel() : new PluginsSettingsPageViewModel(HostSettingsFacadeProvider.GetOrCreate()))
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
        if (Design.IsDesignMode)
        {
            return;
        }

        await ViewModel.InitializeAsync();
    }

    private void OnRestartRequested()
    {
        RequestRestart(ViewModel.RestartRequiredMessage);
    }

    private static PluginsSettingsPageViewModel CreateDesignTimeViewModel()
    {
        var viewModel = new PluginsSettingsPageViewModel(HostSettingsFacadeProvider.GetOrCreate());
        viewModel.InstalledPlugins.Add(new InstalledPluginItemViewModel(new InstalledPluginInfo(
            new PluginManifest(
                "calendar-plus",
                "Calendar Plus",
                "CalendarPlus.dll",
                "Adds a compact agenda widget and richer date cards.",
                "LanMountain Labs",
                "1.4.0"),
            true,
            true,
            true,
            null)));
        viewModel.InstalledPlugins.Add(new InstalledPluginItemViewModel(new InstalledPluginInfo(
            new PluginManifest(
                "focus-mode",
                "Focus Mode",
                "FocusMode.dll",
                "Provides a distraction-free overlay and quick toggles.",
                "Studio North",
                "0.9.2"),
            true,
            false,
            true,
            null)));
        viewModel.InstalledPlugins.Add(new InstalledPluginItemViewModel(new InstalledPluginInfo(
            new PluginManifest(
                "notes-dock",
                "Notes Dock",
                "NotesDock.dll",
                "Pins short markdown notes directly on the desktop.",
                "Aster Team",
                "2.1.0"),
            false,
            false,
            true,
            null)));
        viewModel.StatusMessage = "Loaded 3 mocked plugins for Avalonia design mode.";
        return viewModel;
    }
}
