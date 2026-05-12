using System;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using LanMountainDesktop.Models;

namespace LanMountainDesktop.Services;

public static class WeatherIconAssetResolver
{
    private const string RootUri = "avares://LanMountainDesktop/Assets/MaterialWeatherIcons";

    public static Bitmap? LoadIcon(string? styleId, WeatherSnapshot? snapshot)
    {
        return LoadIcon(styleId, ResolveIconKey(snapshot));
    }

    public static Bitmap? LoadIcon(string? styleId, int? weatherCode, string? weatherText, bool isDaylight = true)
    {
        return LoadIcon(styleId, ResolveIconKey(weatherCode, weatherText, isDaylight));
    }

    public static Bitmap? LoadIcon(string? styleId, string iconKey)
    {
        var uri = ResolveAssetUri(styleId, iconKey);
        if (uri is null)
        {
            return null;
        }

        using var stream = AssetLoader.Open(uri);
        return new Bitmap(stream);
    }

    public static Uri? ResolveAssetUri(string? styleId, WeatherSnapshot? snapshot)
    {
        return ResolveAssetUri(styleId, ResolveIconKey(snapshot));
    }

    public static Uri? ResolveAssetUri(string? styleId, int? weatherCode, string? weatherText, bool isDaylight = true)
    {
        return ResolveAssetUri(styleId, ResolveIconKey(weatherCode, weatherText, isDaylight));
    }

    public static Uri? ResolveAssetUri(string? styleId, string iconKey)
    {
        var style = WeatherVisualStyleCatalog.GetStyle(styleId);
        return TryBuildUri(style, iconKey)
            ?? TryBuildUri(style, NormalizeDayNightFallback(iconKey))
            ?? TryBuildUri(style, "cloudy_day")
            ?? TryBuildUri(WeatherVisualStyleCatalog.GetDefault(), iconKey)
            ?? TryBuildUri(WeatherVisualStyleCatalog.GetDefault(), NormalizeDayNightFallback(iconKey))
            ?? TryBuildUri(WeatherVisualStyleCatalog.GetDefault(), "cloudy_day");
    }

    public static string ResolveIconKey(WeatherSnapshot? snapshot)
    {
        var current = snapshot?.Current;
        var isDaylight = current?.IsDaylight ?? true;
        return ResolveIconKey(current?.WeatherCode, current?.WeatherText, isDaylight);
    }

    public static string ResolveIconKey(int? weatherCode, string? weatherText, bool isDaylight)
    {
        var dayNight = isDaylight ? "day" : "night";
        var condition = ResolveCondition(weatherCode, weatherText);
        return condition switch
        {
            "clear" => $"clear_{dayNight}",
            "partly_cloudy" => $"partly_cloudy_{dayNight}",
            "cloudy" => $"cloudy_{dayNight}",
            "rain" => $"rain_{dayNight}",
            "sleet" => $"sleet_{dayNight}",
            "snow" => $"snow_{dayNight}",
            "hail" => $"hail_{dayNight}",
            "thunder" => $"thunder_{dayNight}",
            "thunderstorm" => $"thunderstorm_{dayNight}",
            "fog" => $"fog_{dayNight}",
            "haze" => $"haze_{dayNight}",
            "wind" => $"wind_{dayNight}",
            _ => $"cloudy_{dayNight}"
        };
    }

    private static Uri? TryBuildUri(WeatherVisualStyleDefinition style, string iconKey)
    {
        var fileName = ResolveFileName(style.Id, iconKey);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        var uri = new Uri($"{RootUri}/{style.AssetFolder}/{fileName}", UriKind.Absolute);
        try
        {
            return AssetLoader.Exists(uri) ? uri : null;
        }
        catch (InvalidOperationException)
        {
            return uri;
        }
    }

    private static string ResolveFileName(string styleId, string iconKey)
    {
        var normalized = NormalizeDayNightFallback(iconKey);
        return styleId switch
        {
            WeatherVisualStyleId.GoogleWeatherV4 => ResolveGoogleFileName(iconKey),
            WeatherVisualStyleId.Geometric => ResolveGeometricFileName(normalized),
            WeatherVisualStyleId.Breezy => ResolveBreezyFileName(normalized),
            WeatherVisualStyleId.LemonFlutter => ResolveLemonFileName(normalized),
            _ => ResolveGoogleFileName(iconKey)
        };
    }

    private static string ResolveGoogleFileName(string iconKey)
    {
        return iconKey switch
        {
            "cloudy_day" => "weather_cloudy_day.png",
            "cloudy_night" => "weather_cloudy_night.png",
            _ => $"weather_{iconKey}.png"
        };
    }

