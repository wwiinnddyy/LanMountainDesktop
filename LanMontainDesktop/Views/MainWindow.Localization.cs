using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using FluentIcons.Avalonia;
using FluentIcons.Common;

namespace LanMontainDesktop.Views;

public partial class MainWindow
{
    private const string AppCodeName = "Administrate";
    private const string AppFontName = "MiSans";
    private const string FallbackAppVersion = "1.0.0";

    private static readonly IReadOnlyDictionary<string, string> ZhTimeZoneNames =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["China Standard Time"] = "中国标准时间",
            ["Asia/Shanghai"] = "中国标准时间",
            ["Tokyo Standard Time"] = "日本标准时间",
            ["Asia/Tokyo"] = "日本标准时间",
            ["Pacific Standard Time"] = "太平洋标准时间",
            ["America/Los_Angeles"] = "太平洋标准时间",
            ["Eastern Standard Time"] = "美国东部标准时间",
            ["America/New_York"] = "美国东部标准时间",
            ["Central European Standard Time"] = "中欧标准时间",
            ["Europe/Berlin"] = "中欧标准时间",
            ["GMT Standard Time"] = "格林威治标准时间",
            ["Europe/London"] = "格林威治标准时间",
            ["UTC"] = "协调世界时",
            ["Etc/UTC"] = "协调世界时"
        };

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
        ComponentLibraryTitleTextBlock.Text = L("component_library.title", "Widgets");
        ToolTip.SetTip(CloseComponentLibraryButton, L("common.close", "Close"));
        ComponentLibraryEmptyTextBlock.Text = L(
            "component_library.empty",
            "Swipe to pick a category, tap to open, then drag a widget onto the desktop.");

        LauncherTitleTextBlock.Text = L("launcher.title", "App Launcher");
        LauncherSubtitleTextBlock.Text = L(
            "launcher.subtitle",
            "Displays all apps and folders based on the Windows Start menu structure.");
        ToolTip.SetTip(LauncherFolderBackButton, L("common.back", "Back"));
        ToolTip.SetTip(LauncherFolderCloseButton, L("common.close", "Close"));

        SettingsNavHeaderTextBlock.Text = L("settings.nav_header", "Settings");
        SettingsNavWallpaperTextBlock.Text = L("settings.nav.wallpaper", "Wallpaper");
        SettingsNavGridTextBlock.Text = L("settings.nav.grid", "Grid");
        SettingsNavColorTextBlock.Text = L("settings.nav.color", "Color");
        SettingsNavStatusBarTextBlock.Text = L("settings.nav.status_bar", "Status Bar");
        SettingsNavWeatherTextBlock.Text = L("settings.nav.weather", "Weather");
        SettingsNavRegionTextBlock.Text = L("settings.nav.region", "Region");

        WallpaperPanelTitleTextBlock.Text = L("settings.wallpaper.title", "Personalize your wallpaper");
        WallpaperPlacementSettingsExpander.Header = L("settings.wallpaper.placement_label", "Placement");
        WallpaperPlacementSettingsExpander.Description = L(
            "settings.wallpaper.placement_desc",
            "Adjust how the image fits on the desktop.");
        PickWallpaperButton.Content = L("settings.wallpaper.pick_button", "Browse");
        ClearWallpaperButton.Content = L("settings.wallpaper.clear_button", "Reset");

        GridPanelTitleTextBlock.Text = L("settings.grid.title", "Grid Layout");
        GridSpacingPresetLabelTextBlock.Text = L("settings.grid.spacing_label", "Grid Spacing");
        GridSpacingRelaxedComboBoxItem.Content = L("settings.grid.spacing_relaxed", "Relaxed");
        GridSpacingCompactComboBoxItem.Content = L("settings.grid.spacing_compact", "Compact");
        GridEdgeInsetLabelTextBlock.Text = L("settings.grid.edge_inset_label", "Screen Inset");
        ApplyGridButton.Content = L("settings.grid.apply_button", "Apply");
        UpdateGridEdgeInsetComputedPxText(_currentDesktopCellSize);

        ColorPanelTitleTextBlock.Text = L("settings.color.title", "Color");
        ThemeModeSettingsExpander.Header = L("settings.color.day_night_label", "Day/Night");
        NightModeToggleSwitch.OffContent = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Children =
            {
                new SymbolIcon { Symbol = Symbol.WeatherSunny, IconVariant = IconVariant.Regular, FontSize = 14 },
                new TextBlock
                {
                    Text = L("settings.color.day_night_off", "Day"),
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                }
            }
        };
        NightModeToggleSwitch.OnContent = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Children =
            {
                new SymbolIcon { Symbol = Symbol.WeatherMoon, IconVariant = IconVariant.Regular, FontSize = 14 },
                new TextBlock
                {
                    Text = L("settings.color.day_night_on", "Night"),
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                }
            }
        };
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
        StatusBarSpacingCustomLabelTextBlock.Text = L("settings.status_bar.spacing_custom_label", "Custom spacing (%)");

        WeatherPanelTitleTextBlock.Text = L("settings.weather.title", "Weather");
        WeatherLocationSettingsExpander.Header = L("settings.weather.location_source_header", "Location Source");
        WeatherLocationSettingsExpander.Description = L(
            "settings.weather.location_source_desc",
            "Choose how weather widgets resolve location.");
        WeatherLocationModeCityItem.Content = L("settings.weather.mode_city_search", "City Search");
        WeatherLocationModeCoordinatesItem.Content = L("settings.weather.mode_coordinates", "Coordinates");
        WeatherLocationModeCityChipItem.Content = L("settings.weather.mode_city_search", "City Search");
        WeatherLocationModeCoordinatesChipItem.Content = L("settings.weather.mode_coordinates", "Coordinates");
        WeatherAutoRefreshToggleSwitch.Content = L("settings.weather.auto_refresh", "Auto refresh location on startup");

        WeatherCitySearchSettingsExpander.Header = L("settings.weather.city_search_header", "City Search");
        WeatherCitySearchSettingsExpander.Description = L(
            "settings.weather.city_search_desc",
            "Search cities and apply one weather location.");
        WeatherCitySearchTextBox.Watermark = L("settings.weather.search_placeholder", "e.g. Beijing");
        WeatherSearchButton.Content = L("settings.weather.search_button", "Search");
        WeatherApplyCityButton.Content = L("settings.weather.apply_city_button", "Apply City");

        WeatherCoordinateSettingsExpander.Header = L("settings.weather.coordinates_header", "Coordinates");
        WeatherCoordinateSettingsExpander.Description = L(
            "settings.weather.coordinates_desc",
            "Set latitude/longitude and optional key/name.");
        WeatherLatitudeNumberBox.Header = L("settings.weather.latitude_label", "Latitude");
        WeatherLongitudeNumberBox.Header = L("settings.weather.longitude_label", "Longitude");
        WeatherLocationKeyTextBox.Watermark = L("settings.weather.location_key_placeholder", "Location key (optional)");
        WeatherLocationNameTextBox.Watermark = L("settings.weather.location_name_placeholder", "Display name (optional)");
        WeatherApplyCoordinatesButton.Content = L("settings.weather.apply_coordinates_button", "Apply Coordinates");

        WeatherPreviewSettingsExpander.Header = L("settings.weather.preview_panel_header", "Weather Preview");
        WeatherPreviewSettingsExpander.Description = L(
            "settings.weather.preview_panel_desc",
            "Refresh and verify current weather service status.");
        WeatherPreviewButton.Content = L("settings.weather.refresh_button", "Refresh");

        WeatherAlertFilterSettingsExpander.Header = L("settings.weather.alert_filter_header", "Excluded Alerts");
        WeatherAlertFilterSettingsExpander.Description = L(
            "settings.weather.alert_filter_desc",
            "Alerts containing these words will not be shown. One rule per line.");
        WeatherExcludedAlertsTextBox.Watermark = L("settings.weather.alert_filter_placeholder", "One keyword per line");

        WeatherIconPackSettingsExpander.Header = L("settings.weather.icon_style_header", "Weather Icon Style");
        WeatherIconPackSettingsExpander.Description = L(
            "settings.weather.icon_style_desc",
            "Choose Fluent Icon style for weather symbols.");
        WeatherIconPackFluentRegularItem.Content = L("settings.weather.icon_style_fluent_regular", "Fluent Regular");
        WeatherIconPackFluentFilledItem.Content = L("settings.weather.icon_style_fluent_filled", "Fluent Filled");

        WeatherNoTlsSettingsExpander.Header = L("settings.weather.no_tls_header", "No TLS Weather Request");
        WeatherNoTlsSettingsExpander.Description = L(
            "settings.weather.no_tls_desc",
            "Not recommended. Enable only for incompatible network environments.");

        if (string.IsNullOrWhiteSpace(_weatherSearchKeyword))
        {
            WeatherSearchStatusTextBlock.Text = L(
                "settings.weather.search_hint",
                "Search by city name and apply one location.");
        }

        if (!_isWeatherPreviewInProgress)
        {
            WeatherPreviewResultTextBlock.Text = L(
                "settings.weather.preview_hint",
                "Use test fetch to verify your weather configuration.");
        }

        UpdateWeatherLocationStatusText();

        RegionPanelTitleTextBlock.Text = L("settings.region.title", "Region");
        LanguageSettingsExpander.Header = L("settings.region.language_header", "Language");
        LanguageChineseItem.Content = L("settings.region.language_zh", "Chinese");
        LanguageEnglishItem.Content = L("settings.region.language_en", "English");
        TimeZoneSettingsExpander.Header = L("settings.region.timezone_header", "Time Zone");
        TimeZoneSettingsExpander.Description = L(
            "settings.region.timezone_desc",
            "Select a time zone. Clock and calendar widgets will follow this zone.");

        SettingsNavAboutTextBlock.Text = L("settings.nav.about", "About");
        AboutPanelTitleTextBlock.Text = L("settings.about.title", "About");
        VersionTextBlock.Text = Lf(
            "settings.about.version_format",
            "Version: {0}",
            GetAppVersionText());
        CodeNameTextBlock.Text = Lf(
            "settings.about.codename_format",
            "Code Name: {0}",
            AppCodeName);
        FontInfoTextBlock.Text = Lf(
            "settings.about.font_format",
            "Font: {0}",
            AppFontName);

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

        InitializeTimeZoneSettings();
        BuildComponentLibraryCategoryPages();
        RenderLauncherRootTiles();
        UpdateOpenSettingsActionVisualState();
        UpdateWallpaperDisplay();
    }

    private string GetLocalizedTimeZoneDisplayName(TimeZoneInfo timeZone)
    {
        var offset = timeZone.GetUtcOffset(DateTime.UtcNow);
        var sign = offset >= TimeSpan.Zero ? "+" : "-";
        var hours = Math.Abs(offset.Hours);
        var minutes = Math.Abs(offset.Minutes);
        var name = string.IsNullOrWhiteSpace(timeZone.StandardName)
            ? timeZone.DisplayName
            : timeZone.StandardName;

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

    private void UpdateWeatherLocationStatusText()
    {
        if (WeatherLocationStatusTextBlock is null)
        {
            return;
        }

        var modeText = _weatherLocationMode == WeatherLocationMode.Coordinates
            ? L("settings.weather.mode_coordinates", "Coordinates")
            : L("settings.weather.mode_city_search", "City Search");

        if (_weatherLocationMode == WeatherLocationMode.CitySearch)
        {
            if (string.IsNullOrWhiteSpace(_weatherLocationKey))
            {
                WeatherLocationStatusTextBlock.Text = L(
                    "settings.weather.status_city_empty",
                    "No city location is configured.");
                return;
            }

            var locationName = string.IsNullOrWhiteSpace(_weatherLocationName)
                ? _weatherLocationKey
                : _weatherLocationName;
            WeatherLocationStatusTextBlock.Text = Lf(
                "settings.weather.status_city_format",
                "Mode: {0} | {1} | Key: {2}",
                modeText,
                locationName,
                _weatherLocationKey);
            return;
        }

        WeatherLocationStatusTextBlock.Text = Lf(
            "settings.weather.status_coordinates_format",
            "Mode: {0} | Lat {1:F4}, Lon {2:F4} | Key: {3}",
            modeText,
            _weatherLatitude,
            _weatherLongitude,
            string.IsNullOrWhiteSpace(_weatherLocationKey)
                ? BuildCoordinateLocationKey(_weatherLatitude, _weatherLongitude)
                : _weatherLocationKey);
    }
}
