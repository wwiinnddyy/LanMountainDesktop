using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using LanMontainDesktop.Services;

namespace LanMontainDesktop.Views.Components;

public partial class MonthCalendarWidget : UserControl, IDesktopComponentWidget, ITimeZoneAwareComponentWidget
{
    private readonly DispatcherTimer _timer = new()
    {
        Interval = TimeSpan.FromMinutes(1)
    };

    private static readonly string[] ZhWeekdayHeaders = ["\u65e5", "\u4e00", "\u4e8c", "\u4e09", "\u56db", "\u4e94", "\u516d"];
    private static readonly string[] EnWeekdayHeaders = ["S", "M", "T", "W", "T", "F", "S"];

    private TimeZoneService? _timeZoneService;
    private double _currentCellSize = 48;
    private double _weekdayFontSize = 20;
    private FontWeight _weekdayFontWeight = FontWeight.SemiBold;
    private double _calendarDayFontSize = 22;
    private FontWeight _calendarDayFontWeight = FontWeight.SemiBold;
    private double _calendarTodayDotSize = 44;

    public MonthCalendarWidget()
    {
        InitializeComponent();

        _timer.Tick += OnTimerTick;
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
        SizeChanged += OnSizeChanged;
        UpdateCalendar();
    }

    public void SetTimeZoneService(TimeZoneService timeZoneService)
    {
        ClearTimeZoneService();
        _timeZoneService = timeZoneService;
        _timeZoneService.TimeZoneChanged += OnTimeZoneChanged;
        UpdateCalendar();
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
        UpdateCalendar();
        _timer.Start();
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _timer.Stop();
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        ApplyCellSize(_currentCellSize);
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        UpdateCalendar();
    }

    private void OnTimeZoneChanged(object? sender, EventArgs e)
    {
        UpdateCalendar();
    }

    private void UpdateCalendar()
    {
        var now = _timeZoneService?.GetCurrentTime() ?? DateTime.Now;
        var culture = CultureInfo.CurrentCulture;
        var isZh = culture.TwoLetterISOLanguageName.Equals("zh", StringComparison.OrdinalIgnoreCase);

        HeaderTextBlock.Text = isZh
            ? $"{now.Month}\u6708{now.Day}\u65e5"
            : now.ToString("MMM d", culture);

        // Locale changes the header width; re-balance typography on every refresh.
        ApplyAdaptiveTypography();
        UpdateWeekdayHeaders(isZh);
        GenerateCalendar(now);
    }

    private void UpdateWeekdayHeaders(bool isZh)
    {
        var headers = isZh ? ZhWeekdayHeaders : EnWeekdayHeaders;
        var blocks = GetWeekdayHeaderBlocks();
        for (var i = 0; i < blocks.Count; i++)
        {
            blocks[i].Text = headers[i];
        }
    }

    private IReadOnlyList<TextBlock> GetWeekdayHeaderBlocks()
    {
        return
        [
            WeekdayText0,
            WeekdayText1,
            WeekdayText2,
            WeekdayText3,
            WeekdayText4,
            WeekdayText5,
            WeekdayText6
        ];
    }

    private void GenerateCalendar(DateTime currentDate)
    {
        var removeList = new List<Control>();
        foreach (var child in CalendarGrid.Children)
        {
            if (child is Control control &&
                control.Tag is string tag &&
                (tag == "day" || tag == "today-dot"))
            {
                removeList.Add(control);
            }
        }

        foreach (var child in removeList)
        {
            CalendarGrid.Children.Remove(child);
        }

        var year = currentDate.Year;
        var month = currentDate.Month;
        var today = currentDate.Day;

        var firstDayOfMonth = new DateTime(year, month, 1);
        var daysInMonth = DateTime.DaysInMonth(year, month);
        var startDayOfWeek = (int)firstDayOfMonth.DayOfWeek;

        for (var day = 1; day <= daysInMonth; day++)
        {
            var row = (day + startDayOfWeek - 1) / 7;
            var col = (day + startDayOfWeek - 1) % 7;
            if (row > 5)
            {
                continue;
            }

            var dayText = new TextBlock
            {
                Text = day.ToString(CultureInfo.CurrentCulture),
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                FontSize = _calendarDayFontSize,
                FontWeight = _calendarDayFontWeight,
                Tag = "day"
            };

            if (day == today)
            {
                var accentBrush = this.TryFindResource("AdaptiveAccentBrush", out var accent)
                    ? accent as IBrush
                    : Brushes.Blue;
                var onAccentBrush = this.TryFindResource("AdaptiveOnAccentBrush", out var onAccent)
                    ? onAccent as IBrush
                    : Brushes.White;

                dayText.Foreground = onAccentBrush;
                var dot = new Border
                {
                    Width = _calendarTodayDotSize,
                    Height = _calendarTodayDotSize,
                    CornerRadius = new CornerRadius(_calendarTodayDotSize * 0.5),
                    Background = accentBrush,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                    Child = dayText,
                    Tag = "today-dot"
                };

                Grid.SetRow(dot, row);
                Grid.SetColumn(dot, col);
                CalendarGrid.Children.Add(dot);
            }
            else
            {
                var isWeekend = col is 0 or 6;
                dayText.Foreground = isWeekend
                    ? GetThemeBrush("AdaptiveTextSecondaryBrush", 0.78)
                    : GetThemeBrush("AdaptiveTextPrimaryBrush", 0.94);
                Grid.SetRow(dayText, row);
                Grid.SetColumn(dayText, col);
                CalendarGrid.Children.Add(dayText);
            }
        }
    }

