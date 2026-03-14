using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LanMountainDesktop.Models;
using LanMountainDesktop.Services;
using LanMountainDesktop.Services.Settings;
using LanMountainDesktop.Views.Components;

namespace LanMountainDesktop.ViewModels;

public sealed partial class WeatherSettingsPageViewModel : ViewModelBase
{
    private readonly ISettingsFacadeService _settingsFacade;
    private readonly LocalizationService _localizationService;
    private readonly ILocationService _locationService;
    private readonly WeatherLocationRefreshService _weatherLocationRefreshService;
    private string _languageCode;
    private bool _isInitializing;

    public WeatherSettingsPageViewModel(
        ISettingsFacadeService settingsFacade,
        LocalizationService localizationService,
        ILocationService locationService,
        WeatherLocationRefreshService weatherLocationRefreshService)
    {
        _settingsFacade = settingsFacade ?? throw new ArgumentNullException(nameof(settingsFacade));
        _localizationService = localizationService ?? throw new ArgumentNullException(nameof(localizationService));
        _locationService = locationService ?? throw new ArgumentNullException(nameof(locationService));
        _weatherLocationRefreshService = weatherLocationRefreshService ?? throw new ArgumentNullException(nameof(weatherLocationRefreshService));

        var regionState = _settingsFacade.Region.Get();
        _languageCode = _localizationService.NormalizeLanguageCode(regionState.LanguageCode);

        RefreshLocalizedText();
        LocationModes = CreateLocationModes();

        var weatherState = _settingsFacade.Weather.Get();
        SearchKeyword = weatherState.LocationQuery;
        SelectedLocationMode = LocationModes.FirstOrDefault(option =>
                                   string.Equals(option.Value, weatherState.LocationMode, StringComparison.OrdinalIgnoreCase))
                               ?? LocationModes[0];

        _isInitializing = true;
        Latitude = weatherState.Latitude;
        Longitude = weatherState.Longitude;
        LocationKey = weatherState.LocationKey;
        LocationName = weatherState.LocationName;
        AutoRefreshLocation = weatherState.AutoRefreshLocation;
        ExcludedAlerts = weatherState.ExcludedAlerts;
        NoTlsRequests = weatherState.NoTlsRequests;
        _isInitializing = false;

        IsLocationSupported = _locationService.IsSupported;
        UpdateModeVisibility();
        UpdateCurrentLocationSummary();
        LocationActionStatus = IsLocationSupported
            ? LocationReadyText
            : LocationUnsupportedText;

        _ = RefreshPreviewAsync();
    }

    public IReadOnlyList<SelectionOption> LocationModes { get; }

    public ObservableCollection<WeatherLocation> SearchResults { get; } = [];

    [ObservableProperty]
    private string _pageTitle = string.Empty;

    [ObservableProperty]
    private string _pageDescription = string.Empty;

    [ObservableProperty]
    private string _previewHeader = string.Empty;

    [ObservableProperty]
    private string _previewDescription = string.Empty;

    [ObservableProperty]
    private string _locationSourceHeader = string.Empty;

    [ObservableProperty]
    private string _locationSourceDescription = string.Empty;

    [ObservableProperty]
    private string _citySearchHeader = string.Empty;

    [ObservableProperty]
    private string _citySearchDescription = string.Empty;

    [ObservableProperty]
    private string _coordinatesHeader = string.Empty;

    [ObservableProperty]
    private string _coordinatesDescription = string.Empty;

    [ObservableProperty]
    private string _locationServicesHeader = string.Empty;

    [ObservableProperty]
    private string _locationServicesDescription = string.Empty;

    [ObservableProperty]
    private string _alertFilterHeader = string.Empty;

    [ObservableProperty]
    private string _alertFilterDescription = string.Empty;

    [ObservableProperty]
    private string _requestHeader = string.Empty;

    [ObservableProperty]
    private string _requestDescription = string.Empty;

    [ObservableProperty]
    private string _searchPlaceholder = string.Empty;

    [ObservableProperty]
    private string _searchButtonText = string.Empty;

    [ObservableProperty]
    private string _applyCityButtonText = string.Empty;

    [ObservableProperty]
    private string _refreshButtonText = string.Empty;

    [ObservableProperty]
    private string _applyCoordinatesButtonText = string.Empty;

    [ObservableProperty]
    private string _useCurrentLocationButtonText = string.Empty;

    [ObservableProperty]
    private string _autoRefreshLabel = string.Empty;

    [ObservableProperty]
    private string _latitudeLabel = string.Empty;

    [ObservableProperty]
    private string _longitudeLabel = string.Empty;

