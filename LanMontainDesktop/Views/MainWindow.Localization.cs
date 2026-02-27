using System;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace LanMontainDesktop.Views;

public partial class MainWindow
{
    private void InitializeLocalization(string? languageCode)
    {
        _languageCode = _localizationService.NormalizeLanguageCode(languageCode);

        if (LanguageComboBox is null)
        {
            return;
        }

        _suppressLanguageSelectionEvents = true;
        LanguageComboBox.SelectedIndex = string.Equals(_languageCode, "en-US", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        _suppressLanguageSelectionEvents = false;
    }

    private string L(string key, string fallback)
    {
        return _localizationService.GetString(_languageCode, key, fallback);
    }

    private string Lf(string key, string fallback, params object[] args)
    {
        var template = L(key, fallback);
        return string.Format(template, args);
    }

    private string GetLanguageDisplayName(string languageCode)
    {
        return string.Equals(languageCode, "en-US", StringComparison.OrdinalIgnoreCase)
            ? L("settings.region.language_en", "English")
            : L("settings.region.language_zh", "中文");
    }

    private string GetLocalizedPlacementDisplayName(WallpaperPlacement placement)
    {
        return placement switch
        {
            WallpaperPlacement.Fill => L("placement.fill", "Fill"),
            WallpaperPlacement.Fit => L("placement.fit", "Fit"),
            WallpaperPlacement.Stretch => L("placement.stretch", "Stretch"),
            WallpaperPlacement.Center => L("placement.center", "Center"),
            WallpaperPlacement.Tile => L("placement.tile", "Tile"),
            _ => L("placement.fill", "Fill")
        };
    }

    private void ApplyLocalization()
    {
        Title = L("app.title", "LanMontainDesktop");

        BackToWindowsTextBlock.Text = L("button.back_to_windows", "Back to Windows");
        WallpaperPreviewBackButtonTextBlock.Text = L("button.back_to_windows", "Back to Windows");
        ToolTip.SetTip(BackToWindowsButton, L("tooltip.back_to_windows", "Back to Windows"));

        SettingsTitleTextBlock.Text = L("settings.title", "Settings");
        SettingsNavHeaderTextBlock.Text = L("settings.nav_header", "Settings");
        SettingsNavWallpaperItem.Content = L("settings.nav.wallpaper", "Wallpaper");
        SettingsNavGridItem.Content = L("settings.nav.grid", "Grid");
        SettingsNavColorItem.Content = L("settings.nav.color", "Color");
        SettingsNavStatusBarItem.Content = L("settings.nav.status_bar", "Status Bar");
        SettingsNavRegionItem.Content = L("settings.nav.region", "Region");

        WallpaperPanelTitleTextBlock.Text = L("settings.wallpaper.title", "Wallpaper");
        WallpaperPanelDescriptionTextBlock.Text = L("settings.wallpaper.description", "Pick wallpaper.");
        WallpaperCurrentLabelTextBlock.Text = L("settings.wallpaper.current_label", "Current Wallpaper");
        WallpaperPlacementLabelTextBlock.Text = L("settings.wallpaper.placement_label", "Placement");
        PickWallpaperButton.Content = L("settings.wallpaper.pick_button", "Browse Files");
        ClearWallpaperButton.Content = L("settings.wallpaper.clear_button", "Reset");

        GridPanelTitleTextBlock.Text = L("settings.grid.title", "Grid Layout");
        GridPanelDescriptionTextBlock.Text = L("settings.grid.description", "Each component should occupy at least 1x1.");
        GridShortSideLabelTextBlock.Text = L("settings.grid.short_side_label", "Short Side Cells");
        ApplyGridButton.Content = L("settings.grid.apply_button", "Apply");

        ColorPanelTitleTextBlock.Text = L("settings.color.title", "Color");
        ColorPanelDescriptionTextBlock.Text = L("settings.color.description", "Theme and accent settings.");
        DayNightModeLabelTextBlock.Text = L("settings.color.day_night_label", "Day/Night");
        NightModeToggleSwitch.OnContent = L("settings.color.day_night_on", "Night");
        NightModeToggleSwitch.OffContent = L("settings.color.day_night_off", "Day");
        RecommendedColorsLabelTextBlock.Text = L("settings.color.recommended_label", "Recommended Colors");
        SystemMonetColorsLabelTextBlock.Text = L("settings.color.system_monet_label", "System Monet Colors");
        RefreshMonetColorsButton.Content = L("settings.color.refresh_button", "Refresh");

        StatusBarPanelTitleTextBlock.Text = L("settings.status_bar.title", "Status Bar");
        StatusBarPanelDescriptionTextBlock.Text = L("settings.status_bar.description", "Status bar components.");
        StatusBarClockSettingsExpander.Header = L("settings.status_bar.clock_header", "Clock");
        StatusBarClockDescriptionTextBlock.Text = L("settings.status_bar.clock_description", "Display clock in top status bar.");

        RegionPanelTitleTextBlock.Text = L("settings.region.title", "Region");
        RegionPanelDescriptionTextBlock.Text = L("settings.region.description", "Select language.");
        LanguageSettingsExpander.Header = L("settings.region.language_header", "Language");
        LanguageLabelTextBlock.Text = L("settings.region.language_label", "Language");
        LanguageChineseItem.Content = L("settings.region.language_zh", "中文");
        LanguageEnglishItem.Content = L("settings.region.language_en", "English");

        if (WallpaperPlacementComboBox?.ItemCount >= 5)
        {
            if (WallpaperPlacementComboBox.Items[0] is ComboBoxItem fillItem) fillItem.Content = L("placement.fill", "Fill");
            if (WallpaperPlacementComboBox.Items[1] is ComboBoxItem fitItem) fitItem.Content = L("placement.fit", "Fit");
            if (WallpaperPlacementComboBox.Items[2] is ComboBoxItem stretchItem) stretchItem.Content = L("placement.stretch", "Stretch");
            if (WallpaperPlacementComboBox.Items[3] is ComboBoxItem centerItem) centerItem.Content = L("placement.center", "Center");
            if (WallpaperPlacementComboBox.Items[4] is ComboBoxItem tileItem) tileItem.Content = L("placement.tile", "Tile");
        }

        ThemeModeStatusTextBlock.Text = _isNightMode
            ? L("settings.color.mode_night", "Night mode enabled")
            : L("settings.color.mode_day", "Day mode enabled");

        GridInfoTextBlock.Text = Lf(
            "settings.grid.info_format",
            "Grid: {0} cols x {1} rows | cell {2:F1}px (1:1)",
            DesktopGrid.ColumnDefinitions.Count,
            DesktopGrid.RowDefinitions.Count,
            DesktopGrid.RowDefinitions.Count > 0 ? DesktopGrid.RowDefinitions[0].Height.Value : 0d);

        UpdateOpenSettingsActionVisualState();
        UpdateWallpaperDisplay();
    }

    private void OnLanguageSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressLanguageSelectionEvents || LanguageComboBox?.SelectedItem is not ComboBoxItem item)
        {
            return;
        }

        var selectedLanguage = item.Tag as string;
        _languageCode = _localizationService.NormalizeLanguageCode(selectedLanguage);
        ApplyLocalization();
        ThemeColorStatusTextBlock.Text = Lf(
            "settings.region.applied_format",
            "Language switched to: {0}",
            GetLanguageDisplayName(_languageCode));
        PersistSettings();
    }
}
