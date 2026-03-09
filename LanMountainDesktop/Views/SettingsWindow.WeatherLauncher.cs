using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using FluentIcons.Common;
using LanMountainDesktop.Models;
using LanMountainDesktop.Services;

namespace LanMountainDesktop.Views;

public partial class SettingsWindow
{
    private void InitializeWeatherSettings(AppSettingsSnapshot snapshot)
    {
        _suppressWeatherLocationEvents = true;
        try
        {
            _weatherLocationMode = ParseWeatherLocationMode(snapshot.WeatherLocationMode);
            _weatherLocationKey = snapshot.WeatherLocationKey?.Trim() ?? string.Empty;
            _weatherLocationName = snapshot.WeatherLocationName?.Trim() ?? string.Empty;
            _weatherLatitude = NormalizeLatitude(snapshot.WeatherLatitude);
            _weatherLongitude = NormalizeLongitude(snapshot.WeatherLongitude);
            _weatherAutoRefreshLocation = snapshot.WeatherAutoRefreshLocation;
            _weatherExcludedAlertsRaw = snapshot.WeatherExcludedAlerts?.Trim() ?? string.Empty;
            _weatherIconPackId = string.IsNullOrWhiteSpace(snapshot.WeatherIconPackId) ? "FluentRegular" : snapshot.WeatherIconPackId.Trim();
            _weatherNoTlsRequests = snapshot.WeatherNoTlsRequests;
            _weatherSearchKeyword = string.Empty;

            var legacyQuery = snapshot.WeatherLocationQuery?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(_weatherLocationKey) && !string.IsNullOrWhiteSpace(legacyQuery))
            {
                _weatherLocationKey = legacyQuery;
            }

            if (string.IsNullOrWhiteSpace(_weatherLocationName) && !string.IsNullOrWhiteSpace(legacyQuery))
            {
                _weatherLocationName = legacyQuery;
            }

            SelectWeatherLocationModeInUi(_weatherLocationMode);
            WeatherAutoRefreshToggleSwitch.IsChecked = _weatherAutoRefreshLocation;
            WeatherNoTlsToggleSwitch.IsChecked = _weatherNoTlsRequests;
            WeatherCitySearchTextBox.Text = string.Empty;
            WeatherCityResultsComboBox.Items.Clear();
            WeatherLocationKeyTextBox.Text = _weatherLocationKey;
            WeatherLocationNameTextBox.Text = _weatherLocationName;
            WeatherLatitudeNumberBox.Value = _weatherLatitude;
            WeatherLongitudeNumberBox.Value = _weatherLongitude;
            WeatherExcludedAlertsTextBox.Text = _weatherExcludedAlertsRaw;
            SelectWeatherIconPackInUi(_weatherIconPackId);
            WeatherSearchStatusTextBlock.Text = L("settings.weather.search_hint", "Search by city name and apply one location.");
            WeatherCoordinateStatusTextBlock.Text = string.Empty;
            WeatherPreviewResultTextBlock.Text = L("settings.weather.preview_hint", "Use test fetch to verify your weather configuration.");
            UpdateWeatherPreviewSummary(weatherCode: null, temperatureText: "--", updatedAt: null);
        }
        finally
        {
            _suppressWeatherLocationEvents = false;
        }

