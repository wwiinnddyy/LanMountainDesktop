using System;
using System.Collections.Generic;
using System.Linq;

namespace LanMountainDesktop.Services;

public static class WeatherVisualStyleId
{
    public const string GoogleWeatherV4 = "GoogleWeatherV4";
    public const string Geometric = "Geometric";
    public const string Breezy = "Breezy";
    public const string LemonFlutter = "LemonFlutter";

    public const string Default = GoogleWeatherV4;
}

public sealed record WeatherVisualStyleDefinition(
    string Id,
    string DisplayName,
    string AssetFolder,
    string SourceDescription);

public static class WeatherVisualStyleCatalog
{
    private static readonly WeatherVisualStyleDefinition[] Styles =
    [
        new(
            WeatherVisualStyleId.GoogleWeatherV4,
            "Google Weather v4",
            "google-weather-v4",
            "Google Weather Icons v4 pack for Breezy Weather; icon licensing is uncertain."),
        new(
            WeatherVisualStyleId.Geometric,
            "Geometric",
            "geometric",
            "Geometric Weather icon provider, compatible with Breezy Weather."),
        new(
            WeatherVisualStyleId.Breezy,
            "Breezy Weather",
            "breezy",
            "Breezy Weather bundled icon resources."),
        new(
            WeatherVisualStyleId.LemonFlutter,
            "Lemon Weather Flutter",
            "lemon-flutter",
            "spica_weather_flutter assets, MIT licensed.")
    ];

    public static IReadOnlyList<WeatherVisualStyleDefinition> GetStyles() => Styles;

    public static WeatherVisualStyleDefinition GetDefault() => Styles[0];

    public static WeatherVisualStyleDefinition GetStyle(string? id)
    {
        var normalized = Normalize(id);
        return Styles.First(style => string.Equals(style.Id, normalized, StringComparison.OrdinalIgnoreCase));
    }

    public static string Normalize(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return WeatherVisualStyleId.Default;
        }

        var candidate = id.Trim();
        if (string.Equals(candidate, "DefaultWeather", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(candidate, "HyperOS3", StringComparison.OrdinalIgnoreCase))
        {
            return WeatherVisualStyleId.Default;
        }

        return Styles.Any(style => string.Equals(style.Id, candidate, StringComparison.OrdinalIgnoreCase))
            ? Styles.First(style => string.Equals(style.Id, candidate, StringComparison.OrdinalIgnoreCase)).Id
            : WeatherVisualStyleId.Default;
    }
}