    [ObservableProperty]
    private string _locationKeyPlaceholder = string.Empty;

    [ObservableProperty]
    private string _locationNamePlaceholder = string.Empty;

    [ObservableProperty]
    private string _noTlsToggleText = string.Empty;

    [ObservableProperty]
    private string _locationUnsupportedText = string.Empty;

    [ObservableProperty]
    private string _locationReadyText = string.Empty;

    [ObservableProperty]
    private string _locationRefreshingText = string.Empty;

    [ObservableProperty]
    private string _footerHint = string.Empty;

    [ObservableProperty]
    private SelectionOption _selectedLocationMode = new("CitySearch", "City Search");

    [ObservableProperty]
    private bool _isCitySearchMode = true;

    [ObservableProperty]
    private bool _isCoordinatesMode;

    [ObservableProperty]
    private bool _isLocationSupported;

    [ObservableProperty]
    private string _searchKeyword = string.Empty;

    [ObservableProperty]
    private WeatherLocation? _selectedSearchResult;

    [ObservableProperty]
    private string _searchStatus = string.Empty;

    [ObservableProperty]
    private bool _isSearching;

    [ObservableProperty]
    private double _latitude;

    [ObservableProperty]
    private double _longitude;

    [ObservableProperty]
    private string _locationKey = string.Empty;

    [ObservableProperty]
    private string _locationName = string.Empty;

    [ObservableProperty]
    private bool _autoRefreshLocation;

    [ObservableProperty]
    private string _excludedAlerts = string.Empty;

    [ObservableProperty]
    private bool _noTlsRequests;

    [ObservableProperty]
    private string _currentLocationSummary = string.Empty;

    [ObservableProperty]
    private string _locationActionStatus = string.Empty;

    [ObservableProperty]
    private bool _isRefreshingLocation;

    [ObservableProperty]
    private bool _isRefreshingPreview;

    [ObservableProperty]
    private IImage? _previewIcon;

    [ObservableProperty]
    private string _previewLocation = string.Empty;

    [ObservableProperty]
    private string _previewTemperature = string.Empty;

    [ObservableProperty]
    private string _previewCondition = string.Empty;

    [ObservableProperty]
    private string _previewUpdated = string.Empty;

    [ObservableProperty]
    private string _previewStatus = string.Empty;

    partial void OnSelectedLocationModeChanged(SelectionOption value)
    {
        UpdateModeVisibility();
        UpdateCurrentLocationSummary();
        if (_isInitializing || value is null)
        {
            return;
        }

        _settingsFacade.Weather.Save(CreateEditableState(value.Value));
        _ = RefreshPreviewAsync();
    }

    partial void OnAutoRefreshLocationChanged(bool value)
    {
        _ = value;
        if (_isInitializing)
        {
            return;
        }

        _settingsFacade.Weather.Save(CreateEditableState());
    }

    partial void OnExcludedAlertsChanged(string value)
    {
        _ = value;
        if (_isInitializing)
        {
            return;
        }

        _settingsFacade.Weather.Save(CreateEditableState());
    }

    partial void OnNoTlsRequestsChanged(bool value)
    {
        _ = value;
        if (_isInitializing)
        {
            return;
        }

        _settingsFacade.Weather.Save(CreateEditableState());
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        SearchStatus = string.Empty;
        SearchResults.Clear();
        SelectedSearchResult = null;

        if (string.IsNullOrWhiteSpace(SearchKeyword))
        {
            SearchStatus = L("settings.weather.search_required", "Please enter a city keyword first.");
            return;
        }

        IsSearching = true;
        try
        {
            var result = await _settingsFacade.Weather.SearchLocationsAsync(
                SearchKeyword.Trim(),
                NormalizeWeatherLocale(_languageCode));
            if (!result.Success)
            {
                SearchStatus = string.Format(
                    ResolveCulture(),
                    L("settings.weather.search_failed_format", "Search failed: {0}"),
                    result.ErrorMessage ?? result.ErrorCode ?? L("settings.weather.preview_unknown", "Unknown"));
                return;
            }

            foreach (var item in result.Data ?? [])
            {
                SearchResults.Add(item);
            }

            SearchStatus = SearchResults.Count == 0
                ? L("settings.weather.search_no_results", "No locations were found.")
                : string.Format(
                    ResolveCulture(),
                    L("settings.weather.search_result_count_format", "Found {0} locations."),
                    SearchResults.Count);

            SelectedSearchResult = SearchResults.FirstOrDefault();
        }
        finally
        {
            IsSearching = false;
        }
    }