    private static string ResolveGeometricFileName(string iconKey)
    {
        return iconKey switch
        {
            "clear_day" or "clear_night" or "partly_cloudy_day" or "partly_cloudy_night" => $"weather_{iconKey}_geometric.png",
            "cloudy_day" or "cloudy_night" => "weather_cloudy_geometric.png",
            "rain_day" or "rain_night" => "weather_rain_geometric.png",
            "sleet_day" or "sleet_night" => "weather_sleet_geometric.png",
            "snow_day" or "snow_night" => "weather_snow_geometric.png",
            "hail_day" or "hail_night" => "weather_hail_geometric.png",
            "fog_day" or "fog_night" => "weather_fog_geometric.png",
            "haze_day" or "haze_night" => "weather_haze_geometric.png",
            "wind_day" or "wind_night" => "weather_wind_geometric.png",
            "thunderstorm_day" or "thunderstorm_night" => "weather_thunder_geometric.png",
            "thunder_day" or "thunder_night" => "weather_thunder_geometric.png",
            _ => $"weather_{iconKey}_geometric.png"
        };
    }

    private static string ResolveBreezyFileName(string iconKey)
    {
        return iconKey switch
        {
            "clear_day" or "clear_night" or "partly_cloudy_day" or "partly_cloudy_night" => $"weather_{iconKey}.png",
            "cloudy_day" or "cloudy_night" => "weather_cloudy.png",
            "rain_day" or "rain_night" => "weather_rain.png",
            "sleet_day" or "sleet_night" => "weather_sleet.png",
            "snow_day" or "snow_night" => "weather_snow.png",
            "hail_day" or "hail_night" => "weather_hail.png",
            "fog_day" or "fog_night" => "weather_fog.png",
            "haze_day" or "haze_night" => "weather_haze.png",
            "wind_day" or "wind_night" => "weather_wind.png",
            "thunder_day" or "thunder_night" => "weather_thunder.png",
            "thunderstorm_day" or "thunderstorm_night" => "weather_thunderstorm.png",
            _ => $"weather_{iconKey}.png"
        };
    }

    private static string ResolveLemonFileName(string iconKey)
    {
        return iconKey switch
        {
            "clear_day" or "clear_night" => "ic_sun.png",
            "partly_cloudy_day" or "partly_cloudy_night" => "ic_cloudy.png",
            "cloudy_day" or "cloudy_night" => "ic_cloud.png",
            "rain_day" or "rain_night" => "ic_rain.png",
            "sleet_day" or "sleet_night" => "ic_light_rain.png",
            "snow_day" or "snow_night" => "ic_snow.png",
            "hail_day" or "hail_night" => "ic_storm.png",
            "thunder_day" or "thunder_night" => "ic_thunder.png",
            "thunderstorm_day" or "thunderstorm_night" => "ic_storm.png",
            "fog_day" or "fog_night" => "ic_cloudy.png",
            "haze_day" or "haze_night" => "ic_cloudy.png",
            "wind_day" or "wind_night" => "ic_windmill.png",
            _ => "ic_cloud.png"
        };
    }

    private static string NormalizeDayNightFallback(string iconKey)
    {
        return iconKey switch
        {
            "cloudy_night" => "cloudy_day",
            "rain_night" => "rain_day",
            "sleet_night" => "sleet_day",
            "snow_night" => "snow_day",
            "hail_night" => "hail_day",
            "thunder_night" => "thunder_day",
            "thunderstorm_night" => "thunderstorm_day",
            "fog_night" => "fog_day",
            "haze_night" => "haze_day",
            "wind_night" => "wind_day",
            _ => iconKey
        };
    }

    private static string ResolveCondition(int? weatherCode, string? weatherText)
    {
        if (weatherCode.HasValue)
        {
            return weatherCode.Value switch
            {
                0 => "clear",
                1 => "partly_cloudy",
                2 => "cloudy",
                3 or 7 or 8 or 9 or 10 or 11 or 12 or 19 or 21 or 22 or 23 or 24 or 25 or 301 => "rain",
                4 or 5 => "thunderstorm",
                6 or 13 or 14 or 15 or 16 or 17 or 26 or 27 or 28 or 302 => "snow",
                18 or 32 or 49 or 57 or 58 => "fog",
                20 or 29 or 30 or 31 or 53 or 54 or 55 or 56 => "haze",
                _ => "cloudy"
            };
        }

        var text = weatherText?.Trim().ToLowerInvariant() ?? string.Empty;
        if (text.Contains("thunderstorm") || text.Contains('\u96f7')) return "thunderstorm";
        if (text.Contains("thunder") || text.Contains("storm")) return "thunder";
        if (text.Contains("sleet")) return "sleet";
        if (text.Contains("hail")) return "hail";
        if (text.Contains("snow") || text.Contains('\u96ea')) return "snow";
        if (text.Contains("rain") || text.Contains('\u96e8')) return "rain";
        if (text.Contains("fog") || text.Contains("mist") || text.Contains('\u96fe')) return "fog";
        if (text.Contains("haze") || text.Contains("dust") || text.Contains('\u973e')) return "haze";
        if (text.Contains("wind")) return "wind";
        if (text.Contains("partly")) return "partly_cloudy";
        if (text.Contains("cloud") || text.Contains('\u4e91') || text.Contains('\u9634')) return "cloudy";
        if (text.Contains("clear") || text.Contains("sun") || text.Contains('\u6674')) return "clear";
        return "cloudy";
    }
}
