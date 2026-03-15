using System;
using LanMountainDesktop.PluginSdk;
using LanMountainDesktop.Services;
using LanMountainDesktop.Services.Settings;
using LanMountainDesktop.ViewModels;
using Avalonia.Controls;
using Avalonia.Interactivity;

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
        : this(new AppearanceSettingsPageViewModel(
            HostSettingsFacadeProvider.GetOrCreate(),
            HostAppearanceThemeProvider.GetOrCreate()))
    {
    }

    public AppearanceSettingsPage(AppearanceSettingsPageViewModel viewModel)
    {
        ViewModel = viewModel;
        ViewModel.RestartRequested += OnRestartRequested;
        DataContext = ViewModel;
        InitializeComponent();
    }

    public AppearanceSettingsPageViewModel ViewModel { get; }

    private void OnRestartRequested(string reason)
    {
        RequestRestart(reason);
    }

    private void OnApplyCustomSeedClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        ViewModel.ApplyCustomSeedCommand.Execute(null);
        CustomSeedButton?.Flyout?.Hide();
    }

    private void OnCustomSeedFlyoutClosed(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        ViewModel.CancelCustomSeedPreview();
    }

    private void OnWallpaperSeedCandidateClick(object? sender, RoutedEventArgs e)
    {
        _ = e;

        if (sender is Button { DataContext: ThemeSeedCandidateOption option })
        {
            ViewModel.SelectWallpaperSeed(option.Value);
        }

        WallpaperSeedButton?.Flyout?.Hide();
    }
}
