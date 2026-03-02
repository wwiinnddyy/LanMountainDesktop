using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using LanMontainDesktop.Services;

namespace LanMontainDesktop.Views.Components;

public partial class DateWidget : UserControl, IDesktopComponentWidget, ITimeZoneAwareComponentWidget
{
    private readonly DispatcherTimer _timer = new()
    {
        Interval = TimeSpan.FromMinutes(1)
    };

    private static readonly LunarCalendarService LunarCalendarService = new();

    private static readonly string[] ZhWeekdayHeaders = ["\u65e5", "\u4e00", "\u4e8c", "\u4e09", "\u56db", "\u4e94", "\u516d"];
    private static readonly string[] EnWeekdayHeaders = ["S", "M", "T", "W", "T", "F", "S"];

    private static readonly string[] ZhYiCandidates =
    [
        "\u796d\u7940",
        "\u7948\u798f",
        "\u4f1a\u53cb",
        "\u51fa\u884c",
        "\u6c42\u8d22",
        "\u5f00\u5e02",
        "\u4ea4\u6613",
        "\u5ac1\u5a36",
        "\u6c42\u5b66",
        "\u4fee\u9020",
        "\u5b89\u5e8a",
        "\u7eb3\u91c7"
    ];

    private static readonly string[] ZhJiCandidates =
    [
        "\u52a8\u571f",
        "\u8bc9\u8bbc",
        "\u8fdc\u822a",
        "\u4e89\u6267",
        "\u7834\u571f",
        "\u5b89\u846c",
        "\u4f10\u6728",
        "\u6398\u4e95",
        "\u8fc1\u5f99",
        "\u5f00\u4ed3",
        "\u7f6e\u4ea7",
        "\u5f00\u6e20"
    ];

    private static readonly string[] EnYiCandidates =
    [
        "Worship",
        "Blessing",
        "Travel",
        "Meetings",
        "Trade",
        "Business",
        "Study",
        "Build",
        "Gathering",
        "Planning"
    ];

    private static readonly string[] EnJiCandidates =
    [
        "Dispute",
        "Lawsuit",
        "Major move",
        "Groundwork",
        "Burial",
        "Long voyage",
        "Contract rush",
        "Risky purchase",
        "Heavy repair",
        "Conflict"
    ];

    private TimeZoneService? _timeZoneService;
    private double _currentCellSize = 64;
    private double _weekdayFontSize = 17;
    private FontWeight _weekdayFontWeight = FontWeight.SemiBold;
    private double _calendarDayFontSize = 18;
    private FontWeight _calendarDayFontWeight = FontWeight.SemiBold;
    private double _calendarTodayDotSize = 32;
    private int _lunarItemCount = 3;
    private int _calendarVisibleRows = 6;
    private bool? _isNightModeApplied;
    private double _weekdayHeaderOpacity = 0.60;
    private double _weekdayNumberOpacity = 0.90;
    private double _weekendNumberOpacity = 0.58;

    public DateWidget()
    {
        InitializeComponent();

        _timer.Tick += OnTimerTick;
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
        SizeChanged += OnSizeChanged;
        UpdateDate();
    }