    [RelayCommand]
    private async Task ApplyCitySelectionAsync()
    {
        if (SelectedSearchResult is null)
        {
            SearchStatus = L("settings.weather.search_select_required", "Please select one location from search results.");
            return;
        }

        var selected = SelectedSearchResult;
        var nextState = new WeatherSettingsState(
            "CitySearch",
            selected.LocationKey,
            selected.Name,
            selected.Latitude,
            selected.Longitude,
            AutoRefreshLocation,
            ExcludedAlerts ?? string.Empty,
            "HyperOS3",
            NoTlsRequests,
            SearchKeyword?.Trim() ?? string.Empty);

        ApplySavedState(nextState);
        SearchStatus = string.Format(
            ResolveCulture(),
            L("settings.weather.search_applied_format", "Location applied: {0}"),
            selected.Name);
        await RefreshPreviewAsync();
    }

    [RelayCommand]
    private async Task ApplyCoordinatesAsync()
    {
        var nextState = CreateEditableState("Coordinates");
        _settingsFacade.Weather.Save(nextState);
        ApplySavedState(nextState, save: false);
        SearchStatus = string.Format(
            ResolveCulture(),
            L("settings.weather.coordinates_saved_format", "Coordinates saved: {0:F4}, {1:F4}"),
            nextState.Latitude,
            nextState.Longitude);
        await RefreshPreviewAsync();
    }

    [RelayCommand]
    private async Task UseCurrentLocationAsync()
    {
        if (!IsLocationSupported)
        {
            LocationActionStatus = LocationUnsupportedText;
            return;
        }

        IsRefreshingLocation = true;
        LocationActionStatus = LocationRefreshingText;
        try
        {
            var result = await _weatherLocationRefreshService.RefreshCurrentLocationAsync();
            if (!result.Success || result.AppliedState is null)
            {
                LocationActionStatus = string.Format(
                    ResolveCulture(),
                    L("settings.weather.location_refresh_failed_format", "Failed to get current location: {0}"),
                    result.ErrorMessage ?? result.LocationResult?.FailureReason.ToString() ?? L("settings.weather.preview_unknown", "Unknown"));
                return;
            }

            ApplySavedState(result.AppliedState, save: false);
            LocationActionStatus = string.Format(
                ResolveCulture(),
                L("settings.weather.location_refresh_success_format", "Current location applied: {0}"),
                result.AppliedState.LocationName);
            await RefreshPreviewAsync();
        }
        finally
        {
            IsRefreshingLocation = false;
        }
    }

    [RelayCommand]
    private async Task RefreshPreviewAsync()
    {
        IsRefreshingPreview = true;
        try
        {
            var state = ResolvePreviewState();
            if (string.IsNullOrWhiteSpace(state.LocationKey))
            {
                PreviewStatus = L("settings.weather.preview_missing_location", "Please apply one weather location before testing.");
                PreviewIcon = null;
                PreviewLocation = CurrentLocationSummary;
                PreviewTemperature = "--";
                PreviewCondition = string.Empty;
                PreviewUpdated = string.Empty;
                return;
            }

            var result = await _settingsFacade.Weather.GetWeatherInfoService().GetWeatherAsync(
                new WeatherQuery(
                    state.LocationKey,
                    state.Latitude,
                    state.Longitude,
                    ForecastDays: 3,
                    Locale: NormalizeWeatherLocale(_languageCode),
                    ForceRefresh: true));
            if (!result.Success || result.Data is null)
            {
                PreviewStatus = string.Format(
                    ResolveCulture(),
                    L("settings.weather.preview_failed_format", "Test fetch failed: {0}"),
                    result.ErrorMessage ?? result.ErrorCode ?? L("settings.weather.preview_unknown", "Unknown"));
                PreviewIcon = null;
                return;
            }

            var snapshot = result.Data;
            var isNight = snapshot.Current.IsDaylight.HasValue
                ? !snapshot.Current.IsDaylight.Value
                : _settingsFacade.Theme.Get().IsNightMode;
            var visualKind = HyperOS3WeatherTheme.ResolveVisualKind(snapshot.Current.WeatherCode, isNight);
            PreviewIcon = HyperOS3WeatherAssetLoader.LoadImage(HyperOS3WeatherTheme.ResolveHeroIconAsset(visualKind));
            PreviewLocation = string.IsNullOrWhiteSpace(snapshot.LocationName)
                ? state.LocationName
                : snapshot.LocationName!;
            PreviewTemperature = snapshot.Current.TemperatureC.HasValue
                ? string.Format(CultureInfo.InvariantCulture, "{0:0.#}°C", snapshot.Current.TemperatureC.Value)
                : "--";
            PreviewCondition = snapshot.Current.WeatherText ?? L("settings.weather.preview_unknown", "Unknown");

            var updatedAt = (snapshot.ObservationTime ?? snapshot.FetchedAt).ToLocalTime();
            PreviewUpdated = string.Format(
                ResolveCulture(),
                L("settings.weather.preview_updated_format", "Updated {0}"),
                updatedAt.ToString("g", ResolveCulture()));
            PreviewStatus = string.Format(
                ResolveCulture(),
                L("settings.weather.preview_success_format", "Test success: {0} · {1} · {2}"),
                PreviewLocation,
                PreviewTemperature,
                PreviewCondition);
        }
        finally
        {
            IsRefreshingPreview = false;
        }
    }

