using Avalonia.Controls;
using Avalonia.Media;
using LanMountainDesktop.Models;
using LanMountainDesktop.Services;

namespace LanMountainDesktop.Views.Components;

public sealed class WeatherIconView : Image
{
    public WeatherIconView()
    {
        Stretch = Stretch.Uniform;
        IsHitTestVisible = false;
    }

    public void SetWeatherIcon(string? styleId, string iconKey)
    {
        Source = WeatherIconAssetResolver.LoadIcon(styleId, iconKey);
    }

    public void SetWeatherIcon(string? styleId, WeatherSnapshot? snapshot)
    {
        Source = WeatherIconAssetResolver.LoadIcon(styleId, snapshot);
    }

    public void SetWeatherIcon(string? styleId, int? weatherCode, string? weatherText, bool isDaylight = true)
    {
        Source = WeatherIconAssetResolver.LoadIcon(styleId, weatherCode, weatherText, isDaylight);
    }
}
