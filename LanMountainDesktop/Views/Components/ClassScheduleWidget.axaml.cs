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
    private IReadOnlyList<CourseItemViewModel> _courseItems = Array.Empty<CourseItemViewModel>();
    private IReadOnlyList<CourseItemViewModel> _lastRenderedItems = Array.Empty<CourseItemViewModel>();
    private bool _isNightVisual = true;
    private string _languageCode = "zh-CN";
    private string _componentId = BuiltInComponentIds.DesktopClassSchedule;
    private string _placementId = string.Empty;
    private string? _componentColorScheme;

    private ClassIslandScheduleReadResult? _cachedScheduleResult;
    private string? _lastLoadedSchedulePath;
    private DateTime _lastScheduleLoadTime = DateTime.MinValue;
    private static readonly TimeSpan ScheduleCacheDuration = TimeSpan.FromMinutes(5);

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
        InvalidateScheduleCache();
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

        if (ShouldRefreshOnTimerTick(now, currentDate))
        {
            RefreshSchedule();
        }
        else
        {
            UpdateCurrentCourseState(now);
        }

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

    private bool ShouldRefreshOnTimerTick(DateTime now, DateOnly currentDate)
    {
        if (_lastRefreshDate != currentDate)
        {
            return true;
        }

        if (_courseItems.Count == 0)
        {
            return true;
        }

        foreach (var item in _courseItems)
        {
            if (item.IsCurrent)
            {
                var currentTime = now.TimeOfDay;
                if (currentTime.TotalSeconds < 30 || currentTime.TotalSeconds > 86970)
                {
                    return true;
                }
                break;
            }
        }

        return false;
    }

    private void UpdateCurrentCourseState(DateTime now)
    {
        bool needsRender = false;
        for (var i = 0; i < _courseItems.Count; i++)
        {
            var item = _courseItems[i];
            var timeParts = item.TimeRange.Split('-');
            if (timeParts.Length != 2) continue;

            if (TimeSpan.TryParse(timeParts[0].Trim(), out var startTime) &&
                TimeSpan.TryParse(timeParts[1].Trim(), out var endTime))
            {
                var shouldBeCurrent = now.TimeOfDay >= startTime && now.TimeOfDay <= endTime;
                if (shouldBeCurrent != item.IsCurrent)
                {
                    needsRender = true;
                    break;
                }
            }
        }

        if (needsRender)
        {
            RefreshSchedule();
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

            var targetOffset = bounds.Position.Y - (scrollViewerHeight / 2) + (bounds.Height / 2);

            targetOffset = Math.Max(0, Math.Min(targetOffset, contentHeight - scrollViewerHeight));

            ContentScrollViewer.Offset = new Vector(0, targetOffset);
        }, Avalonia.Threading.DispatcherPriority.Loaded);
    }

    public void RefreshFromSettings()
    {
        InvalidateScheduleCache();
        RefreshSchedule();
    }

    public void SetComponentPlacementContext(string componentId, string? placementId)
    {
        _componentId = string.IsNullOrWhiteSpace(componentId)
            ? BuiltInComponentIds.DesktopClassSchedule
            : componentId.Trim();
        _placementId = placementId?.Trim() ?? string.Empty;
        InvalidateScheduleCache();
        RefreshSchedule();
    }

    private void InvalidateScheduleCache()
    {
        _cachedScheduleResult = null;
        _lastLoadedSchedulePath = null;
        _lastScheduleLoadTime = DateTime.MinValue;
    }

    private ClassIslandScheduleReadResult LoadScheduleWithCache(
        string? path,
        DateOnly? semesterStartDate,
        int semesterWeekCycle)
    {
        if (!string.IsNullOrEmpty(path) &&
            _cachedScheduleResult != null &&
            _lastLoadedSchedulePath == path &&
            (DateTime.Now - _lastScheduleLoadTime) < ScheduleCacheDuration)
        {
            return _cachedScheduleResult;
        }

        var result = _scheduleService.Load(
            path,
            profileFileName: null,
            semesterStartDate: semesterStartDate,
            semesterWeekCycle: semesterWeekCycle);

        if (result.Success)
        {
            _cachedScheduleResult = result;
            _lastLoadedSchedulePath = path;
            _lastScheduleLoadTime = DateTime.Now;
        }

        return result;
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
        var readResult = LoadScheduleWithCache(
            importedSchedulePath,
            componentSettings.SemesterStartDate,
            componentSettings.SemesterWeekCycle);

        if (!readResult.Success || readResult.Snapshot is null)
        {
            var newItems = Array.Empty<CourseItemViewModel>();
            if (!IsDataEqual(_courseItems, newItems))
            {
                _courseItems = newItems;
                UpdateHeader(now);
                ShowStatus(L("schedule.widget.no_source", "未读取到 ClassIsland 课表"));
                RenderScheduleItems();
            }
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
                var newItems = Array.Empty<CourseItemViewModel>();
                if (!IsDataEqual(_courseItems, newItems))
                {
                    _courseItems = newItems;
                    UpdateHeader(now);
                    ShowStatus(L("schedule.widget.no_class_today", "今天没有课程"));
                    RenderScheduleItems();
                }
                return;
            }
        }

        if (!snapshot.TimeLayouts.TryGetValue(resolvedClassPlan.ClassPlan.TimeLayoutId, out var layout))
        {
            var newItems = Array.Empty<CourseItemViewModel>();
            if (!IsDataEqual(_courseItems, newItems))
            {
                _courseItems = newItems;
                UpdateHeader(now);
                ShowStatus(L("schedule.widget.layout_missing", "课表时间布局缺失"));
                RenderScheduleItems();
            }
            return;
        }

        var adjustedNow = today == DateOnly.FromDateTime(now) ? now : DateTime.Today.AddHours(8);
        var newCourseItems = BuildCourseItemViewModels(snapshot, resolvedClassPlan.ClassPlan, layout, adjustedNow);

        if (newCourseItems.Count == 0)
        {
            var nextDay = today.AddDays(1);
            if (_scheduleService.TryResolveClassPlanForDate(snapshot, nextDay, out var nextDayClassPlan) &&
                snapshot.TimeLayouts.TryGetValue(nextDayClassPlan.ClassPlan.TimeLayoutId, out var nextLayout))
            {
                today = nextDay;
                adjustedNow = DateTime.Today.AddHours(8);
                newCourseItems = BuildCourseItemViewModels(snapshot, nextDayClassPlan.ClassPlan, nextLayout, adjustedNow);
            }
        }

        UpdateHeader(today.ToDateTime(TimeOnly.MinValue));

        if (newCourseItems.Count == 0)
        {
            if (!IsDataEqual(_courseItems, newCourseItems))
            {
                _courseItems = newCourseItems;
                ShowStatus(L("schedule.widget.no_class_today", "今天没有课程"));
                RenderScheduleItems();
            }
        }
        else
        {
            var dataChanged = !IsDataEqual(_courseItems, newCourseItems);
            if (dataChanged)
            {
                _courseItems = newCourseItems;
                var currentIndex = FindCurrentCourseIndex();
                _lastCurrentCourseIndex = currentIndex;
                HideStatus();

                if (currentIndex >= 0)
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        ScrollToCurrentCourse(currentIndex);
                    }, Avalonia.Threading.DispatcherPriority.Loaded);
                }

                RenderScheduleItems();
            }
            else
            {
                var currentIndex = FindCurrentCourseIndex();
                if (currentIndex != _lastCurrentCourseIndex)
                {
                    _lastCurrentCourseIndex = currentIndex;
                    IncrementalUpdateCurrentCourseHighlight(currentIndex);
                }
            }
        }
    }

    private static bool IsDataEqual(IReadOnlyList<CourseItemViewModel> oldItems, IReadOnlyList<CourseItemViewModel> newItems)
    {
        if (oldItems.Count != newItems.Count)
        {
            return false;
        }

        for (var i = 0; i < oldItems.Count; i++)
        {
            var oldItem = oldItems[i];
            var newItem = newItems[i];

            if (oldItem.Name != newItem.Name ||
                oldItem.TimeRange != newItem.TimeRange ||
                oldItem.Detail != newItem.Detail ||
                oldItem.IsCurrent != newItem.IsCurrent)
            {
                return false;
            }
        }

        return true;
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
        ClassCountTextBlock.Text = FormatClassCount(_courseItems.Count);

        if (_courseItems.Count == 0)
        {
            if (CourseListPanel.Children.Count > 0)
            {
                CourseListPanel.Children.Clear();
            }
            return;
        }

        var needsFullRebuild = CourseListPanel.Children.Count != _courseItems.Count;

        if (needsFullRebuild)
        {
            RebuildAllItems();
        }
        else
        {
            IncrementalUpdateItems();
        }

        _lastRenderedItems = _courseItems.ToList();
    }

    private void RebuildAllItems()
    {
        CourseListPanel.Children.Clear();

        var useMonetColor = ComponentColorSchemeHelper.ShouldUseMonetColor(
            _componentColorScheme,
            ComponentColorSchemeHelper.GetCurrentGlobalThemeColorMode());

        var scale = ResolveScale();
        var bulletSize = Math.Clamp(10 * scale, 5, 12);
        var courseNameSize = Math.Clamp(42 * scale, 14, 42);
        var secondarySize = Math.Clamp(29 * scale, 10, 28);
        var lineSpacing = Math.Clamp(4 * scale, 1.5, 8);
        var itemPadding = new Thickness(
            Math.Clamp(6 * scale, 3, 10),
            Math.Clamp(4 * scale, 2, 8),
            Math.Clamp(4 * scale, 2, 8),
            Math.Clamp(4 * scale, 2, 8));

        var primaryBrush = CreateBrush(_isNightVisual ? "#F9FBFF" : "#151821");
        var secondaryBrush = CreateBrush(_isNightVisual ? "#848B99" : "#667084");
        var currentBrush = useMonetColor
            ? CreateBrush("#FF4FC3F7")
            : CreateBrush("#FF4D5A");
        var normalBulletBrush = CreateBrush(_isNightVisual ? "#B8BEC9" : "#9AA3B2");

        for (var i = 0; i < _courseItems.Count; i++)
        {
            var item = _courseItems[i];
            var itemControls = CreateSingleItemControl(
                item,
                scale,
                bulletSize,
                courseNameSize,
                secondarySize,
                lineSpacing,
                itemPadding,
                primaryBrush,
                secondaryBrush,
                item.IsCurrent ? currentBrush : normalBulletBrush);

            CourseListPanel.Children.Add(itemControls);
        }
    }

    private void IncrementalUpdateItems()
    {
        var useMonetColor = ComponentColorSchemeHelper.ShouldUseMonetColor(
            _componentColorScheme,
            ComponentColorSchemeHelper.GetCurrentGlobalThemeColorMode());

        var currentBrush = useMonetColor
            ? CreateBrush("#FF4FC3F7")
            : CreateBrush("#FF4D5A");
        var normalBulletBrush = CreateBrush(_isNightVisual ? "#B8BEC9" : "#9AA3B2");
        var primaryBrush = CreateBrush(_isNightVisual ? "#F9FBFF" : "#151821");
        var secondaryBrush = CreateBrush(_isNightVisual ? "#848B99" : "#667084");

        for (var i = 0; i < _courseItems.Count && i < CourseListPanel.Children.Count; i++)
        {
            var item = _courseItems[i];
            var existingBorder = CourseListPanel.Children[i] as Border;
            if (existingBorder == null) continue;

            var existingGrid = existingBorder.Child as Grid;
            if (existingGrid == null || existingGrid.Children.Count < 2) continue;

            var bulletBorder = existingGrid.Children[0] as Border;
            var textStack = existingGrid.Children[1] as StackPanel;
            if (bulletBorder == null || textStack == null || textStack.Children.Count < 3) continue;

            var newBulletBrush = item.IsCurrent ? currentBrush : normalBulletBrush;
            bulletBorder.Background = newBulletBrush;

            var titleText = textStack.Children[0] as TextBlock;
            var timeText = textStack.Children[1] as TextBlock;
            var detailText = textStack.Children[2] as TextBlock;

            if (titleText != null)
            {
                if (titleText.Text != item.Name)
                {
                    titleText.Text = item.Name;
                }
                titleText.Foreground = primaryBrush;
            }

            if (timeText != null)
            {
                if (timeText.Text != item.TimeRange)
                {
                    timeText.Text = item.TimeRange;
                }
                timeText.Foreground = secondaryBrush;
            }

            if (detailText != null)
            {
                if (detailText.Text != item.Detail)
                {
                    detailText.Text = item.Detail;
                }
                detailText.Foreground = secondaryBrush;
            }
        }
    }

    private void IncrementalUpdateCurrentCourseHighlight(int currentCourseIndex)
    {
        var useMonetColor = ComponentColorSchemeHelper.ShouldUseMonetColor(
            _componentColorScheme,
            ComponentColorSchemeHelper.GetCurrentGlobalThemeColorMode());

        var currentBrush = useMonetColor
            ? CreateBrush("#FF4FC3F7")
            : CreateBrush("#FF4D5A");
        var normalBulletBrush = CreateBrush(_isNightVisual ? "#B8BEC9" : "#9AA3B2");

        for (var i = 0; i < CourseListPanel.Children.Count; i++)
        {
            var border = CourseListPanel.Children[i] as Border;
            if (border == null) continue;

            var grid = border.Child as Grid;
            if (grid == null || grid.Children.Count < 2) continue;

            var bulletBorder = grid.Children[0] as Border;
            if (bulletBorder == null) continue;

            bulletBorder.Background = i == currentCourseIndex ? currentBrush : normalBulletBrush;
        }
    }

    private Border CreateSingleItemControl(
        CourseItemViewModel item,
        double scale,
        double bulletSize,
        double courseNameSize,
        double secondarySize,
        double lineSpacing,
        Thickness itemPadding,
        IBrush primaryBrush,
        IBrush secondaryBrush,
        IBrush bulletBrush)
    {
        var bullet = new Border
        {
            Width = bulletSize,
            Height = bulletSize,
            CornerRadius = new CornerRadius(bulletSize * 0.5),
            Background = bulletBrush,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
            Margin = new Thickness(0, Math.Clamp(8 * scale, 2, 12), 0, 0)
        };

        var titleText = new TextBlock
        {
            Text = item.Name,
            FontSize = courseNameSize,
            FontWeight = ToVariableWeight(Lerp(620, 780, Math.Clamp((scale - 0.60) / 1.2, 0, 1))),
            Foreground = primaryBrush,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap
        };

        var timeText = new TextBlock
        {
            Text = item.TimeRange,
            FontSize = secondarySize,
            FontWeight = ToVariableWeight(Lerp(520, 680, Math.Clamp((scale - 0.60) / 1.2, 0, 1))),
            Foreground = secondaryBrush,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap
        };

        var detailText = new TextBlock
        {
            Text = item.Detail,
            FontSize = secondarySize,
            FontWeight = ToVariableWeight(Lerp(500, 640, Math.Clamp((scale - 0.60) / 1.2, 0, 1))),
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
            ColumnSpacing = Math.Clamp(10 * scale, 4, 14)
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

        return itemBorder;
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
        _isNightVisual = ResolveNightMode();

        var useMonetColor = ComponentColorSchemeHelper.ShouldUseMonetColor(
            _componentColorScheme,
            ComponentColorSchemeHelper.GetCurrentGlobalThemeColorMode());

        var slashBrush = useMonetColor
            ? CreateBrush("#FF4FC3F7")
            : CreateBrush("#FF3250");

        RootBorder.CornerRadius = ComponentChromeCornerRadiusHelper.ResolveMainRectangleRadius();
        RootBorder.Background = _isNightVisual
            ? CreateGradientBrush("#171A21", "#0C0E14")
            : CreateGradientBrush("#F7F8FC", "#ECEFF6");
        RootBorder.BorderBrush = CreateBrush(_isNightVisual ? "#24FFFFFF" : "#15000000");

        var rootPadding = new Thickness(
            ComponentChromeCornerRadiusHelper.SafeValue(16 * scale, 10, 24),
            ComponentChromeCornerRadiusHelper.SafeValue(14 * scale, 9, 20),
            ComponentChromeCornerRadiusHelper.SafeValue(16 * scale, 10, 24),
            ComponentChromeCornerRadiusHelper.SafeValue(14 * scale, 8, 20));
        RootBorder.Padding = rootPadding;

        LayoutGrid.RowSpacing = Math.Clamp(14 * scale, 6, 20);
        HeaderGrid.ColumnSpacing = Math.Clamp(10 * scale, 4, 16);
        DateGroup.Spacing = Math.Clamp(1.5 * scale, 0.5, 3);
        MetaStack.Spacing = Math.Clamp(6 * scale, 3, 10);
        CourseListPanel.Spacing = Math.Clamp(6 * scale, 3, 10);

        var dateFontByScale = Math.Clamp(66 * scale, 26, 82);
        var weekdayFontByScale = Math.Clamp(34 * scale, 13, 32);
        var classCountFontByScale = Math.Clamp(40 * scale, 14, 36);

        // 宽度感知：当头部内容总需求超过可用宽度时，按比例缩小日期字体
        var availableWidth = Math.Max(1, Bounds.Width - rootPadding.Left - rootPadding.Right);
        var dateGroupEstimatedWidth = dateFontByScale * 0.6 * 3 + DateGroup.Spacing * 2;
        var metaStackEstimatedWidth = classCountFontByScale * 0.6 * 4 + MetaStack.Spacing;
        var headerColumnSpacing = Math.Clamp(10 * scale, 4, 16);
        var totalHeaderNeed = dateGroupEstimatedWidth + headerColumnSpacing + metaStackEstimatedWidth;

        var dateFont = dateFontByScale;
        if (totalHeaderNeed > availableWidth)
        {
            var shrinkRatio = availableWidth / totalHeaderNeed;
            dateFont = Math.Max(20, dateFontByScale * shrinkRatio);
        }

        // 为 HeaderGrid 左列设置最小宽度，防止被压缩至零
        var minDateColumnWidth = dateFont * 0.6 * 3 + DateGroup.Spacing * 2;
        HeaderGrid.ColumnDefinitions[0].MinWidth = minDateColumnWidth;

        MonthTextBlock.FontSize = dateFont;
        DayTextBlock.FontSize = dateFont;
        SlashTextBlock.FontSize = dateFont;

        MonthTextBlock.Foreground = CreateBrush(_isNightVisual ? "#F8FAFF" : "#131722");
        DayTextBlock.Foreground = CreateBrush(_isNightVisual ? "#F8FAFF" : "#131722");
        SlashTextBlock.Foreground = slashBrush;
        WeekdayTextBlock.Foreground = CreateBrush(_isNightVisual ? "#C6CBD5" : "#4B5463");
        ClassCountTextBlock.Foreground = CreateBrush(_isNightVisual ? "#8D95A4" : "#738095");
        StatusTextBlock.Foreground = CreateBrush(_isNightVisual ? "#9AA2B1" : "#4B5565");

        WeekdayTextBlock.FontSize = weekdayFontByScale;
        ClassCountTextBlock.FontSize = classCountFontByScale;
        StatusTextBlock.FontSize = Math.Clamp(30 * scale, 12, 30);

        WeekdayTextBlock.FontWeight = ToVariableWeight(Lerp(560, 700, Math.Clamp((scale - 0.60) / 1.2, 0, 1)));
        ClassCountTextBlock.FontWeight = ToVariableWeight(Lerp(560, 680, Math.Clamp((scale - 0.60) / 1.2, 0, 1)));
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
        return 0.2126 * r + 0.7155 * g + 0.0722 * b;
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
