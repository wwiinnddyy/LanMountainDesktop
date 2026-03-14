using System;
using Avalonia;
using Avalonia.Controls;
using LanMountainDesktop.PluginSdk;
using LanMountainDesktop.Services.Settings;
using LanMountainDesktop.ViewModels;

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
}
