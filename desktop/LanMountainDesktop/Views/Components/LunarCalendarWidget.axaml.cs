using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using LanMountainDesktop.Services;

namespace LanMountainDesktop.Views.Components;

public partial class LunarCalendarWidget : UserControl, IDesktopComponentWidget, ITimeZoneAwareComponentWidget
{
    private readonly DispatcherTimer _timer = new()
    {
        Interval = TimeSpan.FromMinutes(1)
    };

    private static readonly LunarCalendarService LunarCalendarService = new();

    private TimeZoneService? _timeZoneService;
    private double _currentCellSize = ComponentDesignMetrics.BaseCellSize;
    private FontWeight _gregorianLineWeight = FontWeight.SemiBold;
    private FontWeight _lunarDateWeight = FontWeight.Bold;
    private FontWeight _labelWeight = FontWeight.Bold;
    private FontWeight _itemsWeight = FontWeight.SemiBold;
    private int _auspiciousItemCount = 4;

    public LunarCalendarWidget()
    {
        InitializeComponent();

        _timer.Tick += OnTimerTick;
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
        SizeChanged += OnSizeChanged;
        UpdateContent();
    }

    public void SetTimeZoneService(TimeZoneService timeZoneService)
    {
        _timeZoneService = TimeZoneServiceBinding.Replace(
            _timeZoneService,
            timeZoneService,
            OnTimeZoneChanged);
        UpdateContent();
    }

    public void ClearTimeZoneService()
    {
        _timeZoneService = TimeZoneServiceBinding.Clear(_timeZoneService, OnTimeZoneChanged);
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        UpdateContent();
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
        UpdateContent();
    }

    private void OnTimeZoneChanged(object? sender, EventArgs e)
    {
        UpdateContent();
    }

    private void UpdateContent()
    {
        ApplyAdaptiveTypography();

        var now = _timeZoneService?.GetCurrentTime() ?? DateTime.Now;
        var culture = CultureInfo.CurrentCulture;
        var isZh = culture.TwoLetterISOLanguageName.Equals("zh", StringComparison.OrdinalIgnoreCase);
        var lunar = LunarCalendarService.GetLunarInfo(now);

        GregorianLineTextBlock.Text = isZh
            ? $"{now.Month}\u6708{now.Day}\u65e5 {ToChineseWeekday(now.DayOfWeek)}"
            : now.ToString("MMM d ddd", culture);

        LunarDateTextBlock.Text = isZh ? lunar.LunarDateZh : lunar.LunarDateEn;
        YiLabelTextBlock.Text = isZh ? "\u5b9c" : "Do";
        JiLabelTextBlock.Text = isZh ? "\u5fcc" : "Avoid";
        YiItemsTextBlock.Text = BuildDailySelection(
            now.Date,
            isZh ? LunarCalendarService.YiCandidatesZh : LunarCalendarService.YiCandidatesEn,
            count: _auspiciousItemCount,
            salt: 17,
            useChineseSpacing: isZh);
        JiItemsTextBlock.Text = BuildDailySelection(
            now.Date,
            isZh ? LunarCalendarService.JiCandidatesZh : LunarCalendarService.JiCandidatesEn,
            count: _auspiciousItemCount,
            salt: 29,
            useChineseSpacing: isZh);
    }

    public void ApplyCellSize(double cellSize)
    {
        ComponentDesignMetrics.ApplyCellSize(
            ref _currentCellSize, cellSize, UpdateContent);
    }

    private void ApplyAdaptiveTypography()
    {
        var scale = ResolveScale();
        var mainRectangleCornerRadius = ComponentChromeCornerRadiusHelper.ResolveMainRectangleRadius();

        RootBorder.CornerRadius = mainRectangleCornerRadius;
        RootBorder.Padding = new Thickness(ComponentChromeCornerRadiusHelper.SafeValue(16 * scale, 8, 24));
        LayoutRoot.RowSpacing = Math.Clamp(10 * scale, 5, 18);
        DividerBorder.Margin = new Thickness(
            Math.Clamp(8 * scale, 3, 14),
            Math.Clamp(8 * scale, 3, 14),
            Math.Clamp(8 * scale, 3, 14),
            Math.Clamp(2 * scale, 1, 6));
        AuspiciousGrid.RowSpacing = Math.Clamp(12 * scale, 6, 20);

        var densityBoost = scale <= 0.72 ? 0.90 : scale <= 0.88 ? 0.95 : scale >= 1.42 ? 1.04 : 1.0;
        GregorianLineTextBlock.FontSize = Math.Clamp(24 * scale * densityBoost, 10, 38);
        LunarDateTextBlock.FontSize = Math.Clamp(88 * scale * densityBoost, 28, 134);
        YiLabelTextBlock.FontSize = Math.Clamp(30 * scale * densityBoost, 12, 46);
        JiLabelTextBlock.FontSize = YiLabelTextBlock.FontSize;
        YiItemsTextBlock.FontSize = Math.Clamp(24 * scale * densityBoost, 10, 36);
        JiItemsTextBlock.FontSize = YiItemsTextBlock.FontSize;

        _gregorianLineWeight = ComponentTypography.ToVariableWeight(ComponentTypography.Lerp(500, 640, Math.Clamp((scale - 0.58) / 1.2, 0, 1)));
        _lunarDateWeight = ComponentTypography.ToVariableWeight(ComponentTypography.Lerp(650, 780, Math.Clamp((scale - 0.58) / 1.2, 0, 1)));
        _labelWeight = ComponentTypography.ToVariableWeight(ComponentTypography.Lerp(620, 760, Math.Clamp((scale - 0.58) / 1.2, 0, 1)));
        _itemsWeight = ComponentTypography.ToVariableWeight(ComponentTypography.Lerp(520, 670, Math.Clamp((scale - 0.58) / 1.2, 0, 1)));

        GregorianLineTextBlock.FontWeight = _gregorianLineWeight;
        LunarDateTextBlock.FontWeight = _lunarDateWeight;
        YiLabelTextBlock.FontWeight = _labelWeight;
        JiLabelTextBlock.FontWeight = _labelWeight;
        YiItemsTextBlock.FontWeight = _itemsWeight;
        JiItemsTextBlock.FontWeight = _itemsWeight;

        _auspiciousItemCount = scale switch
        {
            <= 0.72 => 2,
            <= 0.92 => 3,
            <= 1.30 => 4,
            _ => 5
        };
    }

    private double ResolveScale()
    {
        var cellScale = Math.Clamp(_currentCellSize / 44d, 0.62, 1.95);
        var heightScale = Bounds.Height > 1 ? Math.Clamp(Bounds.Height / 300d, 0.58, 2.0) : 1;
        var widthScale = Bounds.Width > 1 ? Math.Clamp(Bounds.Width / 300d, 0.58, 2.0) : 1;
        return Math.Clamp(Math.Min(cellScale, Math.Min(heightScale, widthScale) * 1.05), 0.58, 1.95);
    }

    private static string ToChineseWeekday(DayOfWeek dayOfWeek)
    {
        return dayOfWeek switch
        {
            DayOfWeek.Sunday => "\u5468\u65e5",
            DayOfWeek.Monday => "\u5468\u4e00",
            DayOfWeek.Tuesday => "\u5468\u4e8c",
            DayOfWeek.Wednesday => "\u5468\u4e09",
            DayOfWeek.Thursday => "\u5468\u56db",
            DayOfWeek.Friday => "\u5468\u4e94",
            _ => "\u5468\u516d"
        };
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
}
