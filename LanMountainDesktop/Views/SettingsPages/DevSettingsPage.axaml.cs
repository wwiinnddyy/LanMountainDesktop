using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using FluentAvalonia.UI.Controls;
using LanMountainDesktop.PluginSdk;
using LanMountainDesktop.Services.Settings;
using LanMountainDesktop.ViewModels;

namespace LanMountainDesktop.Views.SettingsPages;

[SettingsPageInfo(
    "dev",
    "Developer",
    SettingsPageCategory.Dev,
    IconKey = "DeveloperBoard",
    SortOrder = 0,
    TitleLocalizationKey = "settings.dev.title",
    DescriptionLocalizationKey = "settings.dev.description")]
public partial class DevSettingsPage : SettingsPageBase
{
    private bool _isReady;
    private bool _syncingToggles;

    public DevSettingsPage()
        : this(new DevSettingsPageViewModel(HostSettingsFacadeProvider.GetOrCreate()))
    {
    }

    public DevSettingsPage(DevSettingsPageViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = ViewModel;
        InitializeComponent();
        _isReady = true;
    }

    public DevSettingsPageViewModel ViewModel { get; }

    private async void OnFusedDesktopToggleChanged(object? sender, RoutedEventArgs e)
    {
        if (!_isReady || _syncingToggles || sender is not ToggleSwitch toggle)
        {
            return;
        }

        var requested = toggle.IsChecked == true;
        if (!requested)
        {
            ViewModel.ApplyFusedDesktopPreference(enabled: false, disableMainWindowDesktopLayer: false);
            SyncTogglesFromViewModel();
            return;
        }

        if (ViewModel.EnableMainWindowDesktopLayer &&
            !await ConfirmDesktopLayerSwitchAsync(ViewModel.DesktopLayerConflictEnableFusedMessage).ConfigureAwait(true))
        {
            SyncTogglesFromViewModel();
            return;
        }

        ViewModel.ApplyFusedDesktopPreference(enabled: true, disableMainWindowDesktopLayer: true);
        SyncTogglesFromViewModel();
    }

    private async void OnMainWindowDesktopLayerToggleChanged(object? sender, RoutedEventArgs e)
    {
        if (!_isReady || _syncingToggles || sender is not ToggleSwitch toggle)
        {
            return;
        }

        var requested = toggle.IsChecked == true;
        if (!requested)
        {
            ViewModel.ApplyMainWindowDesktopLayerPreference(enabled: false, disableFusedDesktop: false);
            SyncTogglesFromViewModel();
            return;
        }

        if (ViewModel.EnableFusedDesktop &&
            !await ConfirmDesktopLayerSwitchAsync(ViewModel.DesktopLayerConflictEnableMainMessage).ConfigureAwait(true))
        {
            SyncTogglesFromViewModel();
            return;
        }

        ViewModel.ApplyMainWindowDesktopLayerPreference(enabled: true, disableFusedDesktop: true);
        SyncTogglesFromViewModel();
    }

    private async Task<bool> ConfirmDesktopLayerSwitchAsync(string message)
    {
        var dialog = new FAContentDialog
        {
            Title = ViewModel.DesktopLayerConflictTitle,
            Content = message,
            PrimaryButtonText = ViewModel.DesktopLayerConflictConfirmText,
            CloseButtonText = ViewModel.DesktopLayerConflictCancelText,
            DefaultButton = FAContentDialogButton.Close
        };

        var owner = this.FindAncestorOfType<Window>();
        var result = owner is not null
            ? await dialog.ShowAsync(owner)
            : await dialog.ShowAsync();
        return result == FAContentDialogResult.Primary;
    }

    private void SyncTogglesFromViewModel()
    {
        _syncingToggles = true;
        try
        {
            FusedDesktopToggle.IsChecked = ViewModel.EnableFusedDesktop;
            MainWindowDesktopLayerToggle.IsChecked = ViewModel.EnableMainWindowDesktopLayer;
        }
        finally
        {
            _syncingToggles = false;
        }
    }
}
