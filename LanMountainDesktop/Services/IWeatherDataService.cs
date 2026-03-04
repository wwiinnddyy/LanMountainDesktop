using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LanMountainDesktop.Models;

namespace LanMountainDesktop.Services;

public sealed record WeatherQuery(
    string LocationKey,
    double Latitude,
    double Longitude,
    int ForecastDays = 7,
    string? Locale = null,
    bool? IsGlobal = null,
    bool ForceRefresh = false);

public sealed record WeatherQueryResult<T>(
    bool Success,
    T? Data,
    string? ErrorCode = null,
    string? ErrorMessage = null)
{
    public static WeatherQueryResult<T> Ok(T data)
    {
        return new WeatherQueryResult<T>(true, data);
    }

    public static WeatherQueryResult<T> Fail(string errorCode, string errorMessage)
    {
        return new WeatherQueryResult<T>(false, default, errorCode, errorMessage);
    }
}

public interface IWeatherInfoService
{
    Task<WeatherQueryResult<WeatherSnapshot>> GetWeatherAsync(WeatherQuery query, CancellationToken cancellationToken = default);
}

public interface IWeatherDataService : IWeatherInfoService
{

    Task<WeatherQueryResult<IReadOnlyList<WeatherLocation>>> SearchLocationsAsync(
        string keyword,
        string? locale = null,
        CancellationToken cancellationToken = default);

    void ClearCache();
}