    public void SetTimeZoneService(TimeZoneService timeZoneService)
    {
        if (_timeZoneService is not null)
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

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        ApplyCellSize(_currentCellSize);
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
        var isZh = culture.TwoLetterISOLanguageName.Equals("zh", StringComparison.OrdinalIgnoreCase);
        var lunar = LunarCalendarService.GetLunarInfo(now);

        GregorianHeadlineTextBlock.Text = isZh
            ? $"{now.Month}\u6708{now.Day}\u65e5"
            : now.ToString("MMM d", culture);

        ApplyAdaptiveTypography();

        if (isZh)
        {
            LunarDateTextBlock.Text = lunar.LunarDateZh;
            LunarMetaTextBlock.Text = $"{lunar.GanzhiYearZh}\u5e74  {lunar.ZodiacZh}";
            YiLabelTextBlock.Text = "\u5b9c";
            JiLabelTextBlock.Text = "\u5fcc";
        }
        else
        {
            LunarDateTextBlock.Text = $"Lunar {lunar.LunarDateEn}";
            LunarMetaTextBlock.Text = $"{lunar.GanzhiYearEn}  {lunar.ZodiacEn}";
            YiLabelTextBlock.Text = "Do";
            JiLabelTextBlock.Text = "Avoid";
        }

        var itemCount = isZh ? _lunarItemCount : Math.Max(1, _lunarItemCount - 1);
        YiItemsTextBlock.Text = BuildDailySelection(
            now.Date,
            isZh ? ZhYiCandidates : EnYiCandidates,
            count: itemCount,
            salt: 17,
            useChineseSpacing: isZh);
        JiItemsTextBlock.Text = BuildDailySelection(
            now.Date,
            isZh ? ZhJiCandidates : EnJiCandidates,
            count: itemCount,
            salt: 29,
            useChineseSpacing: isZh);

        UpdateWeekdayHeaders(isZh);
        ApplyModeVisualIfNeeded();
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
        _calendarVisibleRows = GetCalendarRowCount(startDayOfWeek, daysInMonth);
        EnsureCalendarRows(_calendarVisibleRows);

        // 4x2 widget has less vertical space than 2x2. Compress only on 6-row months.
        var rowDensity = _calendarVisibleRows >= 6 ? 0.84 : 1.0;
        var dayFontSize = Math.Clamp(_calendarDayFontSize * rowDensity, 8, 24);
        var todayDotSize = Math.Clamp(_calendarTodayDotSize * rowDensity, 13.5, 32);

        for (var day = 1; day <= daysInMonth; day++)
        {
            var row = (day + startDayOfWeek - 1) / 7;
            var col = (day + startDayOfWeek - 1) % 7;
            if (row >= _calendarVisibleRows)
            {
                continue;
            }

            var dayText = new TextBlock
            {
                Text = day.ToString(CultureInfo.CurrentCulture),
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                FontSize = dayFontSize,
                FontWeight = _calendarDayFontWeight,
                LineHeight = dayFontSize * 1.04,
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
                    Width = todayDotSize,
                    Height = todayDotSize,
                    CornerRadius = new CornerRadius(todayDotSize * 0.5),
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
                    ? GetThemeBrush("AdaptiveTextSecondaryBrush", _weekendNumberOpacity)
                    : GetThemeBrush("AdaptiveTextPrimaryBrush", _weekdayNumberOpacity);
                Grid.SetRow(dayText, row);
                Grid.SetColumn(dayText, col);
                CalendarGrid.Children.Add(dayText);
            }
        }
    }

    public void ApplyCellSize(double cellSize)
    {
        _currentCellSize = Math.Max(1, cellSize);
        UpdateDate();
    }

