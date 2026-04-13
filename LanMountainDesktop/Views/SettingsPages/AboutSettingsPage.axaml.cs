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
            Debug.WriteLine($"[AboutSettingsPage] 再点击 {remaining} 次即可启用开发者模式。");
        }
    }

    private async void PromptEnableDevMode(ISettingsFacadeService settingsFacade)
    {
        var dialog = new ContentDialog
        {
            Title = "启用开发者模式",
            Content = "开发者模式提供了插件调试、热重载等高级功能，仅供开发和调试用途。\n\n" +
                      "请注意：开发者不对以非开发用途使用此功能造成的任何后果负责，也不接受以非开发用途使用时产生的 Bug 反馈。\n\n" +
                      "确定要启用开发者模式吗？",
            PrimaryButtonText = "启用",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close
        };

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary)
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
