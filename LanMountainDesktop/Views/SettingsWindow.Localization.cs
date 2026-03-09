using System;
using System.Linq;
using Avalonia.Controls;
using LanMountainDesktop.Services;

namespace LanMountainDesktop.Views;

public partial class SettingsWindow
{
    private void InitializeLocalization(string? languageCode)
    {
        _languageCode = _localizationService.NormalizeLanguageCode(languageCode);
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
        Title = L("settings.title", "Settings");
        WindowTitleTextBlock.Text = L("settings.title", "Settings");
        WindowSubtitleTextBlock.Text = L("settings.footer", "LanMountainDesktop Settings");

        SettingsNavWallpaperItem.Content = L("settings.nav.wallpaper", "Wallpaper");
        SettingsNavGridItem.Content = L("settings.nav.grid", "Grid");
        SettingsNavColorItem.Content = L("settings.nav.color", "Color");
        SettingsNavStatusBarItem.Content = L("settings.nav.status_bar", "Status Bar");
        SettingsNavWeatherItem.Content = L("settings.nav.weather", "Weather");
        SettingsNavRegionItem.Content = L("settings.nav.region", "Region");
        SettingsNavUpdateItem.Content = L("settings.nav.update", "Update");
        SettingsNavAboutItem.Content = L("settings.nav.about", "About");
        SettingsNavLauncherItem.Content = L("settings.nav.launcher", "App Launcher");
        SettingsNavPluginsItem.Content = L("settings.nav.plugins", "Plugins");

        WallpaperPanelTitleTextBlock.Text = L("settings.wallpaper.title", "Personalize your wallpaper");
        WallpaperPlacementSettingsExpander.Header = L("settings.wallpaper.placement_label", "Placement");
        WallpaperPlacementSettingsExpander.Description = L("settings.wallpaper.placement_desc", "Adjust how the image fits on the desktop.");
        PickWallpaperButton.Content = L("settings.wallpaper.pick_button", "Browse");
        ClearWallpaperButton.Content = L("settings.wallpaper.clear_button", "Reset");
        WallpaperPreviewBackButtonTextBlock.Text = L("button.back_to_windows", "Back to Windows");
        WallpaperPreviewComponentLibraryTextBlock.Text = L("button.component_library", "Edit Desktop");
        GridPreviewBackButtonTextBlock.Text = L("button.back_to_windows", "Back to Windows");
        GridPreviewComponentLibraryTextBlock.Text = L("button.component_library", "Edit Desktop");

        GridPanelTitleTextBlock.Text = L("settings.grid.title", "Grid Layout");
        GridSpacingSettingsExpander.Header = L("settings.grid.spacing_label", "Grid Spacing");
        GridSpacingRelaxedComboBoxItem.Content = L("settings.grid.spacing_relaxed", "Relaxed");
        GridSpacingCompactComboBoxItem.Content = L("settings.grid.spacing_compact", "Compact");
        GridEdgeInsetSettingsExpander.Header = L("settings.grid.edge_inset_label", "Screen Inset");
        ApplyGridButton.Content = L("settings.grid.apply_button", "Apply");
        UpdateGridEdgeInsetComputedPxText(_currentDesktopCellSize);

        ColorPanelTitleTextBlock.Text = L("settings.color.title", "Color");
        ThemeModeSettingsExpander.Header = L("settings.color.day_night_label", "Day/Night");
        RecommendedColorsLabelTextBlock.Text = L("settings.color.recommended_label", "Recommended Colors");
        SystemMonetColorsLabelTextBlock.Text = L("settings.color.system_monet_label", "System Monet Colors");
        RefreshMonetColorsButton.Content = L("settings.color.refresh_button", "Refresh");

        StatusBarPanelTitleTextBlock.Text = L("settings.status_bar.title", "Status Bar");
        StatusBarClockSettingsExpander.Header = L("settings.status_bar.clock_header", "Clock");
        StatusBarSpacingSettingsExpander.Header = L("settings.status_bar.spacing_header", "Component Spacing");
        StatusBarSpacingSettingsExpander.Description = L("settings.status_bar.spacing_desc", "Adjust spacing between status bar components.");
        StatusBarSpacingModeCompactItem.Content = L("settings.status_bar.spacing_mode_compact", "Compact");
        StatusBarSpacingModeRelaxedItem.Content = L("settings.status_bar.spacing_mode_relaxed", "Relaxed");
        StatusBarSpacingModeCustomItem.Content = L("settings.status_bar.spacing_mode_custom", "Custom");
        StatusBarSpacingCustomPanel.Content = L("settings.status_bar.spacing_custom_label", "Custom spacing (%)");

        RegionPanelTitleTextBlock.Text = L("settings.region.title", "Region");
        LanguageSettingsExpander.Header = L("settings.region.language_header", "Language");
        LanguageSettingsExpander.Description = L("settings.region.language_desc", "Select application language. Changes apply immediately.");
        LanguageChineseItem.Content = L("settings.region.language_zh", "Chinese");
        LanguageEnglishItem.Content = L("settings.region.language_en", "English");
        TimeZoneSettingsExpander.Header = L("settings.region.timezone_header", "Time Zone");
        TimeZoneSettingsExpander.Description = L("settings.region.timezone_desc", "Select a time zone. Clock and calendar widgets will follow this zone.");

        LauncherSettingsPanelTitleTextBlock.Text = L("settings.launcher.title", "App Launcher");
        LauncherHiddenItemsSettingsExpander.Header = L("settings.launcher.hidden_header", "Hidden Items");
        LauncherHiddenItemsSettingsExpander.Description = L("settings.launcher.hidden_desc", "Review hidden launcher entries and show them again.");
        LauncherHiddenItemsDescriptionTextBlock.Text = L("settings.launcher.hidden_hint", "Right-click an icon in launcher to hide it. Hidden entries appear here.");
        LauncherHiddenItemsEmptyTextBlock.Text = L("settings.launcher.hidden_empty", "No hidden items.");

        PluginSettingsPanelTitleTextBlock.Text = L("settings.plugins.title", "Plugins");
        PluginSystemSettingsExpander.Header = L("settings.plugins.runtime_header", "Plugin Runtime");
        PluginSystemSettingsExpander.Description = L("settings.plugins.runtime_desc", "Review plugin runtime state and load results.");
        PluginSystemDescriptionTextBlock.Text = L("settings.plugins.runtime_hint", "This page shows discovery status, load results, and runtime diagnostics for installed plugins.");
        PluginSystemStatusTextBlock.Text = L("settings.plugins.runtime_status", "Plugin runtime status will appear here after plugin discovery completes.");
        InstalledPluginsSettingsExpander.Header = L("settings.plugins.installed_header", "Installed Plugins");
        InstalledPluginsSettingsExpander.Description = L("settings.plugins.installed_desc", "Enable or disable plugins here. Detailed plugin settings appear as separate settings pages.");
        PluginRestartHintTextBlock.Text = L("settings.plugins.restart_hint", "Plugin enable state changes take effect after restarting the app.");
        PluginCatalogEmptyTextBlock.Text = L("settings.plugins.empty", "No plugins found.");
        PluginSettingsPanel.RefreshFromRuntime();

        AboutPanelTitleTextBlock.Text = L("settings.about.title", "About");
        VersionTextBlock.Text = Lf("settings.about.version_format", "Version: {0}", GetAppVersionText());
        CodeNameTextBlock.Text = Lf("settings.about.codename_format", "Code Name: {0}", AppCodeName);
        FontInfoTextBlock.Text = Lf("settings.about.font_format", "Font: {0}", AppFontName);
        AboutStartupSettingsExpander.Header = L("settings.about.startup_header", "Windows Startup");
        AboutStartupSettingsExpander.Description = L("settings.about.startup_desc", "Launch the app automatically when signing in to Windows.");
        AboutRenderModeSettingsExpander.Header = L("settings.about.render_mode_header", "Rendering Mode");
        AboutRenderModeSettingsExpander.Description = L(
            "settings.about.render_mode_desc",
            "Choose the rendering backend. Restart the app after changing this option. Unsupported modes fall back to software.");
        SetAppRenderModeComboItemContent(AppRenderingModeHelper.Default, L("settings.about.render_mode.default", "Default"));
        SetAppRenderModeComboItemContent(AppRenderingModeHelper.Software, L("settings.about.render_mode.software", "Software"));
        SetAppRenderModeComboItemContent(AppRenderingModeHelper.AngleEgl, L("settings.about.render_mode.angle_egl", "angleEgl"));
        SetAppRenderModeComboItemContent(AppRenderingModeHelper.Wgl, L("settings.about.render_mode.wgl", "WGL"));
        SetAppRenderModeComboItemContent(AppRenderingModeHelper.Vulkan, L("settings.about.render_mode.vulkan", "Vulkan"));
        UpdateCurrentRenderBackendStatus();
        UpdatePendingRestartDock();

        var placementItems = WallpaperPlacementComboBox.Items.OfType<ComboBoxItem>().ToList();
        if (placementItems.Count >= 5)
        {
            placementItems[0].Content = L("placement.fill", "Fill");
            placementItems[1].Content = L("placement.fit", "Fit");
            placementItems[2].Content = L("placement.stretch", "Stretch");
            placementItems[3].Content = L("placement.center", "Center");
            placementItems[4].Content = L("placement.tile", "Tile");
        }

        ApplyUpdateLocalization();
        UpdateWallpaperDisplay();
        RenderLauncherHiddenItemsList();
    }

