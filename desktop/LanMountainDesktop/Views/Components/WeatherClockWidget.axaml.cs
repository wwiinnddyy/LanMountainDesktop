using System;
using Avalonia.Threading;
using LanMountainDesktop.Services;

namespace LanMountainDesktop.Views.Components;

public partial class WeatherClockWidget : WeatherWidgetBase, ITimeZoneAwareComponentWidget
{
    private readonly DispatcherTimer _clockTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private TimeZoneService? _timeZoneService;
    // 日期行一天/一次位置变更才重写：记上一次写入时的"日"。
    private DateTime _lastDateTextDate = DateTime.MinValue;

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
        TimeZoneServiceBinding.Attach(
            ref _timeZoneService,
            timeZoneService,
            OnTimeZoneChanged,
            UpdateClock);
    }

    public void ClearTimeZoneService()
    {
        _timeZoneService = TimeZoneServiceBinding.Clear(_timeZoneService, OnTimeZoneChanged);
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

        // 天气快照可能换了位置：失效日期守卫，让紧跟着的 UpdateClock 把新位置写进日期行。
        _lastDateTextDate = DateTime.MinValue;
        UpdateClock();
    }

    private void UpdateClock()
    {
        var now = _timeZoneService?.GetCurrentTime() ?? DateTime.Now;
        var timeText = now.ToString("HH:mm");
        if (!string.Equals(TimeTextBlock.Text, timeText, StringComparison.Ordinal))
        {
            TimeTextBlock.Text = timeText;
        }

        // 日期与位置一天/一次设置才变：先比"日"，换日才拼那条插值字符串，别每秒白付一次分配。
        if (now.Date != _lastDateTextDate)
        {
            _lastDateTextDate = now.Date;
            DateTextBlock.Text = $"{now:ddd, MMM d} · {DisplayLocation}";
        }
    }

    private void OnTimeZoneChanged(object? sender, EventArgs e)
    {
        UpdateClock();
    }
}