        UpdateWeatherLocationModePanels();
        UpdateWeatherLocationStatusText();
    }

    private void InitializeAutoStartWithWindowsSetting(AppSettingsSnapshot snapshot)
    {
        _autoStartWithWindows = OperatingSystem.IsWindows()
            ? _windowsStartupService.IsEnabled()
            : snapshot.AutoStartWithWindows;

        _suppressAutoStartToggleEvents = true;
        try
        {
            AutoStartWithWindowsToggleSwitch.IsEnabled = OperatingSystem.IsWindows();
            AutoStartWithWindowsToggleSwitch.IsChecked = _autoStartWithWindows;
        }
        finally
        {
            _suppressAutoStartToggleEvents = false;
        }
    }

    private void InitializeAppRenderModeSetting(AppSettingsSnapshot snapshot)
    {
        _selectedAppRenderMode = AppRenderingModeHelper.Normalize(snapshot.AppRenderMode);
        _runningAppRenderMode = ResolveActiveAppRenderModeForUi(_selectedAppRenderMode);
        var renderModeForUi = PendingRestartStateService.HasPendingReason(PendingRestartStateService.RenderModeReason)
            ? _selectedAppRenderMode
            : _runningAppRenderMode;

        _suppressAppRenderModeSelectionEvents = true;
        try
        {
            AppRenderModeComboBox.IsEnabled = OperatingSystem.IsWindows();
            SelectAppRenderModeInUi(renderModeForUi);
        }
        finally
        {
            _suppressAppRenderModeSelectionEvents = false;
        }
    }

    private void SelectAppRenderModeInUi(string renderMode)
    {
        AppRenderModeComboBox.SelectedIndex = GetAppRenderModeComboBoxIndex(renderMode);
    }

    private static int GetAppRenderModeComboBoxIndex(string renderMode)
    {
        return AppRenderingModeHelper.Normalize(renderMode) switch
        {
            AppRenderingModeHelper.Software => 1,
            AppRenderingModeHelper.AngleEgl => 2,
            AppRenderingModeHelper.Wgl => 3,
            AppRenderingModeHelper.Vulkan => 4,
            _ => 0
        };
    }

    private static string ResolveActiveAppRenderModeForUi(string configuredRenderMode)
    {
        var detectedRenderMode = AppRenderBackendDiagnostics.Detect().ActualBackend;
        return string.Equals(detectedRenderMode, AppRenderBackendDiagnostics.Unknown, StringComparison.Ordinal)
            ? configuredRenderMode
            : AppRenderingModeHelper.Normalize(detectedRenderMode);
    }

    private static WeatherLocationMode ParseWeatherLocationMode(string? value)
    {
        return string.Equals(value, "Coordinates", StringComparison.OrdinalIgnoreCase)
            ? WeatherLocationMode.Coordinates
            : WeatherLocationMode.CitySearch;
    }

    private static string ToWeatherLocationModeTag(WeatherLocationMode mode)
    {
        return mode == WeatherLocationMode.Coordinates ? "Coordinates" : "CitySearch";
    }

    private static double NormalizeLatitude(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return 39.9042;
        }

        return Math.Clamp(value, -90, 90);
    }

    private static double NormalizeLongitude(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return 116.4074;
        }

        return Math.Clamp(value, -180, 180);
    }

    private string BuildLegacyWeatherLocationQuery()
    {
        if (!string.IsNullOrWhiteSpace(_weatherLocationName))
        {
            return _weatherLocationName;
        }

        if (!string.IsNullOrWhiteSpace(_weatherLocationKey))
        {
            return _weatherLocationKey;
        }

        return string.Create(CultureInfo.InvariantCulture, $"{_weatherLatitude:F4},{_weatherLongitude:F4}");
    }

    private void SelectWeatherLocationModeInUi(WeatherLocationMode mode)
    {
        var targetTag = ToWeatherLocationModeTag(mode);
        foreach (var item in WeatherLocationModeComboBox.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Tag?.ToString(), targetTag, StringComparison.OrdinalIgnoreCase))
            {
                WeatherLocationModeComboBox.SelectedItem = item;
                break;
            }
        }

        foreach (var item in WeatherLocationModeChipListBox.Items.OfType<ListBoxItem>())
        {
            if (string.Equals(item.Tag?.ToString(), targetTag, StringComparison.OrdinalIgnoreCase))
            {
                WeatherLocationModeChipListBox.SelectedItem = item;
                return;
            }
        }

        WeatherLocationModeComboBox.SelectedIndex = mode == WeatherLocationMode.Coordinates ? 1 : 0;
        WeatherLocationModeChipListBox.SelectedIndex = mode == WeatherLocationMode.Coordinates ? 1 : 0;
    }

    private void SelectWeatherIconPackInUi(string iconPackId)
    {
        foreach (var item in WeatherIconPackComboBox.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Tag?.ToString(), iconPackId, StringComparison.OrdinalIgnoreCase))
            {
                WeatherIconPackComboBox.SelectedItem = item;
                return;
            }
        }

        WeatherIconPackComboBox.SelectedIndex = 0;
        _weatherIconPackId = "FluentRegular";
    }

    private void UpdateWeatherLocationModePanels()
    {
        WeatherCitySearchSettingsExpander.IsVisible = _weatherLocationMode == WeatherLocationMode.CitySearch;
        WeatherCoordinateSettingsExpander.IsVisible = _weatherLocationMode == WeatherLocationMode.Coordinates;
    }

    private void OnWeatherLocationModeSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressWeatherLocationEvents || WeatherLocationModeComboBox.SelectedItem is not ComboBoxItem item)
        {
            return;
        }

        _weatherLocationMode = ParseWeatherLocationMode(item.Tag?.ToString());
        _suppressWeatherLocationEvents = true;
        try
        {
            SelectWeatherLocationModeInUi(_weatherLocationMode);
        }
        finally
        {
            _suppressWeatherLocationEvents = false;
        }

        UpdateWeatherLocationModePanels();
        UpdateWeatherLocationStatusText();
        PersistSettings();
    }

    private void OnWeatherLocationModeChipSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressWeatherLocationEvents || WeatherLocationModeChipListBox.SelectedItem is not ListBoxItem item)
        {
            return;
        }

        _weatherLocationMode = ParseWeatherLocationMode(item.Tag?.ToString());
        _suppressWeatherLocationEvents = true;
        try
        {
            SelectWeatherLocationModeInUi(_weatherLocationMode);
        }
        finally
        {
            _suppressWeatherLocationEvents = false;
        }

        UpdateWeatherLocationModePanels();
        UpdateWeatherLocationStatusText();
        PersistSettings();
    }

    private void OnWeatherAutoRefreshToggled(object? sender, RoutedEventArgs e)
    {
        if (_suppressWeatherLocationEvents)
        {
            return;
        }

        _weatherAutoRefreshLocation = WeatherAutoRefreshToggleSwitch.IsChecked == true;
        PersistSettings();
    }

    private void OnWeatherExcludedAlertsLostFocus(object? sender, RoutedEventArgs e)
    {
        _weatherExcludedAlertsRaw = WeatherExcludedAlertsTextBox.Text?.Trim() ?? string.Empty;
        PersistSettings();
    }

    private void OnWeatherIconPackSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressWeatherLocationEvents || WeatherIconPackComboBox.SelectedItem is not ComboBoxItem item)
        {
            return;
        }

        _weatherIconPackId = item.Tag?.ToString() switch
        {
            "FluentFilled" => "FluentFilled",
            _ => "FluentRegular"
        };

        WeatherPreviewIconSymbol.IconVariant = string.Equals(_weatherIconPackId, "FluentFilled", StringComparison.OrdinalIgnoreCase)
            ? IconVariant.Filled
            : IconVariant.Regular;
        PersistSettings();
    }

    private void OnWeatherNoTlsToggled(object? sender, RoutedEventArgs e)
    {
        if (_suppressWeatherLocationEvents)
        {
            return;
        }

        _weatherNoTlsRequests = WeatherNoTlsToggleSwitch.IsChecked == true;
        PersistSettings();
    }

    private void OnAutoStartWithWindowsToggled(object? sender, RoutedEventArgs e)
    {
        if (_suppressAutoStartToggleEvents)
        {
            return;
        }

        var requested = AutoStartWithWindowsToggleSwitch.IsChecked == true;
        if (!OperatingSystem.IsWindows())
        {
            _autoStartWithWindows = false;
            _suppressAutoStartToggleEvents = true;
            try
            {
                AutoStartWithWindowsToggleSwitch.IsEnabled = false;
                AutoStartWithWindowsToggleSwitch.IsChecked = false;
            }
            finally
            {
                _suppressAutoStartToggleEvents = false;
            }

            PersistSettings();
            return;
        }

        var applied = _windowsStartupService.SetEnabled(requested);
        _autoStartWithWindows = _windowsStartupService.IsEnabled();
        if (!applied || _autoStartWithWindows != requested)
        {
            _suppressAutoStartToggleEvents = true;
            try
            {
                AutoStartWithWindowsToggleSwitch.IsChecked = _autoStartWithWindows;
            }
            finally
            {
                _suppressAutoStartToggleEvents = false;
            }
        }

        PersistSettings();
    }

    private void OnAppRenderModeSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressAppRenderModeSelectionEvents)
        {
            return;
        }

        var selectedMode = AppRenderingModeHelper.Normalize(
            TryGetSelectedComboBoxTag(AppRenderModeComboBox));

        if (string.Equals(_selectedAppRenderMode, selectedMode, StringComparison.Ordinal))
        {
            return;
        }

        _selectedAppRenderMode = selectedMode;
        PersistSettings();
        var requiresRestart = !string.Equals(_runningAppRenderMode, selectedMode, StringComparison.Ordinal);
        PendingRestartStateService.SetPending(PendingRestartStateService.RenderModeReason, requiresRestart);
        UpdatePendingRestartDock();

        if (requiresRestart)
        {
            _ = ShowRenderModeRestartPromptAsync(selectedMode);
        }
    }

    private async void OnSearchWeatherCityClick(object? sender, RoutedEventArgs e)
    {
        if (_isWeatherSearchInProgress)
        {
            return;
        }

        var keyword = WeatherCitySearchTextBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(keyword))
        {
            WeatherSearchStatusTextBlock.Text = L("settings.weather.search_required", "Please enter a city keyword first.");
            return;
        }

        _weatherSearchKeyword = keyword;
        _isWeatherSearchInProgress = true;
        SetWeatherSearchBusy(true);
        try
        {
            var result = await _weatherDataService.SearchLocationsAsync(keyword, ResolveWeatherApiLocale());
            if (!result.Success || result.Data is null)
            {
                WeatherCityResultsComboBox.Items.Clear();
                WeatherSearchStatusTextBlock.Text = Lf("settings.weather.search_failed_format", "Search failed: {0}", result.ErrorMessage ?? result.ErrorCode ?? "Unknown error");
                return;
            }

            var locations = result.Data.Where(location => !string.IsNullOrWhiteSpace(location.LocationKey)).Take(80).ToList();
            WeatherCityResultsComboBox.Items.Clear();
            foreach (var location in locations)
            {
                WeatherCityResultsComboBox.Items.Add(new ComboBoxItem
                {
                    Content = FormatWeatherLocationDisplayName(location),
                    Tag = location
                });
            }

            WeatherSearchStatusTextBlock.Text = locations.Count == 0
                ? L("settings.weather.search_no_results", "No locations were found.")
                : Lf("settings.weather.search_result_count_format", "Found {0} locations.", locations.Count);

            if (locations.Count > 0)
            {
                WeatherCityResultsComboBox.SelectedIndex = 0;
            }
        }
        catch (Exception ex)
        {
            WeatherSearchStatusTextBlock.Text = Lf("settings.weather.search_failed_format", "Search failed: {0}", ex.Message);
        }
        finally
        {
            _isWeatherSearchInProgress = false;
            SetWeatherSearchBusy(false);
        }
    }

    private static string FormatWeatherLocationDisplayName(WeatherLocation location)
    {
        var affiliation = string.IsNullOrWhiteSpace(location.Affiliation) ? string.Empty : $" ({location.Affiliation})";
        return string.Create(CultureInfo.InvariantCulture, $"{location.Name}{affiliation} | {location.LocationKey}");
    }

    private static string BuildWeatherLocationName(WeatherLocation location)
    {
        if (string.IsNullOrWhiteSpace(location.Affiliation))
        {
            return location.Name;
        }

        return string.Create(CultureInfo.InvariantCulture, $"{location.Name} ({location.Affiliation})");
    }

    private void OnApplyWeatherCitySelectionClick(object? sender, RoutedEventArgs e)
    {
        if (WeatherCityResultsComboBox.SelectedItem is not ComboBoxItem item || item.Tag is not WeatherLocation location)
        {
            WeatherSearchStatusTextBlock.Text = L("settings.weather.search_select_required", "Please select one location from search results.");
            return;
        }

        _weatherLocationMode = WeatherLocationMode.CitySearch;
        _weatherLocationKey = location.LocationKey.Trim();
        _weatherLocationName = BuildWeatherLocationName(location);
        _weatherLatitude = NormalizeLatitude(location.Latitude);
        _weatherLongitude = NormalizeLongitude(location.Longitude);

        _suppressWeatherLocationEvents = true;
        try
        {
            SelectWeatherLocationModeInUi(_weatherLocationMode);
            WeatherLocationKeyTextBox.Text = _weatherLocationKey;
            WeatherLocationNameTextBox.Text = _weatherLocationName;
            WeatherLatitudeNumberBox.Value = _weatherLatitude;
            WeatherLongitudeNumberBox.Value = _weatherLongitude;
        }
        finally
        {
            _suppressWeatherLocationEvents = false;
        }

        WeatherSearchStatusTextBlock.Text = Lf("settings.weather.search_applied_format", "Location applied: {0}", _weatherLocationName);
        UpdateWeatherLocationModePanels();
        UpdateWeatherLocationStatusText();
        PersistSettings();
    }

    private void OnApplyWeatherCoordinatesClick(object? sender, RoutedEventArgs e)
    {
        var latitude = NormalizeLatitude(WeatherLatitudeNumberBox.Value);
        var longitude = NormalizeLongitude(WeatherLongitudeNumberBox.Value);
        var keyInput = WeatherLocationKeyTextBox.Text?.Trim() ?? string.Empty;
        var nameInput = WeatherLocationNameTextBox.Text?.Trim() ?? string.Empty;

        _weatherLocationMode = WeatherLocationMode.Coordinates;
        _weatherLatitude = latitude;
        _weatherLongitude = longitude;
        _weatherLocationKey = string.IsNullOrWhiteSpace(keyInput) ? BuildCoordinateLocationKey(latitude, longitude) : keyInput;
        _weatherLocationName = string.IsNullOrWhiteSpace(nameInput)
            ? Lf("settings.weather.coordinates_default_name_format", "Coordinate {0:F4}, {1:F4}", latitude, longitude)
            : nameInput;

        _suppressWeatherLocationEvents = true;
        try
        {
            SelectWeatherLocationModeInUi(_weatherLocationMode);
            if (string.IsNullOrWhiteSpace(keyInput))
            {
                WeatherLocationKeyTextBox.Text = _weatherLocationKey;
            }

            if (string.IsNullOrWhiteSpace(nameInput))
            {
                WeatherLocationNameTextBox.Text = _weatherLocationName;
            }
        }
        finally
        {
            _suppressWeatherLocationEvents = false;
        }

        WeatherCoordinateStatusTextBlock.Text = Lf("settings.weather.coordinates_saved_format", "Coordinates saved: {0:F4}, {1:F4}", _weatherLatitude, _weatherLongitude);
        UpdateWeatherLocationModePanels();
        UpdateWeatherLocationStatusText();
        PersistSettings();
    }

    private static string BuildCoordinateLocationKey(double latitude, double longitude)
    {
        return string.Create(CultureInfo.InvariantCulture, $"coord:{latitude:F4},{longitude:F4}");
    }

    private async void OnTestWeatherRequestClick(object? sender, RoutedEventArgs e)
    {
        if (_isWeatherPreviewInProgress)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_weatherLocationKey))
        {
            if (_weatherLocationMode == WeatherLocationMode.Coordinates)
            {
                _weatherLocationKey = BuildCoordinateLocationKey(_weatherLatitude, _weatherLongitude);
            }
            else
            {
                WeatherPreviewResultTextBlock.Text = L("settings.weather.preview_missing_location", "Please apply one weather location before testing.");
                UpdateWeatherPreviewSummary(null, "--", null);
                return;
            }
        }

        _isWeatherPreviewInProgress = true;
        SetWeatherPreviewBusy(true);
        try
        {
            var query = new WeatherQuery(_weatherLocationKey, _weatherLatitude, _weatherLongitude, 3, ResolveWeatherApiLocale(), false, true);
            var result = await _weatherDataService.GetWeatherAsync(query);
            if (!result.Success || result.Data is null)
            {
                WeatherPreviewResultTextBlock.Text = Lf("settings.weather.preview_failed_format", "Test fetch failed: {0}", result.ErrorMessage ?? result.ErrorCode ?? "Unknown error");
                UpdateWeatherPreviewSummary(null, "--", DateTimeOffset.Now);
                return;
            }

            var snapshot = result.Data;
            var location = string.IsNullOrWhiteSpace(snapshot.LocationName)
                ? (!string.IsNullOrWhiteSpace(_weatherLocationName) ? _weatherLocationName : _weatherLocationKey)
                : snapshot.LocationName;
            var weather = snapshot.Current.WeatherText ?? L("settings.weather.preview_unknown", "Unknown");
            var temperature = snapshot.Current.TemperatureC.HasValue
                ? string.Create(CultureInfo.InvariantCulture, $"{snapshot.Current.TemperatureC.Value:F1} C")
                : "--";
            var updatedAt = snapshot.ObservationTime ?? snapshot.FetchedAt;

            WeatherPreviewResultTextBlock.Text = Lf("settings.weather.preview_success_format", "Test success: {0} | {1} | {2}", location, weather, temperature);
            UpdateWeatherPreviewSummary(snapshot.Current.WeatherCode, temperature, updatedAt);
        }
        catch (Exception ex)
        {
            WeatherPreviewResultTextBlock.Text = Lf("settings.weather.preview_failed_format", "Test fetch failed: {0}", ex.Message);
            UpdateWeatherPreviewSummary(null, "--", DateTimeOffset.Now);
        }
        finally
        {
            _isWeatherPreviewInProgress = false;
            SetWeatherPreviewBusy(false);
        }
    }

    private void UpdateWeatherPreviewSummary(int? weatherCode, string temperatureText, DateTimeOffset? updatedAt)
    {
        WeatherPreviewIconSymbol.Symbol = ResolveWeatherPreviewSymbol(weatherCode, _isNightMode);
        WeatherPreviewIconSymbol.IconVariant = string.Equals(_weatherIconPackId, "FluentFilled", StringComparison.OrdinalIgnoreCase)
            ? IconVariant.Filled
            : IconVariant.Regular;
        WeatherPreviewTemperatureTextBlock.Text = string.IsNullOrWhiteSpace(temperatureText) ? "--" : temperatureText;
        WeatherPreviewUpdatedTextBlock.Text = updatedAt.HasValue
            ? Lf("weather.widget.updated_format", "Updated {0:HH:mm}", updatedAt.Value.LocalDateTime)
            : "-";
    }

    private static Symbol ResolveWeatherPreviewSymbol(int? weatherCode, bool isNight)
    {
        return weatherCode switch
        {
            0 => isNight ? Symbol.WeatherMoon : Symbol.WeatherSunny,
            1 or 2 => isNight ? Symbol.WeatherPartlyCloudyNight : Symbol.WeatherPartlyCloudyDay,
            3 or 7 => Symbol.WeatherRainShowersDay,
            8 or 9 => Symbol.WeatherRain,
            4 => Symbol.WeatherThunderstorm,
            13 or 14 or 15 or 16 => Symbol.WeatherSnow,
            18 or 32 => Symbol.WeatherFog,
            _ => isNight ? Symbol.WeatherPartlyCloudyNight : Symbol.WeatherPartlyCloudyDay
        };
    }

    private void SetWeatherSearchBusy(bool isBusy)
    {
        WeatherSearchButton.IsEnabled = !isBusy;
        WeatherSearchProgressRing.IsVisible = isBusy;
    }

    private void SetWeatherPreviewBusy(bool isBusy)
    {
        WeatherPreviewButton.IsEnabled = !isBusy;
        WeatherPreviewProgressRing.IsVisible = isBusy;
    }

    private string ResolveWeatherApiLocale()
    {
        return string.Equals(_languageCode, "zh-CN", StringComparison.OrdinalIgnoreCase) ? "zh_cn" : "en_us";
    }

    private void UpdateWeatherLocationStatusText()
    {
        var modeText = _weatherLocationMode == WeatherLocationMode.Coordinates
            ? L("settings.weather.mode_coordinates", "Coordinates")
            : L("settings.weather.mode_city_search", "City Search");

        if (_weatherLocationMode == WeatherLocationMode.CitySearch)
        {
            if (string.IsNullOrWhiteSpace(_weatherLocationKey))
            {
                WeatherLocationStatusTextBlock.Text = L("settings.weather.status_city_empty", "No city location is configured.");
                return;
            }

            var locationName = string.IsNullOrWhiteSpace(_weatherLocationName) ? _weatherLocationKey : _weatherLocationName;
            WeatherLocationStatusTextBlock.Text = Lf("settings.weather.status_city_format", "Mode: {0} | {1} | Key: {2}", modeText, locationName, _weatherLocationKey);
            return;
        }

        WeatherLocationStatusTextBlock.Text = Lf(
            "settings.weather.status_coordinates_format",
            "Mode: {0} | Lat {1:F4}, Lon {2:F4} | Key: {3}",
            modeText,
            _weatherLatitude,
            _weatherLongitude,
            string.IsNullOrWhiteSpace(_weatherLocationKey) ? BuildCoordinateLocationKey(_weatherLatitude, _weatherLongitude) : _weatherLocationKey);
    }

    private void InitializeLauncherVisibilitySettings(LauncherSettingsSnapshot snapshot)
    {
        _hiddenLauncherFolderPaths.Clear();
        if (snapshot.HiddenLauncherFolderPaths is not null)
        {
            foreach (var folderPath in snapshot.HiddenLauncherFolderPaths)
            {
                var key = NormalizeLauncherHiddenKey(folderPath);
                if (!string.IsNullOrWhiteSpace(key))
                {
                    _hiddenLauncherFolderPaths.Add(key);
                }
            }
        }

        _hiddenLauncherAppPaths.Clear();
        if (snapshot.HiddenLauncherAppPaths is not null)
        {
            foreach (var appPath in snapshot.HiddenLauncherAppPaths)
            {
                var key = NormalizeLauncherHiddenKey(appPath);
                if (!string.IsNullOrWhiteSpace(key))
                {
                    _hiddenLauncherAppPaths.Add(key);
                }
            }
        }
    }

    private async Task LoadLauncherEntriesAsync()
    {
        try
        {
            var loadResult = await Task.Run(() =>
            {
                var loadedRoot = OperatingSystem.IsLinux() ? _linuxDesktopEntryService.Load() : _windowsStartMenuService.Load();
                var folderIconBytes = OperatingSystem.IsWindows() ? WindowsIconService.TryGetSystemFolderIconPngBytes() : null;
                return (Root: loadedRoot, FolderIcon: folderIconBytes);
            });

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _startMenuRoot = loadResult.Root;
                _launcherFolderIconPngBytes = loadResult.FolderIcon;
                _launcherFolderIconBitmap?.Dispose();
                _launcherFolderIconBitmap = null;
                RenderLauncherHiddenItemsList();
            }, DispatcherPriority.Background);
        }
        catch
        {
            _startMenuRoot = new StartMenuFolderNode("All Apps", string.Empty);
            _launcherFolderIconPngBytes = null;
            _launcherFolderIconBitmap?.Dispose();
            _launcherFolderIconBitmap = null;
            RenderLauncherHiddenItemsList();
        }
    }

    private static string NormalizeLauncherHiddenKey(string? key)
    {
        return string.IsNullOrWhiteSpace(key) ? string.Empty : key.Trim();
    }

    private void RenderLauncherHiddenItemsList()
    {
        LauncherHiddenItemsListPanel.Children.Clear();
        var hiddenItems = BuildLauncherHiddenItems();
        LauncherHiddenItemsEmptyTextBlock.IsVisible = hiddenItems.Count == 0;
        if (hiddenItems.Count == 0)
        {
            return;
        }

        foreach (var hiddenItem in hiddenItems)
        {
            LauncherHiddenItemsListPanel.Children.Add(CreateLauncherHiddenItemRow(hiddenItem));
        }
    }

    private IReadOnlyList<LauncherHiddenItemView> BuildLauncherHiddenItems()
    {
        var items = new List<LauncherHiddenItemView>();
        var seenFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenApps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        CollectHiddenLauncherItems(_startMenuRoot, items, seenFolders, seenApps);

        foreach (var key in _hiddenLauncherFolderPaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            if (!seenFolders.Contains(key))
            {
                items.Add(new LauncherHiddenItemView(LauncherEntryKind.Folder, key, BuildLauncherHiddenFallbackDisplayName(key), "DIR", GetLauncherFolderIconBitmap()));
            }
        }

        foreach (var key in _hiddenLauncherAppPaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            if (!seenApps.Contains(key))
            {
                var fallbackName = BuildLauncherHiddenFallbackDisplayName(key);
                items.Add(new LauncherHiddenItemView(LauncherEntryKind.Shortcut, key, fallbackName, BuildMonogram(fallbackName), null));
            }
        }

        return items
            .OrderBy(item => item.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void CollectHiddenLauncherItems(StartMenuFolderNode folder, List<LauncherHiddenItemView> items, HashSet<string> seenFolders, HashSet<string> seenApps)
    {
        foreach (var subFolder in folder.Folders)
        {
            var folderKey = NormalizeLauncherHiddenKey(subFolder.RelativePath);
            if (!string.IsNullOrWhiteSpace(folderKey) && _hiddenLauncherFolderPaths.Contains(folderKey) && seenFolders.Add(folderKey))
            {
                items.Add(new LauncherHiddenItemView(LauncherEntryKind.Folder, folderKey, subFolder.Name, "DIR", GetLauncherFolderIconBitmap()));
            }

            CollectHiddenLauncherItems(subFolder, items, seenFolders, seenApps);
        }

        foreach (var app in folder.Apps)
        {
            var appKey = NormalizeLauncherHiddenKey(app.RelativePath);
            if (string.IsNullOrWhiteSpace(appKey) || !_hiddenLauncherAppPaths.Contains(appKey) || !seenApps.Add(appKey))
            {
                continue;
            }

            items.Add(new LauncherHiddenItemView(LauncherEntryKind.Shortcut, appKey, app.DisplayName, BuildMonogram(app.DisplayName), GetLauncherIconBitmap(app)));
        }
    }

    private static string BuildLauncherHiddenFallbackDisplayName(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return "Unknown";
        }

        var normalized = key.Replace('\\', '/');
        var fileName = Path.GetFileNameWithoutExtension(normalized);
        return string.IsNullOrWhiteSpace(fileName) ? key : fileName;
    }

    private Control CreateLauncherHiddenItemRow(LauncherHiddenItemView hiddenItem)
    {
        Control icon = hiddenItem.IconBitmap is not null
            ? new Image
            {
                Source = hiddenItem.IconBitmap,
                Width = 24,
                Height = 24,
                Stretch = Stretch.Uniform
            }
            : new Border
            {
                Width = 24,
                Height = 24,
                CornerRadius = new CornerRadius(999),
                Background = GetThemeBrush("AdaptiveButtonBackgroundBrush"),
                Child = new TextBlock
                {
                    Text = hiddenItem.Monogram,
                    FontSize = 10,
                    FontWeight = FontWeight.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };

        var typeText = hiddenItem.Kind == LauncherEntryKind.Folder
            ? L("settings.launcher.hidden_type_folder", "Folder")
            : L("settings.launcher.hidden_type_shortcut", "Shortcut");

        var infoPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        infoPanel.Children.Add(icon);
        infoPanel.Children.Add(new StackPanel
        {
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
            Children =
            {
                new TextBlock { Text = hiddenItem.DisplayName, TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 1 },
                new TextBlock { Text = typeText, FontSize = 11, Opacity = 0.7 }
            }
        });

        var restoreButton = new Button
        {
            Content = L("settings.launcher.restore_button", "Show Again"),
            MinWidth = 110,
            Padding = new Thickness(12, 6),
            Tag = new LauncherHiddenItemToken(hiddenItem.Kind, hiddenItem.Key)
        };
        restoreButton.Click += OnRestoreLauncherHiddenItemClick;

        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 10
        };
        row.Children.Add(infoPanel);
        Grid.SetColumn(infoPanel, 0);
        row.Children.Add(restoreButton);
        Grid.SetColumn(restoreButton, 1);

        return new Border
        {
            Classes = { "glass-panel" },
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(10, 8),
            Child = row
        };
    }

    private void OnRestoreLauncherHiddenItemClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: LauncherHiddenItemToken token })
        {
            return;
        }

        var removed = token.Kind switch
        {
            LauncherEntryKind.Folder => _hiddenLauncherFolderPaths.Remove(token.Key),
            LauncherEntryKind.Shortcut => _hiddenLauncherAppPaths.Remove(token.Key),
            _ => false
        };

        if (!removed)
        {
            return;
        }

        RenderLauncherHiddenItemsList();
        PersistSettings();
    }

    private Bitmap? GetLauncherIconBitmap(StartMenuAppEntry app)
    {
        if (app.IconPngBytes is null || app.IconPngBytes.Length == 0)
        {
            return null;
        }

        if (_launcherIconCache.TryGetValue(app.RelativePath, out var cached))
        {
            return cached;
        }

        try
        {
            using var stream = new MemoryStream(app.IconPngBytes, writable: false);
            var bitmap = new Bitmap(stream);
            _launcherIconCache[app.RelativePath] = bitmap;
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    private Bitmap? GetLauncherFolderIconBitmap()
    {
        if (_launcherFolderIconBitmap is not null)
        {
            return _launcherFolderIconBitmap;
        }

        if (_launcherFolderIconPngBytes is null || _launcherFolderIconPngBytes.Length == 0)
        {
            return null;
        }

        try
        {
            using var stream = new MemoryStream(_launcherFolderIconPngBytes, writable: false);
            _launcherFolderIconBitmap = new Bitmap(stream);
            return _launcherFolderIconBitmap;
        }
        catch
        {
            _launcherFolderIconBitmap = null;
            return null;
        }
    }

    private static string BuildMonogram(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "?";
        }

        var letters = text.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(part => part[0]).Take(2).ToArray();
        return letters.Length == 0 ? "?" : new string(letters).ToUpperInvariant();
    }
}
