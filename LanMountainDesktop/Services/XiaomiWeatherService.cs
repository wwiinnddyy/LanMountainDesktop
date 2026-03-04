using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LanMountainDesktop.Models;

namespace LanMountainDesktop.Services;

public sealed record XiaomiWeatherApiOptions
{
    public string BaseUrl { get; init; } = "https://weatherapi.market.xiaomi.com";

    public string WeatherAllPath { get; init; } = "/wtr-v3/weather/all";

    public string CitySearchPath { get; init; } = "/wtr-v3/location/city/search";

    public string AppKey { get; init; } = "weather20151024";

    public string Sign { get; init; } = "zUFJoAR2ZVrDy1vF3D07";

    public string Source { get; init; } = "xiaomi";

    public string Locale { get; init; } = "zh_cn";

    public bool IsGlobal { get; init; }

    public TimeSpan CacheDuration { get; init; } = TimeSpan.FromMinutes(10);

    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(8);
}

public sealed class XiaomiWeatherService : IWeatherDataService, IDisposable
{
    private sealed record CacheEntry(WeatherSnapshot Snapshot, DateTimeOffset ExpireAt);

    private static readonly IReadOnlyDictionary<int, string> ZhWeatherDescriptions = new Dictionary<int, string>
    {
        [0] = "\u6674",
        [1] = "\u591a\u4e91",
        [2] = "\u9634",
        [3] = "\u9635\u96e8",
        [4] = "\u96f7\u9635\u96e8",
        [7] = "\u5c0f\u96e8",
        [8] = "\u4e2d\u96e8",
        [9] = "\u5927\u96e8",
        [13] = "\u9635\u96ea",
        [14] = "\u5c0f\u96ea",
        [15] = "\u4e2d\u96ea",
        [16] = "\u5927\u96ea",
        [18] = "\u96fe",
        [32] = "\u973e"
    };

    private static readonly IReadOnlyDictionary<int, string> EnWeatherDescriptions = new Dictionary<int, string>
    {
        [0] = "Clear",
        [1] = "Partly Cloudy",
        [2] = "Cloudy",
        [3] = "Shower",
        [4] = "Thunder Shower",
        [7] = "Light Rain",
        [8] = "Moderate Rain",
        [9] = "Heavy Rain",
        [13] = "Snow Flurry",
        [14] = "Light Snow",
        [15] = "Moderate Snow",
        [16] = "Heavy Snow",
        [18] = "Fog",
        [32] = "Haze"
    };

    private readonly XiaomiWeatherApiOptions _options;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly object _cacheGate = new();
    private readonly Dictionary<string, CacheEntry> _cache = new(StringComparer.OrdinalIgnoreCase);

    public XiaomiWeatherService(
        XiaomiWeatherApiOptions? options = null,
        HttpClient? httpClient = null)
    {
        _options = options ?? new XiaomiWeatherApiOptions();
        if (httpClient is null)
        {
            _httpClient = new HttpClient
            {
                Timeout = _options.RequestTimeout
            };
            _ownsHttpClient = true;
        }
        else
        {
            _httpClient = httpClient;
            _ownsHttpClient = false;
        }
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    public void ClearCache()
    {
        lock (_cacheGate)
        {
            _cache.Clear();
        }
    }

    public async Task<WeatherQueryResult<IReadOnlyList<WeatherLocation>>> SearchLocationsAsync(
        string keyword,
        string? locale = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return WeatherQueryResult<IReadOnlyList<WeatherLocation>>.Fail("invalid_keyword", "Keyword cannot be empty.");
        }

        var normalizedLocale = string.IsNullOrWhiteSpace(locale) ? _options.Locale : locale.Trim();
        var parameters = new Dictionary<string, string>
        {
            ["name"] = keyword.Trim(),
            ["locale"] = normalizedLocale
        };

        var requestUri = BuildUri(_options.CitySearchPath, parameters);
        string responseText;

        try
        {
            using var response = await _httpClient.GetAsync(requestUri, cancellationToken);
            responseText = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return WeatherQueryResult<IReadOnlyList<WeatherLocation>>.Fail(
                    "http_error",
                    $"HTTP {(int)response.StatusCode}: {Truncate(responseText, 180)}");
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return WeatherQueryResult<IReadOnlyList<WeatherLocation>>.Fail("network_error", ex.Message);
        }

        try
        {
            using var document = JsonDocument.Parse(responseText);
            var root = document.RootElement;
            if (TryGetProperty(root, out var dataNode, "data"))
            {
                root = dataNode;
            }

            var locations = ParseLocationArray(root);
            return WeatherQueryResult<IReadOnlyList<WeatherLocation>>.Ok(locations);
        }
        catch (Exception ex)
        {
            return WeatherQueryResult<IReadOnlyList<WeatherLocation>>.Fail("parse_error", ex.Message);
        }
    }

