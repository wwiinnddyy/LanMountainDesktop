using Avalonia.Controls;
using LanMountainDesktop.AirAppSdk;
using LanMountainDesktop.Services.Settings;
using LanMountainDesktop.ViewModels;

namespace LanMountainDesktop.Views.SettingsPages;

[AirAppSettingsPageInfo(
    "plugins",
    "AirApps",
    AirAppSettingsPageCategory.AirApps,
    IconKey = "PuzzlePiece",
    SortOrder = 30,
    TitleLocalizationKey = "settings.plugins.title",
    DescriptionLocalizationKey = "settings.plugins.description")]
public partial class AirAppsSettingsPage : AirAppSettingsPageBase
{
    public AirAppsSettingsPage()
        : this(Design.IsDesignMode ? CreateDesignTimeViewModel() : new AirAppsSettingsPageViewModel(HostSettingsFacadeProvider.GetOrCreate()))
    {
    }

    public AirAppsSettingsPage(AirAppsSettingsPageViewModel viewModel)
    {
        ViewModel = viewModel;
        ViewModel.RestartRequested += OnRestartRequested;
        DataContext = ViewModel;
        InitializeComponent();
    }

    public AirAppsSettingsPageViewModel ViewModel { get; }

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

    private static AirAppsSettingsPageViewModel CreateDesignTimeViewModel()
    {
        var viewModel = new AirAppsSettingsPageViewModel(HostSettingsFacadeProvider.GetOrCreate());
        viewModel.InstalledAirApps.Add(new InstalledAirAppItemViewModel(new AirAppInstalledInfo(
            new AirAppManifest(
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
        viewModel.InstalledAirApps.Add(new InstalledAirAppItemViewModel(new AirAppInstalledInfo(
            new AirAppManifest(
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
        viewModel.InstalledAirApps.Add(new InstalledAirAppItemViewModel(new AirAppInstalledInfo(
            new AirAppManifest(
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
