using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using LanMontainDesktop.Services;

namespace LanMontainDesktop.Views.Components;

public partial class HolidayCalendarWidget : UserControl, IDesktopComponentWidget, ITimeZoneAwareComponentWidget
{
    private readonly DispatcherTimer _timer = new()
    {
        Interval = TimeSpan.FromMinutes(15)
    };

    private static readonly HolidayCalendarService HolidayService = new();

    private TimeZoneService? _timeZoneService;
    private double _currentCellSize = 48;
    private CancellationTokenSource? _refreshCts;
    private long _refreshVersion;

    public HolidayCalendarWidget()
    {
        InitializeComponent();

        _timer.Tick += OnTimerTick;
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
        SizeChanged += OnSizeChanged;

        ApplyCellSize(_currentCellSize);
        TriggerContentRefresh();
    }

    public void SetTimeZoneService(TimeZoneService timeZoneService)
    {
        ClearTimeZoneService();
        _timeZoneService = timeZoneService;
        _timeZoneService.TimeZoneChanged += OnTimeZoneChanged;
        TriggerContentRefresh();
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
        TriggerContentRefresh();
        _timer.Start();
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _timer.Stop();
        _refreshCts?.Cancel();
        _refreshCts?.Dispose();
        _refreshCts = null;
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        ApplyCellSize(_currentCellSize);
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        TriggerContentRefresh();
    }

    private void OnTimeZoneChanged(object? sender, EventArgs e)
    {
        TriggerContentRefresh();
    }

    private void TriggerContentRefresh()
    {
        _refreshCts?.Cancel();
        _refreshCts?.Dispose();
        _refreshCts = new CancellationTokenSource();

        var now = _timeZoneService?.GetCurrentTime() ?? DateTime.Now;
        var isZh = CultureInfo.CurrentCulture.TwoLetterISOLanguageName.Equals("zh", StringComparison.OrdinalIgnoreCase);
        var version = Interlocked.Increment(ref _refreshVersion);
        _ = UpdateContentAsync(now, isZh, version, _refreshCts.Token);
    }

