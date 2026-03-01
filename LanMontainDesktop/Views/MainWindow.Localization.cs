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
            : L("settings.region.language_zh", "Chinese");
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

        OpenComponentLibraryTextBlock.Text = L("button.component_library", "Edit Desktop");
        WallpaperPreviewComponentLibraryTextBlock.Text = L("button.component_library", "Edit Desktop");
        GridPreviewComponentLibraryTextBlock.Text = L("button.component_library", "Edit Desktop");
        ToolTip.SetTip(OpenComponentLibraryButton, L("tooltip.component_library", "Edit Desktop"));
        ComponentLibraryTitleTextBlock.Text = L("component_library.title", "Edit Desktop");
        ToolTip.SetTip(CloseComponentLibraryButton, L("common.close", "Close"));
        ComponentLibraryEmptyTextBlock.Text = L(
            "component_library.empty",
            "Swipe to pick a category, tap to open, then drag a widget onto the desktop.");

        LauncherTitleTextBlock.Text = L("launcher.title", "应用启动台");
        LauncherSubtitleTextBlock.Text = L("launcher.subtitle", "按 Windows 开始菜单结构显示所有应用与文件夹");
        ToolTip.SetTip(LauncherFolderBackButton, L("common.back", "返回"));
        ToolTip.SetTip(LauncherFolderCloseButton, L("common.close", "关闭"));

        SettingsNavHeaderTextBlock.Text = L("settings.nav_header", "Settings");
        SettingsNavWallpaperTextBlock.Text = L("settings.nav.wallpaper", "Wallpaper");
        SettingsNavGridTextBlock.Text = L("settings.nav.grid", "Grid");
        SettingsNavColorTextBlock.Text = L("settings.nav.color", "Color");
        SettingsNavStatusBarTextBlock.Text = L("settings.nav.status_bar", "Status Bar");
        SettingsNavRegionTextBlock.Text = L("settings.nav.region", "Region");

        WallpaperPanelTitleTextBlock.Text = L("settings.wallpaper.title", "个性化我们的背景");
        WallpaperPlacementSettingsExpander.Header = L("settings.wallpaper.placement_label", "选择契合度");
        WallpaperPlacementSettingsExpander.Description = L("settings.wallpaper.placement_desc", "调整图像在桌面上的填充方式。");
        PickWallpaperButton.Content = L("settings.wallpaper.pick_button", "浏览照片");
        ClearWallpaperButton.Content = L("settings.wallpaper.clear_button", "重置");

        GridPanelTitleTextBlock.Text = L("settings.grid.title", "Grid Layout");

        ColorPanelTitleTextBlock.Text = L("settings.color.title", "Color");
        ThemeModeSettingsExpander.Header = L("settings.color.day_night_label", "Day/Night");
        NightModeToggleSwitch.OnContent = L("settings.color.day_night_on", "Night");
        NightModeToggleSwitch.OffContent = L("settings.color.day_night_off", "Day");
        RecommendedColorsLabelTextBlock.Text = L("settings.color.recommended_label", "Recommended Colors");
        SystemMonetColorsLabelTextBlock.Text = L("settings.color.system_monet_label", "System Monet Colors");
        RefreshMonetColorsButton.Content = L("settings.color.refresh_button", "Refresh");

        StatusBarPanelTitleTextBlock.Text = L("settings.status_bar.title", "Status Bar");
        StatusBarClockSettingsExpander.Header = L("settings.status_bar.clock_header", "Clock");

        RegionPanelTitleTextBlock.Text = L("settings.region.title", "Region");
        LanguageSettingsExpander.Header = L("settings.region.language_header", "Language");
        LanguageChineseItem.Content = L("settings.region.language_zh", "Chinese");
        LanguageEnglishItem.Content = L("settings.region.language_en", "English");

        if (WallpaperPlacementComboBox?.ItemCount >= 5)
        {
            if (WallpaperPlacementComboBox.Items[0] is ComboBoxItem fillItem) fillItem.Content = L("placement.fill", "Fill");
            if (WallpaperPlacementComboBox.Items[1] is ComboBoxItem fitItem) fitItem.Content = L("placement.fit", "Fit");
            if (WallpaperPlacementComboBox.Items[2] is ComboBoxItem stretchItem) stretchItem.Content = L("placement.stretch", "Stretch");
            if (WallpaperPlacementComboBox.Items[3] is ComboBoxItem centerItem) centerItem.Content = L("placement.center", "Center");
            if (WallpaperPlacementComboBox.Items[4] is ComboBoxItem tileItem) tileItem.Content = L("placement.tile", "Tile");
        }


        GridInfoTextBlock.Text = Lf(
            "settings.grid.info_format",
            "Grid: {0} cols x {1} rows | cell {2:F1}px (1:1)",
            DesktopGrid.ColumnDefinitions.Count,
            DesktopGrid.RowDefinitions.Count,
            DesktopGrid.RowDefinitions.Count > 0 ? DesktopGrid.RowDefinitions[0].Height.Value : 0d);

        BuildComponentLibraryCategoryPages();
        RenderLauncherRootTiles();
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
