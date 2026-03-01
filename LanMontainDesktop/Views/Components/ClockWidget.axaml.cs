using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using LanMontainDesktop.Services;

namespace LanMontainDesktop.Views.Components;

public partial class ClockWidget : UserControl
{
    private readonly DispatcherTimer _timer = new()
    {
        Interval = TimeSpan.FromSeconds(1)
    };

    private TimeZoneService? _timeZoneService;

    public ClockWidget()
    {
        InitializeComponent();

        _timer.Tick += OnTimerTick;
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
        UpdateClock();
    }

    /// <summary>
    /// 设置时区服务
    /// </summary>
    public void SetTimeZoneService(TimeZoneService timeZoneService)
    {
        if (_timeZoneService != null)
        {
            _timeZoneService.TimeZoneChanged -= OnTimeZoneChanged;
        }

        _timeZoneService = timeZoneService;
        _timeZoneService.TimeZoneChanged += OnTimeZoneChanged;
        UpdateClock();
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        UpdateClock();
        _timer.Start();
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _timer.Stop();
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        UpdateClock();
    }

    private void OnTimeZoneChanged(object? sender, EventArgs e)
    {
        UpdateClock();
    }

    private void UpdateClock()
    {
        var now = _timeZoneService?.GetCurrentTime() ?? DateTime.Now;
        TimeTextBlock.Text = now.ToString("HH:mm:ss", CultureInfo.CurrentCulture);
    }

    public void ApplyCellSize(double cellSize)
    {
        var padding = Math.Clamp(cellSize * 0.12, 2, 14);
        RootBorder.Padding = new Thickness(padding);
        RootBorder.CornerRadius = new CornerRadius(Math.Clamp(cellSize * 0.16, 4, 18));

        // Keep the time legible across dense and sparse grid layouts.
        TimeTextBlock.FontSize = Math.Clamp(cellSize * 0.42, 10, 56);
        TimeTextBlock.FontWeight = FontWeight.SemiBold;
    }
}
