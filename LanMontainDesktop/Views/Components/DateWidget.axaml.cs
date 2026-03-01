using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using LanMontainDesktop.Services;

namespace LanMontainDesktop.Views.Components;

public partial class DateWidget : UserControl
{
    private readonly DispatcherTimer _timer = new()
    {
        Interval = TimeSpan.FromMinutes(1)
    };

    private TimeZoneService? _timeZoneService;

    public DateWidget()
    {
        InitializeComponent();

        _timer.Tick += OnTimerTick;
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
        UpdateDate();
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
        UpdateDate();
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        UpdateDate();
        _timer.Start();
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _timer.Stop();
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        UpdateDate();
    }

    private void OnTimeZoneChanged(object? sender, EventArgs e)
    {
        UpdateDate();
    }

    private void UpdateDate()
    {
        var now = _timeZoneService?.GetCurrentTime() ?? DateTime.Now;
        var culture = CultureInfo.CurrentCulture;
        
        // 右侧：今日详情
        TodayDayTextBlock.Text = now.Day.ToString();
        TodayWeekdayTextBlock.Text = now.ToString("dddd", culture);
        
        // 左侧：月历
        CalendarMonthYearTextBlock.Text = now.ToString("yyyy年M月", culture);
        
        // 生成月历
        GenerateCalendar(now);
    }

    private void GenerateCalendar(DateTime currentDate)
    {
        // 清空之前的日期（保留星期标题）
        var childrenToRemove = new List<Control>();
        foreach (var child in CalendarGrid.Children)
        {
            if (child is TextBlock tb && tb.Tag?.ToString() == "day")
            {
                childrenToRemove.Add(tb);
            }
        }
        foreach (var child in childrenToRemove)
        {
            CalendarGrid.Children.Remove(child);
        }

        var year = currentDate.Year;
        var month = currentDate.Month;
        var today = currentDate.Day;
        
        // 获取该月第一天
        var firstDayOfMonth = new DateTime(year, month, 1);
        var daysInMonth = DateTime.DaysInMonth(year, month);
        var startDayOfWeek = (int)firstDayOfMonth.DayOfWeek; // 0 = Sunday

        // 生成日期
        for (int day = 1; day <= daysInMonth; day++)
        {
            var row = ((day + startDayOfWeek - 1) / 7) + 1; // +1 because row 0 is weekday headers
            var col = (day + startDayOfWeek - 1) % 7;

            if (row > 5) continue; // 最多显示6行

            var dayText = new TextBlock
            {
                Text = day.ToString(),
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                FontSize = 10,
                Tag = "day"
            };

            // 今天高亮
            if (day == today)
            {
                // 使用主题色高亮今天
                var accentBrush = this.TryFindResource("AdaptiveAccentBrush", out var accent) 
                    ? accent as IBrush 
                    : Brushes.Blue;
                var onAccentBrush = this.TryFindResource("AdaptiveOnAccentBrush", out var onAccent) 
                    ? onAccent as IBrush 
                    : Brushes.White;
                
                dayText.Foreground = onAccentBrush;
                dayText.FontWeight = FontWeight.Bold;
                dayText.Background = new SolidColorBrush(Colors.Transparent);
                
                // 添加背景圆
                var highlight = new Border
                {
                    Background = accentBrush,
                    CornerRadius = new CornerRadius(10),
                    Width = 20,
                    Height = 20,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                    Child = dayText
                };
                Grid.SetRow(highlight, row);
                Grid.SetColumn(highlight, col);
                CalendarGrid.Children.Add(highlight);
            }
            else
            {
                // 使用主题次要文本颜色
                var secondaryBrush = this.TryFindResource("AdaptiveTextSecondaryBrush", out var secondary) 
                    ? secondary as IBrush 
                    : Brushes.Gray;
                dayText.Foreground = secondaryBrush;
                Grid.SetRow(dayText, row);
                Grid.SetColumn(dayText, col);
                CalendarGrid.Children.Add(dayText);
            }
        }
    }

    public void ApplyCellSize(double cellSize)
    {
        // 根据格子大小调整圆角
        RootBorder.CornerRadius = new CornerRadius(Math.Clamp(cellSize * 0.12, 8, 20));
        
        // 调整字体大小
        var baseFontSize = cellSize * 0.25;
        TodayDayTextBlock.FontSize = Math.Clamp(baseFontSize * 2.8, 28, 72);
        TodayWeekdayTextBlock.FontSize = Math.Clamp(baseFontSize * 0.6, 10, 16);
        CalendarMonthYearTextBlock.FontSize = Math.Clamp(baseFontSize * 0.55, 9, 14);
    }
}