    private void RefreshLocalizedText()
    {
        PageTitle = L("settings.weather.title", "Weather");
        PageDescription = L("settings.weather.description", "Configure weather location, automatic positioning, and Xiaomi weather preview.");
        PreviewHeader = L("settings.weather.preview_panel_header", "Weather Preview");
        PreviewDescription = L("settings.weather.preview_panel_desc", "Refresh and verify current weather service status.");
        LocationSourceHeader = L("settings.weather.location_source_header", "Location Source");
        LocationSourceDescription = L("settings.weather.location_source_desc", "Choose how weather widgets resolve location.");
        CitySearchHeader = L("settings.weather.city_search_header", "City Search");
        CitySearchDescription = L("settings.weather.city_search_desc", "Search cities and apply one weather location.");
        CoordinatesHeader = L("settings.weather.coordinates_header", "Coordinates");
        CoordinatesDescription = L("settings.weather.coordinates_desc", "Set latitude/longitude and optional key/name.");
        LocationServicesHeader = L("settings.weather.location_services_header", "Location Service");
        LocationServicesDescription = L("settings.weather.location_services_desc", "Use the current Windows location and decide whether it refreshes automatically at startup.");
        AlertFilterHeader = L("settings.weather.alert_filter_header", "Excluded Alerts");
        AlertFilterDescription = L("settings.weather.alert_filter_desc", "Alerts containing these words will not be shown. One rule per line.");
        RequestHeader = L("settings.weather.no_tls_header", "No TLS Weather Request");
        RequestDescription = L("settings.weather.no_tls_desc", "Not recommended. Enable only for incompatible network environments.");
        SearchPlaceholder = L("settings.weather.search_placeholder", "e.g. Beijing");
        SearchButtonText = L("settings.weather.search_button", "Search");
        ApplyCityButtonText = L("settings.weather.apply_city_button", "Apply City");
        RefreshButtonText = L("settings.weather.refresh_button", "Refresh");
        ApplyCoordinatesButtonText = L("settings.weather.apply_coordinates_button", "Apply Coordinates");
        UseCurrentLocationButtonText = L("settings.weather.use_current_location", "Use Current Location");
        AutoRefreshLabel = L("settings.weather.auto_refresh", "Auto refresh location on startup");
        LatitudeLabel = L("settings.weather.latitude_label", "Latitude");
        LongitudeLabel = L("settings.weather.longitude_label", "Longitude");
        LocationKeyPlaceholder = L("settings.weather.location_key_placeholder", "Location key (optional)");
        LocationNamePlaceholder = L("settings.weather.location_name_placeholder", "Display name (optional)");
        NoTlsToggleText = L("settings.weather.no_tls_toggle", "Allow non-TLS request fallback");
        LocationUnsupportedText = L("settings.weather.location_unsupported", "Current platform does not support retrieving the current location.");
        LocationReadyText = L("settings.weather.location_ready", "You can use the current Windows location.");
        LocationRefreshingText = L("settings.weather.location_refreshing", "Requesting current location…");
        FooterHint = L("settings.weather.footer_hint", "Desktop weather widgets will reuse the location and alert exclusion settings configured here.");
    }

    private IReadOnlyList<SelectionOption> CreateLocationModes()
    {
        return
        [
            new SelectionOption("CitySearch", L("settings.weather.mode_city_search", "City Search")),
            new SelectionOption("Coordinates", L("settings.weather.mode_coordinates", "Coordinates"))
        ];
    }

    private void UpdateModeVisibility()
    {
        var mode = SelectedLocationMode?.Value ?? "CitySearch";
        IsCitySearchMode = string.Equals(mode, "CitySearch", StringComparison.OrdinalIgnoreCase);
        IsCoordinatesMode = string.Equals(mode, "Coordinates", StringComparison.OrdinalIgnoreCase);
    }

