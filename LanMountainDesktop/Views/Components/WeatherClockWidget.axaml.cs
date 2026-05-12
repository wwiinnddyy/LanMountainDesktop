using System;
using Avalonia.Threading;
using LanMountainDesktop.Services;

namespace LanMountainDesktop.Views.Components;

public partial class WeatherClockWidget : WeatherWidgetBase, ITimeZoneAwareComponentWidget
{
    private readonly DispatcherTimer _clockTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private TimeZoneService? _timeZoneService;

    public WeatherClockWidget()
    {
        InitializeComponent();
        _clockTimer.Tick += (_, _) => UpdateClock();
        AttachedToVisualTree += (_, _) =>
        {
            UpdateClock();
            _clockTimer.Start();
        };
        DetachedFromVisualTree += (_, _) => _clockTimer.Stop();
        RenderWeather();
    }

    protected override MaterialWeatherSceneControl SceneControl => Scene;

    public void SetTimeZoneService(TimeZoneService timeZoneService)
    {
        ClearTimeZoneService();
        _timeZoneService = timeZoneService;
        _timeZoneService.TimeZoneChanged += OnTimeZoneChanged;
        UpdateClock();
    }

    public void ClearTimeZoneService()
    {
        if (_timeZoneService is null)
        {
            return;
        }

        _timeZoneService.TimeZoneChanged -= OnTimeZoneChanged;
        _timeZoneService = null;
    }

    protected override void ApplyResponsiveLayout(double cellSize)
    {
        var scale = cellSize / 64d;
        ContentGrid.Margin = new Avalonia.Thickness(16 * scale, 10 * scale);
        ContentGrid.ColumnSpacing = 10 * scale;
        TimeTextBlock.FontSize = System.Math.Clamp(36 * scale, 22, 52);
        DateTextBlock.FontSize = System.Math.Clamp(12 * scale, 8, 16);
        TemperatureTextBlock.FontSize = System.Math.Clamp(20 * scale, 14, 30);
        ConditionTextBlock.FontSize = System.Math.Clamp(11 * scale, 8, 14);
        MainIcon.Width = System.Math.Clamp(40 * scale, 24, 56);
        MainIcon.Height = System.Math.Clamp(40 * scale, 24, 56);
    }

    protected override void RenderWeather()
    {
        RootBorder.Background = Brush(CurrentPalette.BackgroundBottom);
        OverlayBorder.Background = new Avalonia.Media.SolidColorBrush(CurrentPalette.OverlayTint);
        TimeTextBlock.Foreground = Brush(CurrentPalette.TextPrimary);
        DateTextBlock.Foreground = Brush(CurrentPalette.TextSecondary);
        TemperatureTextBlock.Foreground = Brush(CurrentPalette.TextPrimary);
        ConditionTextBlock.Foreground = Brush(CurrentPalette.TextSecondary);
        TemperatureTextBlock.Text = FormatTemperature(Snapshot?.Current.TemperatureC);
        MainIcon.SetWeatherIcon(CurrentVisualStyleId, Snapshot);
        ConditionTextBlock.Text = State == WeatherWidgetState.MissingLocation
            ? "Set location"
            : State == WeatherWidgetState.Error
                ? "Unavailable"
                : MaterialWeatherVisualTheme.ResolveDisplayText(Snapshot, "Weather");
        UpdateClock();
    }

    private void UpdateClock()
    {
        var now = _timeZoneService?.GetCurrentTime() ?? DateTime.Now;
        TimeTextBlock.Text = now.ToString("HH:mm");
        DateTextBlock.Text = $"{now:ddd, MMM d} · {DisplayLocation}";
    }

    private void OnTimeZoneChanged(object? sender, EventArgs e)
    {
        UpdateClock();
    }
}
