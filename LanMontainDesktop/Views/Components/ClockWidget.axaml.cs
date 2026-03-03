using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using LanMontainDesktop.Services;

namespace LanMontainDesktop.Views.Components;

public enum ClockDisplayFormat
{
    HourMinuteSecond,  // HH:mm:ss
    HourMinute          // HH:mm
}

public partial class ClockWidget : UserControl, IDesktopComponentWidget, ITimeZoneAwareComponentWidget
{
    private readonly DispatcherTimer _timer = new()
    {
        Interval = TimeSpan.FromSeconds(1)
    };

    private TimeZoneService? _timeZoneService;
    private ClockDisplayFormat _displayFormat = ClockDisplayFormat.HourMinuteSecond;

    public ClockWidget()
    {
        InitializeComponent();

        _timer.Tick += OnTimerTick;
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
        UpdateClock();
    }

    public ClockDisplayFormat DisplayFormat
    {
        get => _displayFormat;
        set
        {
            _displayFormat = value;
            UpdateClock();
        }
    }

    public void SetDisplayFormat(ClockDisplayFormat format)
    {
        DisplayFormat = format;
    }

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
        
        MainTimeTextBlock.Text = now.ToString("HH:mm", CultureInfo.CurrentCulture);
        SecondsTextBlock.Text = now.ToString("ss", CultureInfo.CurrentCulture);
        
        SecondsTextBlock.IsVisible = _displayFormat == ClockDisplayFormat.HourMinuteSecond;
    }

    public void ApplyCellSize(double cellSize)
    {
        // --- Class Island “满盈”风格算法 ---
        
        // 1. 计算组件高度：保持与任务栏核心比例一致 (0.74x)
        var targetHeight = Math.Clamp(cellSize * 0.74, 34, 74);
        RootBorder.Height = targetHeight;
        
        // 2. 动态圆角：确保始终是完美的胶囊半圆
        RootBorder.CornerRadius = new CornerRadius(targetHeight / 2);
        RootBorder.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
        
        // 3. 核心：满盈字阶 (Filled Typography)
        // 使主时间文字占据容器高度的 ~68%，产生饱满的视觉张力
        var mainFontSize = targetHeight * 0.68;
        MainTimeTextBlock.FontSize = mainFontSize;
        MainTimeTextBlock.FontWeight = FontWeight.SemiBold;
        
        // 4. 次级信息：秒数维持 0.7x 比例，并增强透明度呼吸感
        SecondsTextBlock.FontSize = mainFontSize * 0.7;
        SecondsTextBlock.Opacity = 0.55;
        
        // 5. 视觉占比：占据约 2.2 个单元格的感官宽度 (cellSize * 2 + gaps)
        RootBorder.MinWidth = cellSize * 2.2;

        // 6. 间距微调
        if (MainTimeTextBlock.Parent is StackPanel panel)
        {
            panel.Spacing = Math.Clamp(cellSize * 0.06, 2, 8);
        }
        
        // 确保清除可能存在的固定 Padding，由代码控制“紧密感”
        RootBorder.Padding = new Thickness(Math.Clamp(cellSize * 0.15, 12, 24), 0);
    }
}