    public void ApplyCellSize(double cellSize)
    {
        _currentCellSize = Math.Max(1, cellSize);
        UpdateCalendar();
    }

    private void ApplyAdaptiveTypography()
    {
        var scale = ResolveScale();

        RootBorder.CornerRadius = new CornerRadius(Math.Clamp(28 * scale, 14, 40));
        RootBorder.Padding = new Thickness(Math.Clamp(14 * scale, 8, 22));
        LayoutRoot.RowSpacing = Math.Clamp(10 * scale, 5, 16);
        LayoutRoot.Width = Math.Clamp(280 * scale, 220, 420);
        LayoutRoot.Height = Math.Clamp(280 * scale, 220, 420);

        var isZh = CultureInfo.CurrentCulture.TwoLetterISOLanguageName.Equals("zh", StringComparison.OrdinalIgnoreCase);
        var headerTextLength = Math.Max(1, HeaderTextBlock.Text?.Length ?? (isZh ? 5 : 6));
        var headerCompression = headerTextLength >= 8 ? 0.90 : headerTextLength >= 6 ? 0.95 : 1.0;
        var densityBoost = scale <= 0.74 ? 0.90 : scale <= 0.90 ? 0.95 : scale >= 1.45 ? 1.05 : 1.0;

        HeaderTextBlock.FontSize = Math.Clamp(42 * scale * headerCompression * densityBoost, 13, 62);
        HeaderTextBlock.FontWeight = ToVariableWeight(Lerp(560, 720, Math.Clamp((scale - 0.62) / 1.2, 0, 1)));
        HeaderTextBlock.LineHeight = HeaderTextBlock.FontSize * 1.05;

        _weekdayFontSize = Math.Clamp(20 * scale * densityBoost, 7.5, 27);
        _weekdayFontWeight = ToVariableWeight(Lerp(500, 640, Math.Clamp((scale - 0.60) / 1.3, 0, 1)));
        foreach (var block in GetWeekdayHeaderBlocks())
        {
            block.FontSize = _weekdayFontSize;
            block.FontWeight = _weekdayFontWeight;
            block.LineHeight = _weekdayFontSize * 1.06;
        }

        _calendarDayFontSize = Math.Clamp(22 * scale * densityBoost, 8, 32);
        _calendarDayFontWeight = ToVariableWeight(Lerp(540, 680, Math.Clamp((scale - 0.60) / 1.3, 0, 1)));
        _calendarTodayDotSize = Math.Clamp(_calendarDayFontSize * 1.95, 16, 62);
    }

    private double ResolveScale()
    {
        var cellScale = Math.Clamp(_currentCellSize / 44d, 0.65, 1.85);
        var heightScale = Bounds.Height > 1 ? Math.Clamp(Bounds.Height / 280d, 0.60, 1.90) : 1;
        var widthScale = Bounds.Width > 1 ? Math.Clamp(Bounds.Width / 280d, 0.60, 1.90) : 1;
        return Math.Clamp(Math.Min(cellScale, Math.Min(heightScale, widthScale) * 1.06), 0.60, 1.85);
    }

    private IBrush GetThemeBrush(string key, double opacity)
    {
        if (this.TryFindResource(key, out var value) && value is IBrush brush)
        {
            if (brush is ISolidColorBrush solid)
            {
                return new SolidColorBrush(solid.Color, opacity);
            }

            return brush;
        }

        return new SolidColorBrush(Colors.Gray, opacity);
    }

    private static double Lerp(double from, double to, double t)
    {
        return from + ((to - from) * t);
    }

    private static FontWeight ToVariableWeight(double weight)
    {
        return (FontWeight)(int)Math.Clamp(Math.Round(weight), 1, 1000);
    }
}
