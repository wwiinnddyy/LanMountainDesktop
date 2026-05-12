using Avalonia.Media;

namespace LanMountainDesktop.Views.Components;

public partial class WeatherWidget : WeatherWidgetBase
{
    public WeatherWidget()
    {
        InitializeComponent();
        RenderWeather();
    }

    protected override MaterialWeatherSceneControl SceneControl => Scene;

    protected override void ApplyResponsiveLayout(double cellSize)
    {
        var scale = cellSize / 64d;
        ContentGrid.Margin = new Avalonia.Thickness(18 * scale, 14 * scale, 18 * scale, 12 * scale);
        TemperatureTextBlock.FontSize = System.Math.Clamp(68 * scale, 40, 92);
        ConditionTextBlock.FontSize = System.Math.Clamp(16 * scale, 12, 24);
        LocationTextBlock.FontSize = System.Math.Clamp(12 * scale, 9, 16);
        RangeTextBlock.FontSize = System.Math.Clamp(12 * scale, 9, 16);
        MainIcon.Width = System.Math.Clamp(64 * scale, 40, 88);
        MainIcon.Height = System.Math.Clamp(64 * scale, 40, 88);
    }

    protected override void RenderWeather()
    {
        RootBorder.Background = Brush(CurrentPalette.BackgroundBottom);
        OverlayBorder.Background = new SolidColorBrush(CurrentPalette.OverlayTint);
        TemperatureTextBlock.Foreground = Brush(CurrentPalette.TextPrimary);
        ConditionTextBlock.Foreground = Brush(CurrentPalette.TextPrimary, 0.88);
        LocationTextBlock.Foreground = Brush(CurrentPalette.TextSecondary, 0.78);
        RangeTextBlock.Foreground = Brush(CurrentPalette.TextSecondary);

        TemperatureTextBlock.Text = State == WeatherWidgetState.MissingLocation ? "--\u00b0" : FormatTemperature(Snapshot?.Current.TemperatureC);
        ConditionTextBlock.Text = State switch
        {
            WeatherWidgetState.MissingLocation => "Set weather location",
            WeatherWidgetState.Error => "Weather unavailable",
            WeatherWidgetState.Loading => "Loading",
            _ => MaterialWeatherVisualTheme.ResolveDisplayText(Snapshot, "Weather")
        };
        LocationTextBlock.Text = DisplayLocation;
        RangeTextBlock.Text = FormatRange(Snapshot);
        MainIcon.SetWeatherIcon(CurrentVisualStyleId, Snapshot);
    }
}
