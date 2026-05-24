using System.Linq;
using Avalonia.Controls;
using LanMountainDesktop.Services;

namespace LanMountainDesktop.Views.Components;

public partial class ExtendedWeatherWidget : WeatherWidgetBase
{
    public ExtendedWeatherWidget()
    {
        InitializeComponent();
        RenderWeather();
    }

    protected override MaterialWeatherSceneControl SceneControl => Scene;

    protected override void ApplyResponsiveLayout(double cellSize)
    {
        var scale = cellSize / 64d;
        ContentGrid.Margin = new Avalonia.Thickness(18 * scale, 14 * scale, 18 * scale, 12 * scale);
        ContentGrid.RowSpacing = 10 * scale;
        LocationTextBlock.FontSize = System.Math.Clamp(12 * scale, 9, 18);
        ConditionTextBlock.FontSize = System.Math.Clamp(15 * scale, 11, 22);
        TemperatureTextBlock.FontSize = System.Math.Clamp(52 * scale, 32, 76);
        MainIcon.Width = System.Math.Clamp(48 * scale, 32, 72);
        MainIcon.Height = System.Math.Clamp(48 * scale, 32, 72);
    }

    protected override void RenderWeather()
    {
        RootBorder.Background = Brush(CurrentPalette.BackgroundBottom);
        OverlayBorder.Background = new Avalonia.Media.SolidColorBrush(CurrentPalette.OverlayTint);
        LocationTextBlock.Foreground = Brush(CurrentPalette.TextSecondary, 0.78);
        ConditionTextBlock.Foreground = Brush(CurrentPalette.TextPrimary, 0.9);
        TemperatureTextBlock.Foreground = Brush(CurrentPalette.TextPrimary);
        LocationTextBlock.Text = DisplayLocation;
        ConditionTextBlock.Text = State == WeatherWidgetState.Error ? "Weather unavailable" : MaterialWeatherVisualTheme.ResolveDisplayText(Snapshot, StatusText);
        TemperatureTextBlock.Text = FormatTemperature(Snapshot?.Current.TemperatureC);
        MainIcon.SetWeatherIcon(CurrentVisualStyleId, Snapshot);
        BuildMetrics();
        BuildHourlyItems();
        BuildDailyItems();
    }

    private void BuildMetrics()
    {
        MetricGrid.Children.Clear();
        MetricGrid.Children.Add(CreateMetric("AQI", Snapshot?.Current.AirQualityIndex?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "--"));
        MetricGrid.Children.Add(CreateMetric("Humidity", Snapshot?.Current.RelativeHumidityPercent is int h ? $"{h}%" : "--"));
        MetricGrid.Children.Add(CreateMetric("Wind", Snapshot?.Current.WindSpeedKph is double w ? $"{w:0.#} km/h" : "--"));
    }

    private Control CreateMetric(string label, string value)
    {
        var surfaceBrush = new Avalonia.Media.SolidColorBrush(CurrentPalette.SurfaceColor);
        var panel = new StackPanel { Spacing = 3, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center };
        panel.Children.Add(new Border
        {
            Background = surfaceBrush,
            CornerRadius = new Avalonia.CornerRadius(6),
            Padding = new Avalonia.Thickness(8, 5),
            Child = new StackPanel
            {
                Spacing = 2,
                Children =
                {
                    new TextBlock { Text = value, Foreground = Brush(CurrentPalette.TextPrimary), FontWeight = Avalonia.Media.FontWeight.SemiBold, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, FontSize = 12 },
                    new TextBlock { Text = label, FontSize = 10, Foreground = Brush(CurrentPalette.TextSecondary), HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center }
                }
            }
        });
        return panel;
    }

    private void BuildHourlyItems()
    {
        HourlyGrid.Children.Clear();
        var forecasts = Snapshot?.HourlyForecasts.Take(6).ToArray() ?? CreatePreviewSnapshot().HourlyForecasts.Take(6).ToArray();
        foreach (var item in forecasts)
        {
            var surfaceBrush = new Avalonia.Media.SolidColorBrush(CurrentPalette.SurfaceVariantColor);
            var panel = new Border
            {
                Background = surfaceBrush,
                CornerRadius = new Avalonia.CornerRadius(6),
                Padding = new Avalonia.Thickness(5, 5),
                Child = new StackPanel { Spacing = 2, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center }
            };
            var inner = (StackPanel)panel.Child!;
            inner.Children.Add(new TextBlock { Text = FormatTime(item.Time), Foreground = Brush(CurrentPalette.TextSecondary), FontSize = 10, TextAlignment = Avalonia.Media.TextAlignment.Center, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center });
            inner.Children.Add(new WeatherIconView { Width = 26, Height = 26, Source = WeatherIconAssetResolver.LoadIcon(CurrentVisualStyleId, item.WeatherCode, item.WeatherText) });
            inner.Children.Add(new TextBlock { Text = FormatTemperature(item.TemperatureC), Foreground = Brush(CurrentPalette.TextPrimary), FontWeight = Avalonia.Media.FontWeight.SemiBold, TextAlignment = Avalonia.Media.TextAlignment.Center, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, FontSize = 12, ClipToBounds = false });
            HourlyGrid.Children.Add(panel);
        }
    }

    private void BuildDailyItems()
    {
        DailyGrid.Children.Clear();
        var forecasts = Snapshot?.DailyForecasts.Take(5).ToArray() ?? CreatePreviewSnapshot().DailyForecasts.Take(5).ToArray();
        foreach (var item in forecasts)
        {
            var surfaceBrush = new Avalonia.Media.SolidColorBrush(CurrentPalette.SurfaceVariantColor);
            var panel = new Border
            {
                Background = surfaceBrush,
                CornerRadius = new Avalonia.CornerRadius(6),
                Padding = new Avalonia.Thickness(5, 5),
                Child = new StackPanel { Spacing = 2, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center }
            };
            var inner = (StackPanel)panel.Child!;
            inner.Children.Add(new TextBlock { Text = ResolveDayLabel(item.Date), Foreground = Brush(CurrentPalette.TextSecondary), FontSize = 10, TextAlignment = Avalonia.Media.TextAlignment.Center, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center });
            inner.Children.Add(new WeatherIconView { Width = 26, Height = 26, Source = WeatherIconAssetResolver.LoadIcon(CurrentVisualStyleId, item.DayWeatherCode, item.DayWeatherText) });
            inner.Children.Add(new TextBlock { Text = $"{FormatTemperature(item.HighTemperatureC)} / {FormatTemperature(item.LowTemperatureC)}", Foreground = Brush(CurrentPalette.TextPrimary), FontWeight = Avalonia.Media.FontWeight.SemiBold, TextAlignment = Avalonia.Media.TextAlignment.Center, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, FontSize = 11, ClipToBounds = false });
            DailyGrid.Children.Add(panel);
        }
    }
}
