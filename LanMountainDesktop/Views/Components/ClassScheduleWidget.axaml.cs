using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using LanMountainDesktop.DesktopComponents.Runtime;
using LanMountainDesktop.ComponentSystem;
using LanMountainDesktop.Models;
using LanMountainDesktop.PluginSdk;
using LanMountainDesktop.Services;
using LanMountainDesktop.Services.Settings;

namespace LanMountainDesktop.Views.Components;

public partial class ClassScheduleWidget : UserControl, IDesktopComponentWidget, ITimeZoneAwareComponentWidget, IComponentPlacementContextAware
{
    private sealed record CourseItemViewModel(
        string Name,
        string TimeRange,
        string Detail,
        bool IsCurrent);

    private readonly DispatcherTimer _refreshTimer = new()
    {
        Interval = TimeSpan.FromMinutes(1)
    };

    private int _lastCurrentCourseIndex = -1;
    private DateOnly _lastRefreshDate = DateOnly.MinValue;

    private bool _isUserScrolling;
    private Vector _lastScrollOffset;
    private Point _dragStartPoint;
    private Point _lastDragPoint;

    private ISettingsService _settingsService = HostSettingsFacadeProvider.GetOrCreate().Settings;
    private readonly LocalizationService _localizationService = new();
    private readonly IClassIslandScheduleDataService _scheduleService = new ClassIslandScheduleDataService();

    private TimeZoneService? _timeZoneService;
    private double _currentCellSize = 48;
    private double _layoutScale = 1d;
    private IReadOnlyList<CourseItemViewModel> _courseItems = Array.Empty<CourseItemViewModel>();
    private bool _isNightVisual = true;
    private string _languageCode = "zh-CN";
    private string _componentId = BuiltInComponentIds.DesktopClassSchedule;
    private string _placementId = string.Empty;
    private string? _componentColorScheme;

    public ClassScheduleWidget()
    {
        InitializeComponent();

        _refreshTimer.Tick += OnRefreshTimerTick;
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
        SizeChanged += OnSizeChanged;
        ActualThemeVariantChanged += OnActualThemeVariantChanged;

        ContentScrollViewer.PointerPressed += OnScrollViewerPointerPressed;
        ContentScrollViewer.PointerMoved += OnScrollViewerPointerMoved;
        ContentScrollViewer.PointerReleased += OnScrollViewerPointerReleased;

        ApplyCellSize(_currentCellSize);
        RefreshSchedule();
    }

    public void ApplyCellSize(double cellSize)
    {
        _currentCellSize = Math.Max(1, cellSize);
        ApplyAdaptiveLayout();
        RenderScheduleItems();
    }

    public void SetTimeZoneService(TimeZoneService timeZoneService)
    {
        ClearTimeZoneService();
        _timeZoneService = timeZoneService;
        _timeZoneService.TimeZoneChanged += OnTimeZoneChanged;
        RefreshSchedule();
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
        _refreshTimer.Start();
        RefreshSchedule();
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _refreshTimer.Stop();
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        ApplyCellSize(_currentCellSize);
    }

    private void OnActualThemeVariantChanged(object? sender, EventArgs e)
    {
        ApplyAdaptiveLayout();
        RenderScheduleItems();
    }

    private void OnTimeZoneChanged(object? sender, EventArgs e)
    {
        RefreshSchedule();
    }

