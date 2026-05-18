using System.Linq;
using Avalonia.Controls;
using LanMountainDesktop.Models;
using LanMountainDesktop.Services;

namespace LanMountainDesktop.Views.Components;

public partial class MultiDayWeatherWidget : WeatherWidgetBase
{
    public MultiDayWeatherWidget()
    {
        InitializeComponent();
        RenderWeather();
    }

    protected override MaterialWeatherSceneControl SceneControl => Scene;

    protected override void ApplyResponsiveLayout(double cellSize)
    {
        var scale = cellSize / 64d;
        ContentGrid.Margin = new Avalonia.Thickness(16 * scale, 12 * scale);
        ContentGrid.ColumnSpacing = 12 * scale;
        TemperatureTextBlock.FontSize = System.Math.Clamp(40 * scale, 26, 58);
        ConditionTextBlock.FontSize = System.Math.Clamp(14 * scale, 10, 20);
        LocationTextBlock.FontSize = System.Math.Clamp(11 * scale, 8, 16);
        MainIcon.Width = System.Math.Clamp(56 * scale, 36, 80);
        MainIcon.Height = System.Math.Clamp(56 * scale, 36, 80);
    }

    protected override void RenderWeather()
    {
        RootBorder.Background = Brush(CurrentPalette.BackgroundBottom);
        OverlayBorder.Background = new Avalonia.Media.SolidColorBrush(CurrentPalette.OverlayTint);
        TemperatureTextBlock.Foreground = Brush(CurrentPalette.TextPrimary);
        ConditionTextBlock.Foreground = Brush(CurrentPalette.TextPrimary, 0.88);
        LocationTextBlock.Foreground = Brush(CurrentPalette.TextSecondary, 0.78);
        TemperatureTextBlock.Text = FormatTemperature(Snapshot?.Current.TemperatureC);
        MainIcon.SetWeatherIcon(CurrentVisualStyleId, Snapshot);
        ConditionTextBlock.Text = State == WeatherWidgetState.Error ? "Weather unavailable" : MaterialWeatherVisualTheme.ResolveDisplayText(Snapshot, StatusText);
        LocationTextBlock.Text = DisplayLocation;
        BuildDailyItems();
    }

    private void BuildDailyItems()
    {
        var forecasts = Snapshot?.DailyForecasts.Take(5).ToArray() ?? CreatePreviewSnapshot().DailyForecasts.Take(5).ToArray();
        var panel = new StackPanel { Spacing = 0, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
        for (var i = 0; i < forecasts.Length; i++)
        {
            panel.Children.Add(CreateRow(forecasts[i], i < forecasts.Length - 1));
        }

        DailyItemsControl.ItemsSource = new[] { panel };
    }

    private Control CreateRow(WeatherDailyForecast item, bool addDivider)
    {
        var rowPanel = new StackPanel { Spacing = 0 };
        
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"),
            ColumnSpacing = 10,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };
        row.Children.Add(new WeatherIconView { Width = 24, Height = 24, Source = WeatherIconAssetResolver.LoadIcon(CurrentVisualStyleId, item.DayWeatherCode, item.DayWeatherText), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center });
        row.Children.Add(new TextBlock { Text = ResolveDayLabel(item.Date), Foreground = Brush(CurrentPalette.TextPrimary), FontWeight = Avalonia.Media.FontWeight.SemiBold, TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, FontSize = 12 });
        Grid.SetColumn(row.Children[^1], 1);
        row.Children.Add(new TextBlock { Text = FormatTemperature(item.HighTemperatureC), Foreground = Brush(CurrentPalette.TextPrimary), FontWeight = Avalonia.Media.FontWeight.SemiBold, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, FontSize = 12 });
        Grid.SetColumn(row.Children[^1], 2);
        row.Children.Add(new TextBlock { Text = FormatTemperature(item.LowTemperatureC), Foreground = Brush(CurrentPalette.TextSecondary), FontWeight = Avalonia.Media.FontWeight.Medium, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, FontSize = 12 });
        Grid.SetColumn(row.Children[^1], 3);
        
        rowPanel.Children.Add(row);
        
        if (addDivider)
        {
            rowPanel.Children.Add(new Border
            {
                Height = 1,
                Background = new Avalonia.Media.SolidColorBrush(CurrentPalette.OutlineColor),
                Margin = new Avalonia.Thickness(0, 6, 0, 6)
            });
        }
        
        return rowPanel;
    }
}