    private void UpdateCurrentLocationSummary()
    {
        var state = CreateEditableState();
        var modeLabel = SelectedLocationMode?.Label ?? state.LocationMode;
        if (string.Equals(state.LocationMode, "CitySearch", StringComparison.OrdinalIgnoreCase))
        {
            CurrentLocationSummary = string.IsNullOrWhiteSpace(state.LocationKey)
                ? L("settings.weather.status_city_empty", "No city location is configured.")
                : string.Format(
                    ResolveCulture(),
                    L("settings.weather.status_city_format", "Mode: {0} | {1} | Key: {2}"),
                    modeLabel,
                    string.IsNullOrWhiteSpace(state.LocationName) ? L("settings.weather.location_not_selected", "No location selected") : state.LocationName,
                    state.LocationKey);
            return;
        }

        CurrentLocationSummary = string.Format(
            ResolveCulture(),
            L("settings.weather.status_coordinates_format", "Mode: {0} | Lat {1:F4}, Lon {2:F4} | Key: {3}"),
            modeLabel,
            state.Latitude,
            state.Longitude,
            state.LocationKey);
    }

    private WeatherSettingsState CreateEditableState(string? locationMode = null)
    {
        var mode = locationMode ?? SelectedLocationMode?.Value ?? "CitySearch";
        var locationKey = LocationKey?.Trim() ?? string.Empty;
        var locationName = LocationName?.Trim() ?? string.Empty;

        if (string.Equals(mode, "Coordinates", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(locationKey))
            {
                locationKey = BuildCoordinateKey(Latitude, Longitude);
            }

            if (string.IsNullOrWhiteSpace(locationName))
            {
                locationName = BuildCoordinateDisplayName(Latitude, Longitude);
            }
        }

        return new WeatherSettingsState(
            mode,
            locationKey,
            locationName,
            Latitude,
            Longitude,
            AutoRefreshLocation,
            ExcludedAlerts ?? string.Empty,
            "HyperOS3",
            NoTlsRequests,
            SearchKeyword?.Trim() ?? string.Empty);
    }

    private WeatherSettingsState ResolvePreviewState()
    {
        if (IsCitySearchMode && SelectedSearchResult is not null)
        {
            return new WeatherSettingsState(
                "CitySearch",
                SelectedSearchResult.LocationKey,
                SelectedSearchResult.Name,
                SelectedSearchResult.Latitude,
                SelectedSearchResult.Longitude,
                AutoRefreshLocation,
                ExcludedAlerts ?? string.Empty,
                "HyperOS3",
                NoTlsRequests,
                SearchKeyword?.Trim() ?? string.Empty);
        }

        return CreateEditableState();
    }

    private void ApplySavedState(WeatherSettingsState state, bool save = true)
    {
        if (save)
        {
            _settingsFacade.Weather.Save(state);
        }

        _isInitializing = true;
        SelectedLocationMode = LocationModes.FirstOrDefault(option =>
                                   string.Equals(option.Value, state.LocationMode, StringComparison.OrdinalIgnoreCase))
                               ?? LocationModes[0];
        Latitude = state.Latitude;
        Longitude = state.Longitude;
        LocationKey = state.LocationKey;
        LocationName = state.LocationName;
        AutoRefreshLocation = state.AutoRefreshLocation;
        ExcludedAlerts = state.ExcludedAlerts;
        NoTlsRequests = state.NoTlsRequests;
        SearchKeyword = state.LocationQuery;
        _isInitializing = false;

        UpdateModeVisibility();
        UpdateCurrentLocationSummary();
    }

    private string BuildCoordinateDisplayName(double latitude, double longitude)
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            L("settings.weather.coordinates_default_name_format", "Coordinate {0:F4}, {1:F4}"),
            latitude,
            longitude);
    }

    private static string BuildCoordinateKey(double latitude, double longitude)
    {
        return FormattableString.Invariant($"coord:{latitude:F4},{longitude:F4}");
    }

    private string L(string key, string fallback)
    {
        return _localizationService.GetString(_languageCode, key, fallback);
    }

    private CultureInfo ResolveCulture()
    {
        try
        {
            return CultureInfo.GetCultureInfo(_languageCode);
        }
        catch (CultureNotFoundException)
        {
            return CultureInfo.InvariantCulture;
        }
    }

    private static string NormalizeWeatherLocale(string? languageCode)
    {
        return string.Equals(languageCode, "en-US", StringComparison.OrdinalIgnoreCase)
            ? "en_us"
            : "zh_cn";
    }
}
