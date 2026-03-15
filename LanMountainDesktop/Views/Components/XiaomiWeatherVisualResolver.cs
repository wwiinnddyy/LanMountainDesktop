using System;
using LanMountainDesktop.Services;

namespace LanMountainDesktop.Views.Components;

internal readonly record struct WeatherVisualSpec(
    HyperOS3WeatherVisualKind VisualKind,
    string DisplayText,
    string? BackgroundAsset,
    string? PrimaryIconAsset,
    string? CompactIconAsset,
    string? ParticleAsset);

internal static class XiaomiWeatherVisualResolver
{
    public static WeatherVisualSpec Resolve(string? weatherText, int? weatherCode, bool isNight, string locale)
    {
        var visualKind = HyperOS3WeatherTheme.ResolveVisualKind(weatherCode, isNight);
        return new WeatherVisualSpec(
            visualKind,
            ResolveDisplayText(weatherText, weatherCode, locale),
            HyperOS3WeatherTheme.ResolveBackgroundAsset(visualKind),
            HyperOS3WeatherTheme.ResolveHeroIconAsset(visualKind),
            HyperOS3WeatherTheme.ResolveMiniIconAsset(visualKind),
            HyperOS3WeatherTheme.ResolveParticleAsset(visualKind));
    }

    public static string ResolveDisplayText(string? weatherText, int? weatherCode, string locale)
    {
        if (!string.IsNullOrWhiteSpace(weatherText))
        {
            return weatherText.Trim();
        }

        var mappedText = XiaomiWeatherCodeMapper.ResolveDisplayText(weatherCode, locale);
        if (!string.IsNullOrWhiteSpace(mappedText))
        {
            return mappedText;
        }

        return locale.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
            ? "\u672a\u77e5\u5929\u6c14"
            : "Unknown";
    }
}
