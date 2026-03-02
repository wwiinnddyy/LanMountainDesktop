using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using LanMontainDesktop.Services;

namespace LanMontainDesktop.Views.Components;

public partial class HolidayCalendarWidget : UserControl
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

        TriggerContentRefresh();
    }

    public void SetTimeZoneService(TimeZoneService timeZoneService)
    {
        if (_timeZoneService is not null)
        {
            _timeZoneService.TimeZoneChanged -= OnTimeZoneChanged;
        }

        _timeZoneService = timeZoneService;
        _timeZoneService.TimeZoneChanged += OnTimeZoneChanged;
        TriggerContentRefresh();
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
    }

    public void ApplyCellSize(double cellSize)
    {
        _currentCellSize = Math.Max(1, cellSize);
        var scale = ResolveScale();

        RootBorder.CornerRadius = new CornerRadius(Math.Clamp(34 * scale, 15, 50));
        RootBorder.Padding = new Thickness(Math.Clamp(14 * scale, 7, 22));
        LayoutRoot.RowSpacing = Math.Clamp(8 * scale, 4, 14);

        TitleTextBlock.FontSize = Math.Clamp(24 * scale, 11, 36);
        CountTextBlock.FontSize = Math.Clamp(120 * scale, 36, 160);
        DayUnitTextBlock.FontSize = Math.Clamp(56 * scale, 16, 78);
        DateTextBlock.FontSize = Math.Clamp(34 * scale, 12, 50);
    }

    private double ResolveScale()
    {
        var cellScale = Math.Clamp(_currentCellSize / 44d, 0.60, 1.95);
        var heightScale = Bounds.Height > 1 ? Math.Clamp(Bounds.Height / 300d, 0.58, 2.0) : 1;
        var widthScale = Bounds.Width > 1 ? Math.Clamp(Bounds.Width / 300d, 0.58, 2.0) : 1;
        return Math.Clamp(Math.Min(cellScale, Math.Min(heightScale, widthScale) * 1.05), 0.58, 1.95);
    }
}