    public async Task<WeatherQueryResult<WeatherSnapshot>> GetWeatherAsync(
        WeatherQuery query,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query.LocationKey))
        {
            return WeatherQueryResult<WeatherSnapshot>.Fail("invalid_location", "LocationKey is required.");
        }

        var normalizedDays = Math.Clamp(query.ForecastDays, 1, 15);
        var normalizedLocale = string.IsNullOrWhiteSpace(query.Locale) ? _options.Locale : query.Locale.Trim();
        var isGlobal = query.IsGlobal ?? _options.IsGlobal;
        var cacheKey = BuildCacheKey(query.LocationKey, query.Latitude, query.Longitude, normalizedDays, normalizedLocale, isGlobal);

        if (!query.ForceRefresh && TryGetCached(cacheKey, out var cached))
        {
            return WeatherQueryResult<WeatherSnapshot>.Ok(cached);
        }

        var parameters = new Dictionary<string, string>
        {
            ["locationKey"] = query.LocationKey.Trim(),
            ["latitude"] = query.Latitude.ToString("F6", CultureInfo.InvariantCulture),
            ["longitude"] = query.Longitude.ToString("F6", CultureInfo.InvariantCulture),
            ["days"] = normalizedDays.ToString(CultureInfo.InvariantCulture),
            ["appKey"] = _options.AppKey,
            ["sign"] = _options.Sign,
            ["locale"] = normalizedLocale,
            ["isGlobal"] = isGlobal ? "true" : "false",
            ["ts"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)
        };

        if (!string.IsNullOrWhiteSpace(_options.Source))
        {
            parameters["source"] = _options.Source;
        }

        var requestUri = BuildUri(_options.WeatherAllPath, parameters);
        string responseText;

        try
        {
            using var response = await _httpClient.GetAsync(requestUri, cancellationToken);
            responseText = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return WeatherQueryResult<WeatherSnapshot>.Fail(
                    "http_error",
                    $"HTTP {(int)response.StatusCode}: {Truncate(responseText, 220)}");
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return WeatherQueryResult<WeatherSnapshot>.Fail("network_error", ex.Message);
        }

        try
        {
            using var document = JsonDocument.Parse(responseText);
            var snapshot = ParseWeatherSnapshot(
                document.RootElement,
                query.LocationKey.Trim(),
                query.Latitude,
                query.Longitude,
                normalizedDays,
                normalizedLocale);

            SetCache(cacheKey, snapshot);
            return WeatherQueryResult<WeatherSnapshot>.Ok(snapshot);
        }
        catch (Exception ex)
        {
            return WeatherQueryResult<WeatherSnapshot>.Fail("parse_error", ex.Message);
        }
    }

    private static IReadOnlyList<WeatherLocation> ParseLocationArray(JsonElement root)
    {
        var results = new List<WeatherLocation>();
        if (!TryResolveLocationArray(root, out var locationArray))
        {
            return results;
        }

        foreach (var item in locationArray.EnumerateArray())
        {
            var locationKey = ReadString(item, "locationKey") ??
                              ReadString(item, "key") ??
                              ReadString(item, "id");
            if (string.IsNullOrWhiteSpace(locationKey))
            {
                continue;
            }

            var name = ReadString(item, "name") ??
                       ReadString(item, "city") ??
                       locationKey;
            var affiliation = ReadString(item, "affiliation") ?? ReadString(item, "province");

            var latitude = ReadDouble(item, "latitude") ?? 0;
            var longitude = ReadDouble(item, "longitude") ?? 0;

            results.Add(new WeatherLocation(name, locationKey, latitude, longitude, affiliation));
        }

        return results;
    }

    private WeatherSnapshot ParseWeatherSnapshot(
        JsonElement root,
        string locationKey,
        double latitude,
        double longitude,
        int days,
        string locale)
    {
        var payload = root;
        if (TryGetProperty(payload, out var dataNode, "data"))
        {
            payload = dataNode;
        }

        var errorCode = ReadInt(root, "code");
        if (errorCode.HasValue && errorCode.Value is not (0 or 200))
        {
            var message = ReadString(root, "description") ??
                          ReadString(root, "msg") ??
                          $"Weather API returned error code {errorCode.Value}.";
            throw new InvalidOperationException(message);
        }

        var currentNode = TryGetNode(payload, "current") ?? payload;
        var cityNode = TryGetNode(payload, "city");
        var dailyNode = TryGetNode(payload, "forecastDaily") ?? TryGetNode(payload, "daily");
        var hourlyNode = TryGetNode(payload, "forecastHourly") ??
                         TryGetNode(payload, "hourly") ??
                         TryGetNode(payload, "hourlyForecast");

        var weatherCode = ReadInt(currentNode, "weather", "value") ??
                          ReadInt(currentNode, "weatherCode") ??
                          ReadInt(currentNode, "code");

        var weatherText = ReadString(currentNode, "weather", "desc") ??
                          ReadString(currentNode, "weather", "text") ??
                          ResolveWeatherDescription(weatherCode, locale);

        var current = new WeatherCurrentCondition(
            TemperatureC: ReadDouble(currentNode, "temperature", "value") ?? ReadDouble(currentNode, "temperature"),
            FeelsLikeC: ReadDouble(currentNode, "feelsLike", "value") ?? ReadDouble(currentNode, "apparentTemperature", "value"),
            RelativeHumidityPercent: ReadInt(currentNode, "humidity", "value") ?? ReadInt(currentNode, "humidity"),
            AirQualityIndex: ReadInt(payload, "aqi", "value") ??
                             ReadInt(currentNode, "aqi", "value") ??
                             ReadInt(payload, "aqi", "index"),
            WindSpeedKph: ReadDouble(currentNode, "wind", "speed", "value") ??
                          ReadDouble(currentNode, "windSpeed", "value"),
            WindDirectionDegree: ReadDouble(currentNode, "wind", "angle", "value") ??
                                 ReadDouble(currentNode, "wind", "direction", "value"),
            WeatherCode: weatherCode,
            IsDaylight: ReadBool(currentNode, "daylight", "value") ??
                        ReadBool(currentNode, "daylight") ??
                        ReadBool(currentNode, "isDaylight") ??
                        ReadBool(currentNode, "isDay") ??
                        ReadBool(currentNode, "day") ??
                        ReadBool(payload, "isDaylight"),
            WeatherText: weatherText);

        var forecasts = ParseDailyForecasts(dailyNode, days, locale);
        var hourlyForecasts = ParseHourlyForecasts(hourlyNode, locale);

        var locationName = ReadString(cityNode, "name") ??
                           ReadString(payload, "cityName") ??
                           ReadString(payload, "locationName");
        var observationTime = ParseTime(ReadString(currentNode, "pubTime")) ??
                              ParseTime(ReadString(payload, "pubTime")) ??
                              ParseTime(ReadString(payload, "serverTime"));

        return new WeatherSnapshot(
            Provider: "Xiaomi",
            LocationKey: locationKey,
            LocationName: locationName,
            Latitude: latitude,
            Longitude: longitude,
            FetchedAt: DateTimeOffset.UtcNow,
            ObservationTime: observationTime,
            Current: current,
            DailyForecasts: forecasts,
            HourlyForecasts: hourlyForecasts);
    }

    private IReadOnlyList<WeatherDailyForecast> ParseDailyForecasts(JsonElement? dailyNode, int days, string locale)
    {
        var forecasts = new List<WeatherDailyForecast>();
        if (!dailyNode.HasValue || dailyNode.Value.ValueKind != JsonValueKind.Object)
        {
            return forecasts;
        }

        var root = dailyNode.Value;
        var temperatureArray = ReadArray(root, "temperature", "value");
        var weatherArray = ReadArray(root, "weather", "value");
        var sunArray = ReadArray(root, "sunRiseSet", "value") ?? ReadArray(root, "sunriseSunset", "value");
        var precipitationArray = ReadArray(root, "precipitationProbability", "value");
        var dateArray = ReadArray(root, "date", "value") ?? ReadArray(root, "date");

        var count = Math.Max(
            Math.Max(temperatureArray?.GetArrayLength() ?? 0, weatherArray?.GetArrayLength() ?? 0),
            Math.Max(sunArray?.GetArrayLength() ?? 0, precipitationArray?.GetArrayLength() ?? 0));
        count = Math.Max(count, dateArray?.GetArrayLength() ?? 0);
        count = Math.Clamp(count, 0, days);

        for (var i = 0; i < count; i++)
        {
            var forecastDate = ResolveDateForIndex(dateArray, i);
            if (forecastDate is null)
            {
                forecastDate = DateOnly.FromDateTime(DateTime.Today.AddDays(i));
            }

            var tempItem = GetArrayItem(temperatureArray, i);
            var weatherItem = GetArrayItem(weatherArray, i);
            var sunItem = GetArrayItem(sunArray, i);
            var precipitationItem = GetArrayItem(precipitationArray, i);

            var dayCode = ReadInt(weatherItem, "from") ?? ReadInt(weatherItem, "day");
            var nightCode = ReadInt(weatherItem, "to") ?? ReadInt(weatherItem, "night");
            var dayText = ResolveWeatherDescription(dayCode, locale);
            var nightText = ResolveWeatherDescription(nightCode, locale);

            forecasts.Add(new WeatherDailyForecast(
                Date: forecastDate.Value,
                LowTemperatureC: ReadDouble(tempItem, "from") ?? ReadDouble(tempItem, "min"),
                HighTemperatureC: ReadDouble(tempItem, "to") ?? ReadDouble(tempItem, "max"),
                DayWeatherCode: dayCode,
                DayWeatherText: dayText,
                NightWeatherCode: nightCode,
                NightWeatherText: nightText,
                SunriseTime: ReadString(sunItem, "from") ?? ReadString(sunItem, "sunrise"),
                SunsetTime: ReadString(sunItem, "to") ?? ReadString(sunItem, "sunset"),
                PrecipitationProbabilityPercent: ReadInt(precipitationItem, "from") ??
                                                 ReadInt(precipitationItem, "value") ??
                                                 ReadInt(precipitationItem, "probability")));
        }

        return forecasts;
    }

    private IReadOnlyList<WeatherHourlyForecast> ParseHourlyForecasts(JsonElement? hourlyNode, string locale)
    {
        var forecasts = new List<WeatherHourlyForecast>();
        if (!hourlyNode.HasValue)
        {
            return forecasts;
        }

        var root = hourlyNode.Value;
        if (root.ValueKind == JsonValueKind.Array)
        {
            ParseHourlyArray(root, locale, forecasts);
            return forecasts
                .OrderBy(item => item.Time)
                .Take(48)
                .ToList();
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            return forecasts;
        }

        var directArray =
            ReadArray(root, "value") ??
            ReadArray(root, "list") ??
            ReadArray(root, "hourly");

        if (directArray.HasValue && directArray.Value.ValueKind == JsonValueKind.Array)
        {
            ParseHourlyArray(directArray.Value, locale, forecasts);
        }

        var timeArray =
            ReadArray(root, "time", "value") ??
            ReadArray(root, "datetime", "value") ??
            ReadArray(root, "date", "value") ??
            ReadArray(root, "pubTime", "value");
        var tempArray =
            ReadArray(root, "temperature", "value") ??
            ReadArray(root, "temp", "value") ??
            ReadArray(root, "temperature");
        var weatherArray =
            ReadArray(root, "weather", "value") ??
            ReadArray(root, "weatherCode", "value") ??
            ReadArray(root, "weather");

        var count = Math.Max(
            timeArray?.GetArrayLength() ?? 0,
            Math.Max(
                tempArray?.GetArrayLength() ?? 0,
                weatherArray?.GetArrayLength() ?? 0));
        count = Math.Clamp(count, 0, 72);

        for (var i = 0; i < count; i++)
        {
            var timeItem = GetArrayItem(timeArray, i);
            var tempItem = GetArrayItem(tempArray, i);
            var weatherItem = GetArrayItem(weatherArray, i);

            var time = ParseTime(
                ReadString(timeItem, "value") ??
                ReadString(timeItem, "datetime") ??
                ReadString(timeItem, "time") ??
                ReadString(timeItem, "date") ??
                ReadString(timeItem));
            if (!time.HasValue)
            {
                continue;
            }

            var code = ReadInt(weatherItem, "value") ??
                       ReadInt(weatherItem, "code") ??
                       ReadInt(weatherItem, "weatherCode") ??
                       ReadInt(weatherItem, "from") ??
                       ReadInt(weatherItem);
            var weatherText = ReadString(weatherItem, "text") ??
                              ReadString(weatherItem, "desc") ??
                              ResolveWeatherDescription(code, locale);

            forecasts.Add(new WeatherHourlyForecast(
                Time: time.Value,
                TemperatureC: ReadDouble(tempItem, "value") ??
                              ReadDouble(tempItem, "temperature") ??
                              ReadDouble(tempItem, "temp") ??
                              ReadDouble(tempItem),
                WeatherCode: code,
                WeatherText: weatherText));
        }

        return forecasts
            .GroupBy(item => item.Time.ToUnixTimeSeconds())
            .Select(group => group.First())
            .OrderBy(item => item.Time)
            .Take(48)
            .ToList();
    }

    private void ParseHourlyArray(JsonElement array, string locale, ICollection<WeatherHourlyForecast> output)
    {
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind is not (JsonValueKind.Object or JsonValueKind.String or JsonValueKind.Number))
            {
                continue;
            }

            var time = ParseTime(
                ReadString(item, "datetime") ??
                ReadString(item, "time") ??
                ReadString(item, "date") ??
                ReadString(item, "forecastTime") ??
                ReadString(item, "pubTime") ??
                ReadString(item, "ts") ??
                ReadString(item));
            if (!time.HasValue)
            {
                continue;
            }

            var code = ReadInt(item, "weatherCode") ??
                       ReadInt(item, "code") ??
                       ReadInt(item, "weather", "value") ??
                       ReadInt(item, "weather") ??
                       ReadInt(item, "from");
            var weatherText = ReadString(item, "weatherText") ??
                              ReadString(item, "weather", "desc") ??
                              ReadString(item, "weather", "text") ??
                              ReadString(item, "desc") ??
                              ResolveWeatherDescription(code, locale);

            var temperature = ReadDouble(item, "temperature", "value") ??
                              ReadDouble(item, "temperature") ??
                              ReadDouble(item, "temp", "value") ??
                              ReadDouble(item, "temp") ??
                              ReadDouble(item, "value");

            output.Add(new WeatherHourlyForecast(
                Time: time.Value,
                TemperatureC: temperature,
                WeatherCode: code,
                WeatherText: weatherText));
        }
    }

    private static DateOnly? ResolveDateForIndex(JsonElement? dateArray, int index)
    {
        var item = GetArrayItem(dateArray, index);
        if (item is null)
        {
            return null;
        }

        if (item.Value.ValueKind == JsonValueKind.String)
        {
            var text = item.Value.GetString();
            if (DateOnly.TryParse(text, out var dateOnly))
            {
                return dateOnly;
            }

            if (DateTime.TryParse(text, out var dateTime))
            {
                return DateOnly.FromDateTime(dateTime);
            }
        }

        return null;
    }

    private bool TryGetCached(string key, out WeatherSnapshot snapshot)
    {
        lock (_cacheGate)
        {
            if (_cache.TryGetValue(key, out var entry))
            {
                if (entry.ExpireAt > DateTimeOffset.UtcNow)
                {
                    snapshot = entry.Snapshot;
                    return true;
                }

                _cache.Remove(key);
            }
        }

        snapshot = null!;
        return false;
    }

    private void SetCache(string key, WeatherSnapshot snapshot)
    {
        var expireAt = DateTimeOffset.UtcNow.Add(_options.CacheDuration);
        lock (_cacheGate)
        {
            _cache[key] = new CacheEntry(snapshot, expireAt);
        }
    }

    private static string BuildCacheKey(
        string locationKey,
        double latitude,
        double longitude,
        int days,
        string locale,
        bool isGlobal)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{locationKey.Trim()}|{latitude:F4}|{longitude:F4}|{days}|{locale}|{isGlobal}");
    }

    private Uri BuildUri(string path, IReadOnlyDictionary<string, string> query)
    {
        var baseUrl = _options.BaseUrl.TrimEnd('/');
        var requestPath = path.StartsWith("/", StringComparison.Ordinal) ? path : $"/{path}";

        var builder = new System.Text.StringBuilder(baseUrl.Length + requestPath.Length + 128);
        builder.Append(baseUrl);
        builder.Append(requestPath);

        var first = true;
        foreach (var pair in query)
        {
            if (string.IsNullOrWhiteSpace(pair.Key))
            {
                continue;
            }

            builder.Append(first ? '?' : '&');
            first = false;
            builder.Append(Uri.EscapeDataString(pair.Key));
            builder.Append('=');
            builder.Append(Uri.EscapeDataString(pair.Value ?? string.Empty));
        }

        return new Uri(builder.ToString(), UriKind.Absolute);
    }

    private static bool TryResolveLocationArray(JsonElement root, out JsonElement array)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            array = root;
            return true;
        }

        if (TryGetProperty(root, out array, "cities") && array.ValueKind == JsonValueKind.Array)
        {
            return true;
        }

        if (TryGetProperty(root, out array, "city") && array.ValueKind == JsonValueKind.Array)
        {
            return true;
        }

        if (TryGetProperty(root, out array, "location") && array.ValueKind == JsonValueKind.Array)
        {
            return true;
        }

        if (TryGetProperty(root, out var data) && data.ValueKind == JsonValueKind.Array)
        {
            array = data;
            return true;
        }

        array = default;
        return false;
    }

    private static bool TryGetProperty(JsonElement node, out JsonElement value, string propertyName = "data")
    {
        value = default;
        return node.ValueKind == JsonValueKind.Object &&
               node.TryGetProperty(propertyName, out value);
    }

    private static JsonElement? TryGetNode(JsonElement node, params string[] path)
    {
        var current = node;
        foreach (var segment in path)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out var next))
            {
                return null;
            }

            current = next;
        }

        return current;
    }

    private static JsonElement? ReadArray(JsonElement node, params string[] path)
    {
        var target = TryGetNode(node, path);
        if (target is null)
        {
            return null;
        }

        if (target.Value.ValueKind == JsonValueKind.Array)
        {
            return target.Value;
        }

        if (target.Value.ValueKind == JsonValueKind.Object &&
            target.Value.TryGetProperty("value", out var value) &&
            value.ValueKind == JsonValueKind.Array)
        {
            return value;
        }

        return null;
    }

    private static JsonElement? GetArrayItem(JsonElement? array, int index)
    {
        if (!array.HasValue || array.Value.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        if (index < 0 || index >= array.Value.GetArrayLength())
        {
            return null;
        }

        return array.Value[index];
    }

    private static string? ReadString(JsonElement? node, params string[] path)
    {
        if (!node.HasValue)
        {
            return null;
        }

        var target = path.Length == 0 ? node : TryGetNode(node.Value, path);
        if (!target.HasValue)
        {
            return null;
        }

        return target.Value.ValueKind switch
        {
            JsonValueKind.String => target.Value.GetString(),
            JsonValueKind.Number => target.Value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }

    private static int? ReadInt(JsonElement? node, params string[] path)
    {
        if (!node.HasValue)
        {
            return null;
        }

        var target = path.Length == 0 ? node : TryGetNode(node.Value, path);
        if (!target.HasValue)
        {
            return null;
        }

        if (target.Value.ValueKind == JsonValueKind.Number && target.Value.TryGetInt32(out var number))
        {
            return number;
        }

        if (target.Value.ValueKind == JsonValueKind.String &&
            int.TryParse(target.Value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static double? ReadDouble(JsonElement? node, params string[] path)
    {
        if (!node.HasValue)
        {
            return null;
        }

        var target = path.Length == 0 ? node : TryGetNode(node.Value, path);
        if (!target.HasValue)
        {
            return null;
        }

        if (target.Value.ValueKind == JsonValueKind.Number && target.Value.TryGetDouble(out var number))
        {
            return number;
        }

        if (target.Value.ValueKind == JsonValueKind.String &&
            double.TryParse(target.Value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static bool? ReadBool(JsonElement? node, params string[] path)
    {
        if (!node.HasValue)
        {
            return null;
        }

        var target = path.Length == 0 ? node : TryGetNode(node.Value, path);
        if (!target.HasValue)
        {
            return null;
        }

        if (target.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            return target.Value.GetBoolean();
        }

        if (target.Value.ValueKind == JsonValueKind.Number && target.Value.TryGetInt32(out var number))
        {
            return number != 0;
        }

        if (target.Value.ValueKind == JsonValueKind.String)
        {
            var text = target.Value.GetString();
            if (bool.TryParse(text, out var parsed))
            {
                return parsed;
            }

            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out number))
            {
                return number != 0;
            }
        }

        return null;
    }

    private static DateTimeOffset? ParseTime(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var dto))
        {
            return dto;
        }

        if (long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var epoch))
        {
            // Xiaomi endpoints may return second or millisecond Unix timestamps.
            return epoch > 1_000_000_000_000
                ? DateTimeOffset.FromUnixTimeMilliseconds(epoch)
                : DateTimeOffset.FromUnixTimeSeconds(epoch);
        }

        return null;
    }

    private static string? ResolveWeatherDescription(int? code, string locale)
    {
        if (!code.HasValue)
        {
            return null;
        }

        var isZh = locale.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
        var source = isZh ? ZhWeatherDescriptions : EnWeatherDescriptions;
        if (source.TryGetValue(code.Value, out var text))
        {
            return text;
        }

        return isZh ? $"\u5929\u6c14\u7801 {code.Value}" : $"Weather {code.Value}";
    }

    private static string Truncate(string? text, int maxLength)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        return text.Length <= maxLength
            ? text
            : $"{text[..maxLength]}...";
    }
}