    private async Task UpdateContentAsync(DateTime now, bool isZh, long refreshVersion, CancellationToken cancellationToken)
    {
        HolidayDisplayInfo displayInfo;
        try
        {
            displayInfo = await HolidayService.GetDisplayInfoAsync(now, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch
        {
            var fallbackDayType = now.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday
                ? HolidayDayType.Weekend
                : HolidayDayType.Workday;

            displayInfo = new HolidayDisplayInfo(
                NextHoliday: HolidayService.GetNextHoliday(now),
                TodayStatus: new HolidayDayStatus(
                    Date: DateOnly.FromDateTime(now.Date),
                    DayType: fallbackDayType,
                    TypeNameZh: fallbackDayType == HolidayDayType.Weekend ? "\u5468\u672b" : "\u5de5\u4f5c\u65e5",
                    IsHoliday: false,
                    IsAdjustedWorkday: false,
                    NameZh: null,
                    NameEn: null,
                    TargetHolidayZh: null),
                UsesOnlineData: false);
        }

        if (cancellationToken.IsCancellationRequested ||
            refreshVersion != Volatile.Read(ref _refreshVersion))
        {
            return;
        }

        var holiday = displayInfo.NextHoliday;

        if (holiday is null)
        {
            TitleTextBlock.Text = isZh
                ? "\u6682\u65e0\u8282\u5047\u65e5\u6570\u636e"
                : "No holiday data";
            CountTextBlock.Text = "--";
            DayUnitTextBlock.Text = isZh ? "\u5929" : "Days";
            DateTextBlock.Text = "--";
            ApplyCellSize(_currentCellSize);
            return;
        }

        var today = DateOnly.FromDateTime(now.Date);
        var remainDays = Math.Max(0, holiday.Date.DayNumber - today.DayNumber);
        CountTextBlock.Text = remainDays.ToString(CultureInfo.InvariantCulture);

        if (isZh)
        {
            if (remainDays == 0)
            {
                TitleTextBlock.Text = $"{holiday.NameZh}\u4eca\u5929";
            }
            else
            {
                var adjustPrefix = displayInfo.TodayStatus.IsAdjustedWorkday
                    ? string.IsNullOrWhiteSpace(displayInfo.TodayStatus.NameZh)
                        ? "\u4eca\u65e5\u8c03\u4f11\u8865\u73ed\uff0c"
                        : string.Create(CultureInfo.InvariantCulture, $"\u4eca\u65e5{displayInfo.TodayStatus.NameZh}\uff0c")
                    : string.Empty;
                TitleTextBlock.Text = string.Create(
                    CultureInfo.InvariantCulture,
                    $"{adjustPrefix}\u8ddd{holiday.NameZh}\u8fd8\u6709");
            }

            DayUnitTextBlock.Text = "\u5929";

            var holidayDateText = HolidayCalendarService.FormatDate(holiday.Date, isZh: true);
            DateTextBlock.Text = displayInfo.TodayStatus.IsAdjustedWorkday && remainDays > 0
                ? string.Create(CultureInfo.InvariantCulture, $"{holidayDateText} \u00b7 \u4eca\u65e5\u8865\u73ed")
                : holidayDateText;
        }
        else
        {
            if (remainDays == 0)
            {
                TitleTextBlock.Text = $"{holiday.NameEn} is today";
            }
            else
            {
                var adjustPrefix = displayInfo.TodayStatus.IsAdjustedWorkday
                    ? "Make-up workday today, "
                    : string.Empty;
                TitleTextBlock.Text = $"{adjustPrefix}Days to {holiday.NameEn}";
            }

            DayUnitTextBlock.Text = "Days";

            var holidayDateText = HolidayCalendarService.FormatDate(holiday.Date, isZh: false);
            DateTextBlock.Text = displayInfo.TodayStatus.IsAdjustedWorkday && remainDays > 0
                ? $"{holidayDateText} - make-up workday"
                : holidayDateText;
        }

        ApplyCellSize(_currentCellSize);
    }

    public void ApplyCellSize(double cellSize)
    {
        _currentCellSize = Math.Max(1, cellSize);
        var width = Bounds.Width > 1 ? Bounds.Width : 220;
        var height = Bounds.Height > 1 ? Bounds.Height : 220;
        var shortSide = Math.Min(width, height);
        var scale = ResolveScale(width, height);
        var isCompact = width < 170 || height < 170;
        var isUltraCompact = width < 130 || height < 130;
        var titleUnits = GetDisplayUnits(TitleTextBlock.Text);
        var dateUnits = GetDisplayUnits(DateTextBlock.Text);
        var titleNeedsTwoLines = isUltraCompact || titleUnits >= (isCompact ? 13 : 17);
        var dateNeedsTwoLines = isUltraCompact || dateUnits >= (isCompact ? 15 : 20);

        RootBorder.CornerRadius = new CornerRadius(Math.Clamp(shortSide * 0.13, 10, 46));
        var padding = Math.Clamp(shortSide * 0.05, 4.5, 21);
        RootBorder.Padding = new Thickness(padding);
        LayoutRoot.RowSpacing = Math.Clamp(shortSide * 0.028, 2.2, 12);
        var rowWeights = ApplyAdaptiveRowHeights(isCompact, isUltraCompact, titleNeedsTwoLines, dateNeedsTwoLines);

        var innerWidth = Math.Max(1, width - padding * 2);
        var innerHeight = Math.Max(1, height - padding * 2);
        var totalWeight = Math.Max(0.001, rowWeights[0] + rowWeights[1] + rowWeights[2] + rowWeights[3] + rowWeights[4]);
        var row0Height = innerHeight * (rowWeights[0] / totalWeight);
        var row1Height = innerHeight * (rowWeights[1] / totalWeight);
        var row3Height = innerHeight * (rowWeights[3] / totalWeight);
        var row4Height = innerHeight * (rowWeights[4] / totalWeight);
        var horizontalMargin = Math.Clamp(8 * scale, 4, 14);
        var titleMaxWidth = Math.Max(24, innerWidth - horizontalMargin * 2);
        var dateMaxWidth = titleMaxWidth;

        var titlePreferred = Math.Clamp(24 * scale, 8.8, 34);
        var titleHeightCap = Math.Max(10, row0Height * 0.94);
        var titleLineCount = titleNeedsTwoLines ? 2 : 1;
        TitleTextBlock.MaxLines = titleLineCount;
        TitleTextBlock.TextWrapping = titleLineCount > 1 ? TextWrapping.Wrap : TextWrapping.NoWrap;
        TitleTextBlock.Margin = new Thickness(horizontalMargin, 0, horizontalMargin, 0);
        TitleTextBlock.FontSize = FitTextSize(
            TitleTextBlock.Text,
            TitleTextBlock.FontWeight,
            Math.Min(titlePreferred, Math.Max(8.8, row0Height * 0.62)),
            8.6,
            titleMaxWidth,
            titleHeightCap,
            titleLineCount,
            lineHeightFactor: 1.10);
        TitleTextBlock.LineHeight = TitleTextBlock.FontSize * 1.10;

        var digitCount = Math.Max(1, CountTextBlock.Text?.Trim().Length ?? 1);
        var digitCompression = digitCount switch
        {
            >= 5 => 0.68,
            4 => 0.8,
            3 => 0.9,
            _ => 1.0
        };
        var countCompactFactor = isUltraCompact ? 0.86 : isCompact ? 0.93 : 1.0;
        var countPreferred = Math.Clamp(132 * scale * digitCompression * countCompactFactor, 28, 170);
        var countHeightCap = Math.Max(30, row1Height * 0.96);
        CountTextBlock.FontSize = FitTextSize(
            CountTextBlock.Text,
            CountTextBlock.FontWeight,
            Math.Min(countPreferred, Math.Max(28, row1Height * 0.9)),
            24,
            titleMaxWidth,
            countHeightCap,
            maxLines: 1,
            lineHeightFactor: 1.08);
        CountTextBlock.LineHeight = CountTextBlock.FontSize * 1.08;

        var unitCompactFactor = isUltraCompact ? 0.8 : isCompact ? 0.9 : 1.0;
        DayUnitTextBlock.FontSize = Math.Clamp(52 * scale * unitCompactFactor, 10, 72);
        DayUnitTextBlock.FontSize = Math.Min(DayUnitTextBlock.FontSize, Math.Max(10, row3Height * 0.64));
        DayUnitTextBlock.LineHeight = DayUnitTextBlock.FontSize * 1.02;

        var dateCompactFactor = isUltraCompact ? 0.84 : isCompact ? 0.92 : 1.0;
        var datePreferred = Math.Clamp(32 * scale * dateCompactFactor, 9, 46);
        var dateHeightCap = Math.Max(10, row4Height * 0.96);
        var dateLineCount = dateNeedsTwoLines ? 2 : 1;
        DateTextBlock.MaxLines = dateLineCount;
        DateTextBlock.TextWrapping = dateLineCount > 1 ? TextWrapping.Wrap : TextWrapping.NoWrap;
        DateTextBlock.Margin = new Thickness(horizontalMargin, 0, horizontalMargin, 0);
        DateTextBlock.FontSize = FitTextSize(
            DateTextBlock.Text,
            DateTextBlock.FontWeight,
            Math.Min(datePreferred, Math.Max(9, row4Height * 0.58)),
            8.5,
            dateMaxWidth,
            dateHeightCap,
            dateLineCount,
            lineHeightFactor: 1.12);
        DateTextBlock.LineHeight = DateTextBlock.FontSize * 1.12;
    }

    private double[] ApplyAdaptiveRowHeights(
        bool isCompact,
        bool isUltraCompact,
        bool titleNeedsTwoLines,
        bool dateNeedsTwoLines)
    {
        var weights = isUltraCompact
            ? new[] { 1.35, 2.55, 0.48, 0.6, 0.82 }
            : isCompact
                ? new[] { 1.2, 2.45, 0.56, 0.7, 0.9 }
                : new[] { 1.1, 2.3, 0.62, 0.78, 0.95 };

        if (titleNeedsTwoLines)
        {
            weights[0] += 0.36;
            weights[1] -= 0.21;
            weights[2] -= 0.08;
            weights[3] -= 0.07;
        }

        if (dateNeedsTwoLines)
        {
            weights[4] += 0.42;
            weights[1] -= 0.23;
            weights[2] -= 0.10;
            weights[3] -= 0.09;
        }

        weights[0] = Math.Max(0.92, weights[0]);
        weights[1] = Math.Max(1.45, weights[1]);
        weights[2] = Math.Max(0.34, weights[2]);
        weights[3] = Math.Max(0.44, weights[3]);
        weights[4] = Math.Max(0.72, weights[4]);

        if (LayoutRoot.RowDefinitions.Count < 5)
        {
            return weights;
        }

        for (var i = 0; i < 5; i++)
        {
            LayoutRoot.RowDefinitions[i].Height = new GridLength(weights[i], GridUnitType.Star);
        }

        return weights;
    }

    private static int GetDisplayUnits(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        var units = 0;
        foreach (var ch in text.Trim())
        {
            if (char.IsWhiteSpace(ch))
            {
                continue;
            }

            units += ch > 0x7F ? 2 : 1;
        }

        return units;
    }

    private static double FitTextSize(
        string? text,
        FontWeight fontWeight,
        double preferredSize,
        double minSize,
        double maxWidth,
        double maxHeight,
        int maxLines,
        double lineHeightFactor)
    {
        var safeText = string.IsNullOrWhiteSpace(text) ? " " : text.Trim();
        var safeMaxWidth = Math.Max(1, maxWidth);
        var safeMaxHeight = Math.Max(1, maxHeight);
        var safeMaxLines = Math.Max(1, maxLines);

        var probe = new TextBlock
        {
            Text = safeText,
            FontWeight = fontWeight,
            MaxLines = safeMaxLines,
            TextWrapping = safeMaxLines > 1 ? TextWrapping.Wrap : TextWrapping.NoWrap
        };

        for (var size = preferredSize; size >= minSize; size -= 0.5)
        {
            probe.FontSize = size;
            probe.LineHeight = size * lineHeightFactor;
            probe.Measure(new Size(safeMaxWidth, double.PositiveInfinity));
            var desired = probe.DesiredSize;
            if (desired.Width <= safeMaxWidth + 0.6 &&
                desired.Height <= safeMaxHeight + 0.6)
            {
                return size;
            }
        }

        return minSize;
    }

    private double ResolveScale(double width, double height)
    {
        var cellScale = Math.Clamp(_currentCellSize / 44d, 0.56, 2.0);
        var widthScale = Math.Clamp(width / 220d, 0.5, 2.0);
        var heightScale = Math.Clamp(height / 220d, 0.5, 2.0);
        return Math.Clamp(Math.Min(cellScale, Math.Min(widthScale, heightScale) * 1.02), 0.5, 2.0);
    }
}
