using System.Linq;
using Avalonia.Controls;
using LanMountainDesktop.Services;

namespace LanMountainDesktop.Views.Components;

public partial class HourlyWeatherWidget : WeatherWidgetBase
{
    public HourlyWeatherWidget()
    {
        InitializeComponent();
        RenderWeather();
    }

    protected override MaterialWeatherSceneControl SceneControl => Scene;

    protected override void ApplyResponsiveLayout(double cellSize)
    {
        var scale = cellSize / 64d;
        ContentGrid.Margin = new Avalonia.Thickness(16 * scale, 12 * scale);
        ContentGrid.RowSpacing = 10 * scale;
        TemperatureTextBlock.FontSize = System.Math.Clamp(40 * scale, 26, 58);
        ConditionTextBlock.FontSize = System.Math.Clamp(14 * scale, 10, 20);
        LocationTextBlock.FontSize = System.Math.Clamp(11 * scale, 8, 16);
        RangeTextBlock.FontSize = System.Math.Clamp(11 * scale, 8, 16);
        MainIcon.Width = System.Math.Clamp(44 * scale, 28, 60);
        MainIcon.Height = System.Math.Clamp(44 * scale, 28, 60);
    }

    protected override void RenderWeather()
    {
        RootBorder.Background = Brush(CurrentPalette.BackgroundBottom);
        OverlayBorder.Background = new Avalonia.Media.SolidColorBrush(CurrentPalette.OverlayTint);
        TemperatureTextBlock.Foreground = Brush(CurrentPalette.TextPrimary);
        ConditionTextBlock.Foreground = Brush(CurrentPalette.TextPrimary, 0.88);
        LocationTextBlock.Foreground = Brush(CurrentPalette.TextSecondary, 0.78);
        RangeTextBlock.Foreground = Brush(CurrentPalette.TextSecondary);
        TemperatureTextBlock.Text = FormatTemperature(Snapshot?.Current.TemperatureC);
        MainIcon.SetWeatherIcon(CurrentVisualStyleId, Snapshot);
        ConditionTextBlock.Text = State == WeatherWidgetState.Error ? "Weather unavailable" : MaterialWeatherVisualTheme.ResolveDisplayText(Snapshot, StatusText);
        LocationTextBlock.Text = DisplayLocation;
        RangeTextBlock.Text = FormatRange(Snapshot);
        BuildHourlyItems();
    }

    private void BuildHourlyItems()
    {
        HourlyGrid.Children.Clear();
        var items = (Snapshot?.HourlyForecasts.Take(6).ToArray() ?? CreatePreviewSnapshot().HourlyForecasts.Take(6).ToArray())
            .Select(item => new WeatherChipItem(
                FormatTime(item.Time),
                FormatTemperature(item.TemperatureC),
                item.WeatherCode,
                item.WeatherText,
                MaterialWeatherVisualTheme.ResolveCondition(item.WeatherCode, item.WeatherText)))
            .ToArray();
        foreach (var item in items)
        {
            HourlyGrid.Children.Add(CreateChip(item));
        }
    }

    private Control CreateChip(WeatherChipItem item)
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
        inner.Children.Add(new TextBlock { Text = item.Label, FontSize = 10, Foreground = Brush(CurrentPalette.TextSecondary), HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, TextAlignment = Avalonia.Media.TextAlignment.Center });
        inner.Children.Add(new WeatherIconView { Width = 24, Height = 24, Source = WeatherIconAssetResolver.LoadIcon(CurrentVisualStyleId, item.WeatherCode, item.WeatherText) });
        inner.Children.Add(new TextBlock { Text = item.Value, FontWeight = Avalonia.Media.FontWeight.SemiBold, Foreground = Brush(CurrentPalette.TextPrimary), HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, FontSize = 11, TextAlignment = Avalonia.Media.TextAlignment.Center });
        return panel;
    }

    private readonly record struct WeatherChipItem(string Label, string Value, int? WeatherCode, string? WeatherText, MaterialWeatherCondition Condition);
}