    private void ApplyAdaptiveTypography()
    {
        var scale = ResolveScale();

        RootBorder.CornerRadius = new CornerRadius(Math.Clamp(28 * scale, 16, 40));
        RootBorder.Padding = new Thickness(Math.Clamp(11 * scale, 7, 17));

        LayoutRoot.ColumnSpacing = Math.Clamp(10 * scale, 6, 16);
        LeftPanelGrid.RowSpacing = Math.Clamp(5.2 * scale, 2.5, 10);
        WeekdayHeaderGrid.Margin = new Thickness(
            0,
            Math.Clamp(0.5 * scale, 0, 2),
            0,
            Math.Clamp(2.4 * scale, 1, 4));
        CalendarGrid.Margin = new Thickness(0, 0, 0, Math.Clamp(0.8 * scale, 0, 2));

        LunarCardBorder.CornerRadius = new CornerRadius(Math.Clamp(24 * scale, 14, 34));
        LunarCardBorder.Padding = new Thickness(Math.Clamp(14 * scale, 8, 20));
        RightPanelGrid.RowSpacing = Math.Clamp(7.5 * scale, 3.5, 11);
        DividerBorder.Margin = new Thickness(0, Math.Clamp(1 * scale, 0, 2), 0, Math.Clamp(1 * scale, 0, 2));

        var isZh = CultureInfo.CurrentCulture.TwoLetterISOLanguageName.Equals("zh", StringComparison.OrdinalIgnoreCase);
        var headerTextLength = Math.Max(1, GregorianHeadlineTextBlock.Text?.Length ?? (isZh ? 5 : 6));
        var headerCompression = headerTextLength >= 8 ? 0.90 : headerTextLength >= 6 ? 0.95 : 1.0;
        var densityBoost = scale <= 0.74 ? 0.90 : scale <= 0.90 ? 0.95 : scale >= 1.45 ? 1.05 : 1.0;

        GregorianHeadlineTextBlock.FontSize = Math.Clamp(29 * scale * headerCompression * densityBoost, 12.5, 42);
        GregorianHeadlineTextBlock.FontWeight = ToVariableWeight(Lerp(560, 720, Math.Clamp((scale - 0.60) / 1.2, 0, 1)));
        GregorianHeadlineTextBlock.LineHeight = GregorianHeadlineTextBlock.FontSize * 1.03;

        _weekdayFontSize = Math.Clamp(14.8 * scale * densityBoost, 7, 20);
        _weekdayFontWeight = ToVariableWeight(Lerp(500, 640, Math.Clamp((scale - 0.60) / 1.2, 0, 1)));
        foreach (var block in GetWeekdayHeaderBlocks())
        {
            block.FontSize = _weekdayFontSize;
            block.FontWeight = _weekdayFontWeight;
            block.LineHeight = _weekdayFontSize * 1.02;
        }

        _calendarDayFontSize = Math.Clamp(15.4 * scale * densityBoost, 8, 22);
        _calendarDayFontWeight = ToVariableWeight(Lerp(540, 680, Math.Clamp((scale - 0.60) / 1.2, 0, 1)));
        _calendarTodayDotSize = Math.Clamp(_calendarDayFontSize * 1.30, 13.5, 31);

        var rightDensity = scale <= 0.72 ? 0.90 : scale <= 0.90 ? 0.95 : scale >= 1.38 ? 1.03 : 1.0;
        LunarDateTextBlock.FontSize = Math.Clamp(30 * scale * rightDensity, 14, 44);
        LunarMetaTextBlock.FontSize = Math.Clamp(12.5 * scale * rightDensity, 8.8, 18);
        YiLabelTextBlock.FontSize = Math.Clamp(16.5 * scale * rightDensity, 10, 23);
        JiLabelTextBlock.FontSize = YiLabelTextBlock.FontSize;
        YiItemsTextBlock.FontSize = Math.Clamp(13.8 * scale * rightDensity, 8.5, 19);
        JiItemsTextBlock.FontSize = YiItemsTextBlock.FontSize;
        YiItemsTextBlock.LineHeight = YiItemsTextBlock.FontSize * 1.15;
        JiItemsTextBlock.LineHeight = JiItemsTextBlock.FontSize * 1.15;

        LunarDateTextBlock.FontWeight = ToVariableWeight(Lerp(640, 760, Math.Clamp((scale - 0.60) / 1.2, 0, 1)));
        LunarMetaTextBlock.FontWeight = ToVariableWeight(Lerp(500, 620, Math.Clamp((scale - 0.60) / 1.2, 0, 1)));
        YiLabelTextBlock.FontWeight = ToVariableWeight(Lerp(620, 740, Math.Clamp((scale - 0.60) / 1.2, 0, 1)));
        JiLabelTextBlock.FontWeight = YiLabelTextBlock.FontWeight;
        YiItemsTextBlock.FontWeight = ToVariableWeight(Lerp(520, 660, Math.Clamp((scale - 0.60) / 1.2, 0, 1)));
        JiItemsTextBlock.FontWeight = YiItemsTextBlock.FontWeight;

        var maxLines = scale <= 0.82 ? 1 : 2;
        YiItemsTextBlock.MaxLines = maxLines;
        JiItemsTextBlock.MaxLines = maxLines;

        _lunarItemCount = scale switch
        {
            <= 0.72 => 2,
            <= 0.96 => 3,
            <= 1.32 => 4,
            _ => 5
        };

        if (maxLines == 1)
        {
            _lunarItemCount = Math.Min(_lunarItemCount, 3);
        }
    }

    private void ApplyModeVisualIfNeeded()
    {
        var isNightMode = ResolveIsNightMode();
        if (_isNightModeApplied.HasValue && _isNightModeApplied.Value == isNightMode)
        {
            return;
        }

        _isNightModeApplied = isNightMode;
        ApplyModeVisual(isNightMode);
    }

