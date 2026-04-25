using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using FluentAvalonia.UI.Controls;
using LanMountainDesktop.Models;
using LanMountainDesktop.PluginSdk;
using LanMountainDesktop.Services;
using LanMountainDesktop.Services.Settings;
using LanMountainDesktop.ViewModels;
using LanMountainDesktop.Views;

namespace LanMountainDesktop.Views.SettingsPages;

[SettingsPageInfo(
    "about",
    "About",
    SettingsPageCategory.About,
    IconKey = "Info",
    SortOrder = 40,
    TitleLocalizationKey = "settings.about.title",
    DescriptionLocalizationKey = "settings.about.description",
    HidePageTitle = true)]
public partial class AboutSettingsPage : SettingsPageBase
{
    private const double HeroAspectRatio = 9d / 16d;
    private const int DevModeActivationClicks = 5;

    private int _heroCardClickCount;
    private DateTime _lastHeroCardClickTime = DateTime.MinValue;

    public AboutSettingsPage()
        : this(new AboutSettingsPageViewModel(HostSettingsFacadeProvider.GetOrCreate()))
    {
    }

    public AboutSettingsPage(AboutSettingsPageViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = ViewModel;
        InitializeComponent();
        if (AboutHeroCard is not null)
        {
            AboutHeroCard.SizeChanged += OnAboutHeroCardSizeChanged;
            UpdateHeroCardHeight(AboutHeroCard.Bounds.Width);
        }
    }

    public AboutSettingsPageViewModel ViewModel { get; }

    private void OnAboutHeroCardSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        _ = sender;
        UpdateHeroCardHeight(e.NewSize.Width);
    }

    private void UpdateHeroCardHeight(double width)
    {
        if (AboutHeroCard is null || width <= 1d)
        {
            return;
        }

        var targetHeight = Math.Round(width * HeroAspectRatio, 2);
        if (Math.Abs(AboutHeroCard.Height - targetHeight) <= 0.5d)
        {
            return;
        }

        AboutHeroCard.Height = targetHeight;
    }

    private void OnAboutHeroCardPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _ = sender;
        _ = e;

        var now = DateTime.UtcNow;
        var elapsed = now - _lastHeroCardClickTime;

        if (elapsed.TotalSeconds > 3)
        {
            _heroCardClickCount = 1;
        }
        else
        {
            _heroCardClickCount++;
        }

        _lastHeroCardClickTime = now;

        var settingsFacade = HostSettingsFacadeProvider.GetOrCreate();
        var snapshot = settingsFacade.Settings.LoadSnapshot<AppSettingsSnapshot>(SettingsScope.App);

        if (snapshot.IsDevModeEnabled)
        {
            if (_heroCardClickCount >= 3)
            {
                _heroCardClickCount = 0;
                Debug.WriteLine("[AboutSettingsPage] Developer mode is already enabled.");
            }

            return;
        }

        var remaining = DevModeActivationClicks - _heroCardClickCount;

        if (remaining <= 0)
        {
            _heroCardClickCount = 0;
            PromptEnableDevMode(settingsFacade);
        }
        else if (remaining <= 2)
        {
            Debug.WriteLine($"[AboutSettingsPage] {remaining} tap(s) remaining before developer mode unlocks.");
        }
    }

    private async void PromptEnableDevMode(ISettingsFacadeService settingsFacade)
    {
        var dialog = new FAContentDialog
        {
            Title = "Enable developer mode",
            Content = "Developer mode exposes experimental settings, diagnostics, and local plugin debugging options.\n\nUse it only when you are actively testing or troubleshooting the desktop host.",
            PrimaryButtonText = "Enable",
            CloseButtonText = "Not now",
            DefaultButton = FAContentDialogButton.Close
        };

        var result = await dialog.ShowAsync();
        if (result != FAContentDialogResult.Primary)
        {
            return;
        }

        var snapshot = settingsFacade.Settings.LoadSnapshot<AppSettingsSnapshot>(SettingsScope.App);
        snapshot.IsDevModeEnabled = true;
        settingsFacade.Settings.SaveSnapshot(
            SettingsScope.App,
            snapshot,
            changedKeys: [nameof(AppSettingsSnapshot.IsDevModeEnabled)]);

        AppLogger.Info("DevMode", "Developer mode enabled via About page activation.");

        if (this.FindAncestorOfType<SettingsWindow>() is { } settingsWindow)
        {
            settingsWindow.RebuildAndNavigateToDevPage();
        }
    }
}
