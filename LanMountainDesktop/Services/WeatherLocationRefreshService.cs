using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using LanMountainDesktop.Models;
using LanMountainDesktop.Services.Settings;

namespace LanMountainDesktop.Services;

public sealed record WeatherLocationRefreshResult(
    bool Success,
    bool IsSupported,
    WeatherSettingsState? AppliedState = null,
    WeatherLocation? ResolvedLocation = null,
    LocationRequestResult? LocationResult = null,
    string? ErrorMessage = null)
{
    public static WeatherLocationRefreshResult Unsupported(LocationRequestResult result)
        => new(false, false, null, null, result, result.ErrorMessage);

    public static WeatherLocationRefreshResult Fail(LocationRequestResult? locationResult, string? errorMessage)
        => new(false, locationResult?.IsSupported ?? true, null, null, locationResult, errorMessage);

    public static WeatherLocationRefreshResult Ok(
        WeatherSettingsState state,
        WeatherLocation? resolvedLocation,
        LocationRequestResult locationResult)
        => new(true, true, state, resolvedLocation, locationResult, null);
}

public sealed class WeatherLocationRefreshService
{
    private readonly ISettingsFacadeService _settingsFacade;
    private readonly ILocationService _locationService;
    private readonly LocalizationService _localizationService;

    public WeatherLocationRefreshService(
        ISettingsFacadeService settingsFacade,
        ILocationService locationService,
        LocalizationService localizationService)
    {
        _settingsFacade = settingsFacade ?? throw new ArgumentNullException(nameof(settingsFacade));
        _locationService = locationService ?? throw new ArgumentNullException(nameof(locationService));
        _localizationService = localizationService ?? throw new ArgumentNullException(nameof(localizationService));
    }

    public bool IsSupported => _locationService.IsSupported;

    public async Task<WeatherLocationRefreshResult> RefreshCurrentLocationAsync(CancellationToken cancellationToken = default)
    {
        var locationResult = await _locationService.TryGetCurrentLocationAsync(cancellationToken);
        if (!locationResult.IsSupported)
        {
            return WeatherLocationRefreshResult.Unsupported(locationResult);
        }

        if (!locationResult.Success || locationResult.Coordinate is null)
        {
            return WeatherLocationRefreshResult.Fail(locationResult, locationResult.ErrorMessage);
        }

        var coordinate = locationResult.Coordinate.Value;
        var settingsState = _settingsFacade.Weather.Get();
        var languageCode = _settingsFacade.Region.Get().LanguageCode;
        var locale = NormalizeWeatherLocale(languageCode);

        WeatherLocation? resolvedLocation = null;
        var weatherService = _settingsFacade.Weather.GetWeatherInfoService();
        var resolvedResult = await weatherService.ResolveLocationAsync(
            coordinate.Latitude,
            coordinate.Longitude,
            locale,
            cancellationToken);
        if (resolvedResult.Success && resolvedResult.Data is not null)
        {
            resolvedLocation = resolvedResult.Data;
        }

        var locationKey = resolvedLocation?.LocationKey?.Trim();
        if (string.IsNullOrWhiteSpace(locationKey))
        {
            locationKey = BuildCoordinateKey(coordinate.Latitude, coordinate.Longitude);
        }

        var locationName = resolvedLocation?.Name?.Trim();
        if (string.IsNullOrWhiteSpace(locationName))
        {
            locationName = BuildCoordinateDisplayName(languageCode, coordinate.Latitude, coordinate.Longitude);
        }

        var nextState = settingsState with
        {
            LocationMode = "Coordinates",
            LocationKey = locationKey,
            LocationName = locationName,
            Latitude = Math.Round(coordinate.Latitude, 6),
            Longitude = Math.Round(coordinate.Longitude, 6),
            LocationQuery = resolvedLocation?.Name?.Trim() ?? settingsState.LocationQuery,
            IconPackId = NormalizeIconPackId(settingsState.IconPackId)
        };

        _settingsFacade.Weather.Save(nextState);
        return WeatherLocationRefreshResult.Ok(nextState, resolvedLocation, locationResult);
    }

    public async Task<bool> TryRefreshOnStartupAsync(CancellationToken cancellationToken = default)
    {
        var state = _settingsFacade.Weather.Get();
        var isCoordinatesMode = string.Equals(state.LocationMode, "Coordinates", StringComparison.OrdinalIgnoreCase);
        if (!isCoordinatesMode || !state.AutoRefreshLocation)
        {
            return false;
        }

        var result = await RefreshCurrentLocationAsync(cancellationToken);
        if (!result.Success)
        {
            AppLogger.Warn(
                "Weather.Location",
                $"Automatic weather location refresh failed. Reason='{result.LocationResult?.FailureReason}'. Message='{result.ErrorMessage ?? "<none>"}'.");
        }

        return result.Success;
    }

    private static string NormalizeIconPackId(string? iconPackId)
    {
        return string.IsNullOrWhiteSpace(iconPackId)
            ? "HyperOS3"
            : "HyperOS3";
    }

    private string BuildCoordinateDisplayName(string? languageCode, double latitude, double longitude)
    {
        var normalizedLanguage = _localizationService.NormalizeLanguageCode(languageCode);
        var format = _localizationService.GetString(
            normalizedLanguage,
            "settings.weather.coordinates_default_name_format",
            "Coordinate {0:F4}, {1:F4}");
        return string.Format(
            CultureInfo.InvariantCulture,
            format,
            latitude,
            longitude);
    }

    private static string BuildCoordinateKey(double latitude, double longitude)
    {
        return FormattableString.Invariant($"coord:{latitude:F4},{longitude:F4}");
    }

    private static string NormalizeWeatherLocale(string? languageCode)
    {
        return string.Equals(languageCode, "en-US", StringComparison.OrdinalIgnoreCase)
            ? "en_us"
            : "zh_cn";
    }
}
