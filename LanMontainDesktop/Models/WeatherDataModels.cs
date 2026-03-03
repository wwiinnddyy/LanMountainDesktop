using System;
using System.Collections.Generic;

namespace LanMontainDesktop.Models;

public sealed record WeatherLocation(
    string Name,
    string LocationKey,
    double Latitude,
    double Longitude,
    string? Affiliation = null);

public sealed record WeatherCurrentCondition(
    double? TemperatureC,
    double? FeelsLikeC,
    int? RelativeHumidityPercent,
    int? AirQualityIndex,
    double? WindSpeedKph,
    double? WindDirectionDegree,
    int? WeatherCode,
    bool? IsDaylight,
    string? WeatherText);

public sealed record WeatherDailyForecast(
    DateOnly Date,
    double? LowTemperatureC,
    double? HighTemperatureC,
    int? DayWeatherCode,
    string? DayWeatherText,
    int? NightWeatherCode,
    string? NightWeatherText,
    string? SunriseTime,
    string? SunsetTime,
    int? PrecipitationProbabilityPercent);

public sealed record WeatherHourlyForecast(
    DateTimeOffset Time,
    double? TemperatureC,
    int? WeatherCode,
    string? WeatherText);

public sealed record WeatherSnapshot(
    string Provider,
    string LocationKey,
    string? LocationName,
    double? Latitude,
    double? Longitude,
    DateTimeOffset FetchedAt,
    DateTimeOffset? ObservationTime,
    WeatherCurrentCondition Current,
    IReadOnlyList<WeatherDailyForecast> DailyForecasts,
    IReadOnlyList<WeatherHourlyForecast> HourlyForecasts);