    private void SetAppRenderModeComboItemContent(string tag, string content)
    {
        var item = AppRenderModeComboBox.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(candidate =>
                string.Equals(candidate.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase));

        if (item is not null)
        {
            item.Content = content;
        }
    }

    private string GetLocalizedTimeZoneDisplayName(TimeZoneInfo timeZone)
    {
        var offset = timeZone.GetUtcOffset(DateTime.UtcNow);
        var sign = offset >= TimeSpan.Zero ? "+" : "-";
        var hours = Math.Abs(offset.Hours);
        var minutes = Math.Abs(offset.Minutes);
        var name = string.IsNullOrWhiteSpace(timeZone.StandardName) ? timeZone.DisplayName : timeZone.StandardName;

        if (string.Equals(_languageCode, "zh-CN", StringComparison.OrdinalIgnoreCase) &&
            ZhTimeZoneNames.TryGetValue(timeZone.Id, out var localizedName))
        {
            name = localizedName;
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            name = timeZone.Id;
        }

        return $"(UTC{sign}{hours:D2}:{minutes:D2}) {name}";
    }

    private static string GetAppVersionText()
    {
        var version = typeof(MainWindow).Assembly.GetName().Version;
        if (version is null || version.Major < 0 || version.Minor < 0 || version.Build < 0)
        {
            return FallbackAppVersion;
        }

        return $"{version.Major}.{version.Minor}.{version.Build}";
    }

    private void OnLanguageSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressLanguageSelectionEvents || LanguageComboBox.SelectedItem is not ComboBoxItem item)
        {
            return;
        }

        _languageCode = _localizationService.NormalizeLanguageCode(item.Tag as string);
        ApplyLocalization();
        ThemeColorStatusTextBlock.Text = Lf("settings.region.applied_format", "Language switched to: {0}", GetLanguageDisplayName(_languageCode));
        PersistSettings();
    }
}