    private void OnScrollViewerPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _isUserScrolling = true;
        _dragStartPoint = e.GetCurrentPoint(ContentScrollViewer).Position;
        _lastDragPoint = _dragStartPoint;
        _lastScrollOffset = ContentScrollViewer.Offset;
    }

    private void OnScrollViewerPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isUserScrolling)
        {
            return;
        }

        var currentPoint = e.GetCurrentPoint(ContentScrollViewer);
        var currentPosition = currentPoint.Position;
        var deltaY = currentPosition.Y - _lastDragPoint.Y;

        var newOffset = _lastScrollOffset;
        newOffset = newOffset.WithY(newOffset.Y - deltaY);

        ContentScrollViewer.Offset = newOffset;
        _lastDragPoint = currentPosition;
    }

    private void OnScrollViewerPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _lastScrollOffset = ContentScrollViewer.Offset;
    }

    private void OnRefreshTimerTick(object? sender, EventArgs e)
    {
        var now = _timeZoneService?.GetCurrentTime() ?? DateTime.Now;
        var currentDate = DateOnly.FromDateTime(now);
        
        var previousCourseIndex = _lastCurrentCourseIndex;
        
        RefreshSchedule();
        
        var newCurrentCourseIndex = FindCurrentCourseIndex();
        _lastCurrentCourseIndex = newCurrentCourseIndex;
        
        if (previousCourseIndex != newCurrentCourseIndex && newCurrentCourseIndex >= 0)
        {
            if (_isUserScrolling)
            {
                _isUserScrolling = false;
            }
            ScrollToCurrentCourse(newCurrentCourseIndex);
        }
        
        if (_lastRefreshDate != currentDate && currentDate > _lastRefreshDate)
        {
            _lastRefreshDate = currentDate;
        }
    }

    private int FindCurrentCourseIndex()
    {
        for (var i = 0; i < _courseItems.Count; i++)
        {
            if (_courseItems[i].IsCurrent)
            {
                return i;
            }
        }
        return -1;
    }

    private void ScrollToCurrentCourse(int courseIndex)
    {
        if (courseIndex < 0 || courseIndex >= _courseItems.Count)
        {
            return;
        }

        // 确保在UI线程执行
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (courseIndex >= CourseListPanel.Children.Count)
            {
                return;
            }

            var targetChild = CourseListPanel.Children[courseIndex];
            if (targetChild == null || !targetChild.IsArrangeValid)
            {
                return;
            }

            var bounds = targetChild.Bounds;
            var scrollViewerHeight = ContentScrollViewer.Bounds.Height;
            var contentHeight = CourseListPanel.Bounds.Height;
            
            // 计算滚动位置，使当前课程居中显示
            var targetOffset = bounds.Position.Y - (scrollViewerHeight / 2) + (bounds.Height / 2);
            
            // 确保不超出边界
            targetOffset = Math.Max(0, Math.Min(targetOffset, contentHeight - scrollViewerHeight));
            
            ContentScrollViewer.Offset = new Vector(0, targetOffset);
        }, Avalonia.Threading.DispatcherPriority.Loaded);
    }

    public void RefreshFromSettings()
    {
        RefreshSchedule();
    }

    public void SetComponentPlacementContext(string componentId, string? placementId)
    {
        _componentId = string.IsNullOrWhiteSpace(componentId)
            ? BuiltInComponentIds.DesktopClassSchedule
            : componentId.Trim();
        _placementId = placementId?.Trim() ?? string.Empty;
        RefreshSchedule();
    }

    private void RefreshSchedule()
    {
        var appSettings = _settingsService.LoadSnapshot<AppSettingsSnapshot>(SettingsScope.App);
        var componentSettings = _settingsService.LoadSnapshot<ComponentSettingsSnapshot>(
            SettingsScope.ComponentInstance,
            _componentId,
            _placementId);
        _languageCode = _localizationService.NormalizeLanguageCode(appSettings.LanguageCode);
        _componentColorScheme = componentSettings.ColorSchemeSource;
        var now = _timeZoneService?.GetCurrentTime() ?? DateTime.Now;
        var today = DateOnly.FromDateTime(now);

        var importedSchedulePath = ResolveImportedSchedulePath(componentSettings);
        var readResult = _scheduleService.Load(importedSchedulePath);
        if (!readResult.Success || readResult.Snapshot is null)
        {
            _courseItems = Array.Empty<CourseItemViewModel>();
            UpdateHeader(now);
            ShowStatus(L("schedule.widget.no_source", "未读取到 ClassIsland 课表"));
            RenderScheduleItems();
            return;
        }

        var snapshot = readResult.Snapshot;
        
        if (!_scheduleService.TryResolveClassPlanForDate(snapshot, today, out var resolvedClassPlan))
        {
            var nextDay = today.AddDays(1);
            if (_scheduleService.TryResolveClassPlanForDate(snapshot, nextDay, out var nextDayClassPlan))
            {
                resolvedClassPlan = nextDayClassPlan;
                today = nextDay;
            }
            else
            {
                _courseItems = Array.Empty<CourseItemViewModel>();
                UpdateHeader(now);
                ShowStatus(L("schedule.widget.no_class_today", "今天没有课程"));
                RenderScheduleItems();
                return;
            }
        }

        if (!snapshot.TimeLayouts.TryGetValue(resolvedClassPlan.ClassPlan.TimeLayoutId, out var layout))
        {
            _courseItems = Array.Empty<CourseItemViewModel>();
            UpdateHeader(now);
            ShowStatus(L("schedule.widget.layout_missing", "课表时间布局缺失"));
            RenderScheduleItems();
            return;
        }

        var adjustedNow = today == DateOnly.FromDateTime(now) ? now : DateTime.Today.AddHours(8);
        _courseItems = BuildCourseItemViewModels(snapshot, resolvedClassPlan.ClassPlan, layout, adjustedNow);
        
        if (_courseItems.Count == 0)
        {
            var nextDay = today.AddDays(1);
            if (_scheduleService.TryResolveClassPlanForDate(snapshot, nextDay, out var nextDayClassPlan) &&
                snapshot.TimeLayouts.TryGetValue(nextDayClassPlan.ClassPlan.TimeLayoutId, out var nextLayout))
            {
                today = nextDay;
                adjustedNow = DateTime.Today.AddHours(8);
                _courseItems = BuildCourseItemViewModels(snapshot, nextDayClassPlan.ClassPlan, nextLayout, adjustedNow);
            }
        }

        UpdateHeader(today.ToDateTime(TimeOnly.MinValue));
        
        if (_courseItems.Count == 0)
        {
            ShowStatus(L("schedule.widget.no_class_today", "今天没有课程"));
        }
        else
        {
            var currentIndex = FindCurrentCourseIndex();
            _lastCurrentCourseIndex = currentIndex;
            HideStatus();
            
            // 初始化时自动跳转到当前课程
            if (currentIndex >= 0)
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    ScrollToCurrentCourse(currentIndex);
                }, Avalonia.Threading.DispatcherPriority.Loaded);
            }
        }

        RenderScheduleItems();
    }

    private IReadOnlyList<CourseItemViewModel> BuildCourseItemViewModels(
        ClassIslandScheduleSnapshot snapshot,
        ClassIslandClassPlan classPlan,
        ClassIslandTimeLayout layout,
        DateTime now)
    {
        var teachingSlots = layout.Items
            .Where(static item => item.TimeType == 0)
            .ToList();
        if (teachingSlots.Count == 0 || classPlan.Classes.Count == 0)
        {
            return Array.Empty<CourseItemViewModel>();
        }

        var result = new List<CourseItemViewModel>(teachingSlots.Count);
        var max = Math.Min(teachingSlots.Count, classPlan.Classes.Count);
        for (var i = 0; i < max; i++)
        {
            var classInfo = classPlan.Classes[i];
            if (!classInfo.IsEnabled)
            {
                continue;
            }

            var slot = teachingSlots[i];
            var subjectName = ResolveSubjectName(snapshot, classInfo.SubjectId);
            var detail = ResolveSubjectDetail(snapshot, classInfo.SubjectId);
            var isCurrent = now.TimeOfDay >= slot.StartTime && now.TimeOfDay <= slot.EndTime;
            result.Add(new CourseItemViewModel(
                Name: subjectName,
                TimeRange: $"{FormatTime(slot.StartTime)}-{FormatTime(slot.EndTime)}",
                Detail: detail,
                IsCurrent: isCurrent));
        }

        return result;
    }

    private string ResolveSubjectName(ClassIslandScheduleSnapshot snapshot, Guid? subjectId)
    {
        if (subjectId.HasValue &&
            snapshot.Subjects.TryGetValue(subjectId.Value, out var subject) &&
            !string.IsNullOrWhiteSpace(subject.Name))
        {
            return subject.Name.Trim();
        }

        return L("schedule.widget.subject_fallback", "未命名课程");
    }

    private string ResolveSubjectDetail(ClassIslandScheduleSnapshot snapshot, Guid? subjectId)
    {
        if (subjectId.HasValue &&
            snapshot.Subjects.TryGetValue(subjectId.Value, out var subject))
        {
            if (!string.IsNullOrWhiteSpace(subject.TeacherName))
            {
                return subject.TeacherName.Trim();
            }

            if (!string.IsNullOrWhiteSpace(subject.Initial))
            {
                return subject.Initial.Trim();
            }
        }

        return L("schedule.widget.detail_fallback", "未设置详情");
    }

    private void UpdateHeader(DateTime now)
    {
        var month = now.Month.ToString(CultureInfo.InvariantCulture);
        var day = now.Day.ToString(CultureInfo.InvariantCulture);
        MonthTextBlock.Text = month;
        DayTextBlock.Text = day;
        WeekdayTextBlock.Text = FormatWeekday(now.DayOfWeek);
        ClassCountTextBlock.Text = FormatClassCount(_courseItems.Count);
    }

    private string FormatClassCount(int count)
    {
        if (string.Equals(_languageCode, "zh-CN", StringComparison.OrdinalIgnoreCase))
        {
            return string.Create(CultureInfo.InvariantCulture, $"{Math.Max(0, count)}节课");
        }

        if (count == 1)
        {
            return "1 class";
        }

        return string.Create(CultureInfo.InvariantCulture, $"{Math.Max(0, count)} classes");
    }

    private string FormatWeekday(DayOfWeek dayOfWeek)
    {
        if (string.Equals(_languageCode, "zh-CN", StringComparison.OrdinalIgnoreCase))
        {
            return dayOfWeek switch
            {
                DayOfWeek.Monday => "周一",
                DayOfWeek.Tuesday => "周二",
                DayOfWeek.Wednesday => "周三",
                DayOfWeek.Thursday => "周四",
                DayOfWeek.Friday => "周五",
                DayOfWeek.Saturday => "周六",
                _ => "周日"
            };
        }

        return dayOfWeek.ToString()[..3];
    }

    private static string? ResolveImportedSchedulePath(ComponentSettingsSnapshot snapshot)
    {
        if (snapshot.ImportedClassSchedules.Count == 0)
        {
            return null;
        }

        var activeId = snapshot.ActiveImportedClassScheduleId?.Trim() ?? string.Empty;
        ImportedClassScheduleSnapshot? selected = null;
        if (!string.IsNullOrWhiteSpace(activeId))
        {
            selected = snapshot.ImportedClassSchedules
                .FirstOrDefault(item => string.Equals(item.Id, activeId, StringComparison.OrdinalIgnoreCase));
        }

        selected ??= snapshot.ImportedClassSchedules[0];
        return selected.FilePath;
    }

    private string L(string key, string fallback)
    {
        return _localizationService.GetString(_languageCode, key, fallback);
    }

    private void ShowStatus(string text)
    {
        StatusTextBlock.Text = text;
        StatusTextBlock.IsVisible = true;
    }

    private void HideStatus()
    {
        StatusTextBlock.Text = string.Empty;
        StatusTextBlock.IsVisible = false;
    }

    private void RenderScheduleItems()
    {
        CourseListPanel.Children.Clear();
        ClassCountTextBlock.Text = FormatClassCount(_courseItems.Count);
        if (_courseItems.Count == 0)
        {
            return;
        }

        var useMonetColor = ComponentColorSchemeHelper.ShouldUseMonetColor(
            _componentColorScheme,
            ComponentColorSchemeHelper.GetCurrentGlobalThemeColorMode());
        var chromeScale = ComponentChromeCornerRadiusHelper.ResolveScale();
        _layoutScale = Math.Clamp(ResolveScale() * (0.9d + (chromeScale * 0.1d)), 0.52d, 2.2d);

        var bulletSize = Math.Clamp(10 * _layoutScale, 5, 12);
        var lineSpacing = Math.Clamp(4 * _layoutScale, 1.5, 8);
        var itemPadding = new Thickness(
            ComponentChromeCornerRadiusHelper.SafeValue(6 * _layoutScale, 3, 10),
            ComponentChromeCornerRadiusHelper.SafeValue(4 * _layoutScale, 2, 8),
            ComponentChromeCornerRadiusHelper.SafeValue(4 * _layoutScale, 2, 8),
            ComponentChromeCornerRadiusHelper.SafeValue(4 * _layoutScale, 2, 8));
        var maxVisibleItems = ResolveMaxVisibleItems(_layoutScale);
        var itemContentWidth = Math.Max(28, (Bounds.Width > 1 ? Bounds.Width : _currentCellSize * 4) - itemPadding.Left - itemPadding.Right - bulletSize - Math.Clamp(10 * _layoutScale, 4, 14));
        var titleHeight = Math.Clamp(34 * _layoutScale, 16, 42);
        var secondaryHeight = Math.Clamp(24 * _layoutScale, 12, 30);

        var primaryBrush = CreateBrush(_isNightVisual ? "#F9FBFF" : "#151821");
        var secondaryBrush = CreateBrush(_isNightVisual ? "#848B99" : "#667084");
        var currentBrush = useMonetColor
            ? CreateBrush("#FF4FC3F7")
            : CreateBrush("#FF4D5A");
        var normalBulletBrush = CreateBrush(_isNightVisual ? "#B8BEC9" : "#9AA3B2");

        for (var i = 0; i < _courseItems.Count; i++)
        {
            var item = _courseItems[i];
            var bulletBrush = item.IsCurrent ? currentBrush : normalBulletBrush;

            var bullet = new Border
            {
                Width = bulletSize,
                Height = bulletSize,
                CornerRadius = new CornerRadius(bulletSize * 0.5),
                Background = bulletBrush,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
                Margin = new Thickness(0, Math.Clamp(8 * _layoutScale, 2, 12), 0, 0)
            };

            var titleText = new TextBlock
            {
                Text = item.Name,
                FontSize = ComponentTypographyLayoutService.FitAdaptiveTextLayout(
                    item.Name,
                    itemContentWidth,
                    titleHeight,
                    minLines: 1,
                    maxLines: 2,
                    minFontSize: 14,
                    maxFontSize: Math.Clamp(42 * _layoutScale, 14, 42),
                    weightCandidates: new[] { FontWeight.SemiBold, FontWeight.Medium },
                    lineHeightFactor: 1.05d).FontSize,
                FontWeight = ComponentTypographyLayoutService.FitAdaptiveTextLayout(
                    item.Name,
                    itemContentWidth,
                    titleHeight,
                    minLines: 1,
                    maxLines: 2,
                    minFontSize: 14,
                    maxFontSize: Math.Clamp(42 * _layoutScale, 14, 42),
                    weightCandidates: new[] { FontWeight.SemiBold, FontWeight.Medium },
                    lineHeightFactor: 1.05d).Weight,
                Foreground = primaryBrush,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap
            };

            var timeText = new TextBlock
            {
                Text = item.TimeRange,
                FontSize = ComponentTypographyLayoutService.FitAdaptiveTextLayout(
                    item.TimeRange,
                    itemContentWidth,
                    secondaryHeight,
                    minLines: 1,
                    maxLines: 1,
                    minFontSize: 10,
                    maxFontSize: Math.Clamp(28 * _layoutScale, 10, 28),
                    weightCandidates: new[] { FontWeight.Medium, FontWeight.Normal },
                    lineHeightFactor: 1d).FontSize,
                FontWeight = ComponentTypographyLayoutService.FitAdaptiveTextLayout(
                    item.TimeRange,
                    itemContentWidth,
                    secondaryHeight,
                    minLines: 1,
                    maxLines: 1,
                    minFontSize: 10,
                    maxFontSize: Math.Clamp(28 * _layoutScale, 10, 28),
                    weightCandidates: new[] { FontWeight.Medium, FontWeight.Normal },
                    lineHeightFactor: 1d).Weight,
                Foreground = secondaryBrush,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap
            };

            var detailText = new TextBlock
            {
                Text = item.Detail,
                FontSize = ComponentTypographyLayoutService.FitAdaptiveTextLayout(
                    item.Detail,
                    itemContentWidth,
                    secondaryHeight,
                    minLines: 1,
                    maxLines: 1,
                    minFontSize: 10,
                    maxFontSize: Math.Clamp(28 * _layoutScale, 10, 28),
                    weightCandidates: new[] { FontWeight.Normal, FontWeight.Medium },
                    lineHeightFactor: 1d).FontSize,
                FontWeight = ComponentTypographyLayoutService.FitAdaptiveTextLayout(
                    item.Detail,
                    itemContentWidth,
                    secondaryHeight,
                    minLines: 1,
                    maxLines: 1,
                    minFontSize: 10,
                    maxFontSize: Math.Clamp(28 * _layoutScale, 10, 28),
                    weightCandidates: new[] { FontWeight.Normal, FontWeight.Medium },
                    lineHeightFactor: 1d).Weight,
                Foreground = secondaryBrush,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap
            };

            var textStack = new StackPanel
            {
                Spacing = lineSpacing,
                Children = { titleText, timeText, detailText }
            };

            var itemGrid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*"),
                ColumnSpacing = Math.Clamp(10 * _layoutScale, 4, 14)
            };
            itemGrid.Children.Add(bullet);
            itemGrid.Children.Add(textStack);
            Grid.SetColumn(textStack, 1);

            var itemBorder = new Border
            {
                Padding = itemPadding,
                Background = Brushes.Transparent,
                Child = itemGrid
            };

            CourseListPanel.Children.Add(itemBorder);
        }
    }

    private int ResolveMaxVisibleItems(double scale)
    {
        var height = Bounds.Height > 1 ? Bounds.Height : _currentCellSize * 4;
        var rootVerticalPadding = RootBorder.Padding.Top + RootBorder.Padding.Bottom;
        var headerEstimatedHeight = Math.Clamp(100 * scale, 54, 140);
        var itemEstimatedHeight = Math.Clamp(136 * scale, 72, 178);
        var available = Math.Max(1, height - rootVerticalPadding - headerEstimatedHeight);
        var count = (int)Math.Floor(available / Math.Max(1, itemEstimatedHeight));
        return Math.Clamp(count, 1, 6);
    }

    private void ApplyAdaptiveLayout()
    {
        if (Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }

        var scale = ResolveScale();
        var chromeScale = ComponentChromeCornerRadiusHelper.ResolveScale();
        _layoutScale = Math.Clamp(scale * (0.9d + (chromeScale * 0.1d)), 0.52d, 2.2d);
        _isNightVisual = ResolveNightMode();

        var useMonetColor = ComponentColorSchemeHelper.ShouldUseMonetColor(
            _componentColorScheme,
            ComponentColorSchemeHelper.GetCurrentGlobalThemeColorMode());

        var slashBrush = useMonetColor
            ? CreateBrush("#FF4FC3F7")
            : CreateBrush("#FF3250");
        var sampleNow = _timeZoneService?.GetCurrentTime() ?? DateTime.Now;
        var headerWidth = Math.Max(42, Bounds.Width > 1 ? Bounds.Width : _currentCellSize * 4);
        var headerHeight = Math.Max(42, Bounds.Height > 1 ? Bounds.Height : _currentCellSize * 4);
        var headerPrimaryWidth = Math.Clamp(headerWidth * 0.34, 28, 92);
        var headerSecondaryWidth = Math.Clamp(headerWidth * 0.52, 40, 148);
        var dateHeight = Math.Clamp(headerHeight * 0.30, 26, 96);
        var secondaryHeaderHeight = Math.Clamp(headerHeight * 0.12, 16, 42);
        var weekdayLayout = ComponentTypographyLayoutService.FitAdaptiveTextLayout(
            FormatWeekday(sampleNow.DayOfWeek),
            headerSecondaryWidth,
            secondaryHeaderHeight,
            minLines: 1,
            maxLines: 1,
            minFontSize: 13,
            maxFontSize: 32,
            weightCandidates: new[] { FontWeight.SemiBold, FontWeight.Medium },
            lineHeightFactor: 1.02d);
        var classCountLayout = ComponentTypographyLayoutService.FitAdaptiveTextLayout(
            FormatClassCount(_courseItems.Count),
            headerSecondaryWidth,
            secondaryHeaderHeight,
            minLines: 1,
            maxLines: 1,
            minFontSize: 14,
            maxFontSize: 36,
            weightCandidates: new[] { FontWeight.SemiBold, FontWeight.Medium },
            lineHeightFactor: 1.02d);
        var statusText = string.IsNullOrWhiteSpace(StatusTextBlock.Text)
            ? L("schedule.widget.no_class_today", "浠婂ぉ娌℃湁璇剧▼")
            : StatusTextBlock.Text;
        var statusLayout = ComponentTypographyLayoutService.FitAdaptiveTextLayout(
            statusText,
            headerSecondaryWidth,
            secondaryHeaderHeight,
            minLines: 1,
            maxLines: 1,
            minFontSize: 12,
            maxFontSize: 30,
            weightCandidates: new[] { FontWeight.Medium, FontWeight.Normal },
            lineHeightFactor: 1.02d);
        var monthLayout = ComponentTypographyLayoutService.FitAdaptiveTextLayout(
            sampleNow.Month.ToString(CultureInfo.InvariantCulture),
            headerPrimaryWidth,
            dateHeight,
            minLines: 1,
            maxLines: 1,
            minFontSize: 26,
            maxFontSize: 82,
            weightCandidates: new[] { FontWeight.SemiBold, FontWeight.Medium },
            lineHeightFactor: 1d);
        var dayLayout = ComponentTypographyLayoutService.FitAdaptiveTextLayout(
            sampleNow.Day.ToString(CultureInfo.InvariantCulture),
            headerPrimaryWidth,
            dateHeight,
            minLines: 1,
            maxLines: 1,
            minFontSize: 26,
            maxFontSize: 82,
            weightCandidates: new[] { FontWeight.SemiBold, FontWeight.Medium },
            lineHeightFactor: 1d);

        RootBorder.CornerRadius = ComponentChromeCornerRadiusHelper.Scale(_currentCellSize * 0.45, 24, 44);
        RootBorder.Background = _isNightVisual
            ? CreateGradientBrush("#171A21", "#0C0E14")
            : CreateGradientBrush("#F7F8FC", "#ECEFF6");
        RootBorder.BorderBrush = CreateBrush(_isNightVisual ? "#24FFFFFF" : "#15000000");

        var rootPadding = new Thickness(
            ComponentChromeCornerRadiusHelper.SafeValue(16 * _layoutScale, 10, 24),
            ComponentChromeCornerRadiusHelper.SafeValue(14 * _layoutScale, 9, 20),
            ComponentChromeCornerRadiusHelper.SafeValue(16 * _layoutScale, 10, 24),
            ComponentChromeCornerRadiusHelper.SafeValue(14 * _layoutScale, 8, 20));
        RootBorder.Padding = rootPadding;

        LayoutGrid.RowSpacing = Math.Clamp(14 * _layoutScale, 6, 20);
        HeaderGrid.ColumnSpacing = Math.Clamp(10 * _layoutScale, 4, 16);
        DateGroup.Spacing = Math.Clamp(1.5 * _layoutScale, 0.5, 3);
        MetaStack.Spacing = Math.Clamp(6 * _layoutScale, 3, 10);
        CourseListPanel.Spacing = Math.Clamp(6 * _layoutScale, 3, 10);

        var dateFont = Math.Clamp(66 * _layoutScale, 26, 82);
        MonthTextBlock.FontSize = monthLayout.FontSize;
        DayTextBlock.FontSize = dayLayout.FontSize;
        SlashTextBlock.FontSize = dateFont;

        MonthTextBlock.Foreground = CreateBrush(_isNightVisual ? "#F8FAFF" : "#131722");
        DayTextBlock.Foreground = CreateBrush(_isNightVisual ? "#F8FAFF" : "#131722");
        SlashTextBlock.Foreground = slashBrush;
        WeekdayTextBlock.Foreground = CreateBrush(_isNightVisual ? "#C6CBD5" : "#4B5463");
        ClassCountTextBlock.Foreground = CreateBrush(_isNightVisual ? "#8D95A4" : "#738095");
        StatusTextBlock.Foreground = CreateBrush(_isNightVisual ? "#9AA2B1" : "#4B5565");

        WeekdayTextBlock.FontSize = weekdayLayout.FontSize;
        ClassCountTextBlock.FontSize = classCountLayout.FontSize;
        StatusTextBlock.FontSize = statusLayout.FontSize;

        WeekdayTextBlock.FontWeight = weekdayLayout.Weight;
        ClassCountTextBlock.FontWeight = classCountLayout.Weight;
        StatusTextBlock.FontWeight = statusLayout.Weight;
    }

    private static string FormatTime(TimeSpan time)
    {
        return string.Create(CultureInfo.InvariantCulture, $"{time.Hours}:{time.Minutes:00}");
    }

    private double ResolveScale()
    {
        var cellScale = Math.Clamp(_currentCellSize / 44d, 0.58, 2.2);
        var widthScale = Bounds.Width > 1 ? Math.Clamp(Bounds.Width / 230d, 0.52, 2.4) : 1;
        var heightScale = Bounds.Height > 1 ? Math.Clamp(Bounds.Height / 440d, 0.52, 2.4) : 1;
        return Math.Clamp(Math.Min(cellScale, Math.Min(widthScale, heightScale) * 1.04), 0.52, 2.2);
    }

    private bool ResolveNightMode()
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
            value is ISolidColorBrush brush)
        {
            return CalculateRelativeLuminance(brush.Color) < 0.45;
        }

        return true;
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

    private static FontWeight ToVariableWeight(double value)
    {
        return (FontWeight)(int)Math.Clamp(Math.Round(value), 1, 1000);
    }

    private static double Lerp(double from, double to, double t)
    {
        return from + ((to - from) * t);
    }

    private static IBrush CreateBrush(string colorHex)
    {
        return new SolidColorBrush(Color.Parse(colorHex));
    }

    private static IBrush CreateGradientBrush(string fromHex, string toHex)
    {
        return new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops = new GradientStops
            {
                new GradientStop(Color.Parse(fromHex), 0),
                new GradientStop(Color.Parse(toHex), 1)
            }
        };
    }
}