    private void ApplyModeVisual(bool isNightMode)
    {
        LunarCardBorder.BorderBrush = isNightMode
            ? CreateBrush("#3FFFFFFF")
            : CreateBrush("#14000000");
        LunarCardBorder.BoxShadow = BoxShadows.Parse(isNightMode
            ? "0 10 26 #42000000"
            : "0 8 20 #1A000000");

        _weekdayHeaderOpacity = isNightMode ? 0.66 : 0.60;
        _weekdayNumberOpacity = isNightMode ? 0.93 : 0.90;
        _weekendNumberOpacity = isNightMode ? 0.68 : 0.58;

        GregorianHeadlineTextBlock.Foreground = GetThemeBrush("AdaptiveTextPrimaryBrush", isNightMode ? 0.97 : 0.95);
        LunarDateTextBlock.Foreground = GetThemeBrush("AdaptiveTextPrimaryBrush", isNightMode ? 0.97 : 0.95);
        LunarMetaTextBlock.Foreground = GetThemeBrush("AdaptiveTextSecondaryBrush", isNightMode ? 0.92 : 0.86);
        YiItemsTextBlock.Foreground = GetThemeBrush("AdaptiveTextPrimaryBrush", isNightMode ? 0.95 : 0.92);
        JiItemsTextBlock.Foreground = YiItemsTextBlock.Foreground;

        foreach (var block in GetWeekdayHeaderBlocks())
        {
            block.Foreground = GetThemeBrush("AdaptiveTextSecondaryBrush", _weekdayHeaderOpacity);
        }

        YiLabelTextBlock.Foreground = CreateBrush(isNightMode ? "#8CB57D" : "#4E7D3A");
        JiLabelTextBlock.Foreground = CreateBrush(isNightMode ? "#C98981" : "#A1473E");
        DividerBorder.Opacity = isNightMode ? 0.48 : 0.72;
    }

    private bool ResolveIsNightMode()
    {
        if (ActualThemeVariant == ThemeVariant.Dark)
        {
            return true;
        }

        if (ActualThemeVariant == ThemeVariant.Light)
        {
            return false;
        }

        if (this.TryFindResource("AdaptiveSurfaceBaseBrush", out var value) &&
            value is ISolidColorBrush solidBrush)
        {
            return CalculateRelativeLuminance(solidBrush.Color) < 0.45;
        }

        return false;
    }

    private double ResolveScale()
    {
        var cellScale = Math.Clamp(_currentCellSize / 48d, 0.62, 1.8);
        var heightScale = Bounds.Height > 1 ? Math.Clamp(Bounds.Height / 220d, 0.62, 1.85) : 1;
        var widthScale = Bounds.Width > 1 ? Math.Clamp(Bounds.Width / 460d, 0.62, 1.85) : 1;
        return Math.Clamp(Math.Min(cellScale, Math.Min(heightScale, widthScale) * 1.08), 0.62, 1.8);
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

    private static IBrush CreateBrush(string colorHex)
    {
        return new SolidColorBrush(Color.Parse(colorHex));
    }

    private static string BuildDailySelection(
        DateTime date,
        string[] pool,
        int count,
        int salt,
        bool useChineseSpacing)
    {
        if (pool.Length == 0 || count <= 0)
        {
            return string.Empty;
        }

        var target = Math.Min(count, pool.Length);
        var selected = new List<string>(target);
        var usedIndices = new HashSet<int>();
        var cursor = Math.Abs(date.Year * 1009 + date.DayOfYear * 37 + salt * 211);
        var step = (salt % Math.Max(1, pool.Length - 1)) + 1;

        for (var i = 0; i < pool.Length * 3 && selected.Count < target; i++)
        {
            var index = (cursor + i * step) % pool.Length;
            if (usedIndices.Add(index))
            {
                selected.Add(pool[index]);
            }
        }

        if (selected.Count == 0)
        {
            return string.Empty;
        }

        return string.Join(useChineseSpacing ? " " : ", ", selected);
    }

    private static double Lerp(double from, double to, double t)
    {
        return from + ((to - from) * t);
    }

    private static FontWeight ToVariableWeight(double weight)
    {
        return (FontWeight)(int)Math.Clamp(Math.Round(weight), 1, 1000);
    }

    private static int GetCalendarRowCount(int startDayOfWeek, int daysInMonth)
    {
        return Math.Max(5, (int)Math.Ceiling((startDayOfWeek + daysInMonth) / 7d));
    }

    private void EnsureCalendarRows(int rowCount)
    {
        if (CalendarGrid.RowDefinitions.Count == rowCount)
        {
            return;
        }

        CalendarGrid.RowDefinitions.Clear();
        for (var i = 0; i < rowCount; i++)
        {
            CalendarGrid.RowDefinitions.Add(new RowDefinition(GridLength.Star));
        }
    }

    private static double CalculateRelativeLuminance(Color color)
    {
        static double ToLinear(double channel)
        {
            return channel <= 0.03928
                ? channel / 12.92
                : Math.Pow((channel + 0.055) / 1.055, 2.4);
        }

        var r = ToLinear(color.R / 255d);
        var g = ToLinear(color.G / 255d);
        var b = ToLinear(color.B / 255d);
        return 0.2126 * r + 0.7152 * g + 0.0722 * b;
    }
}
