using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using LanMountainDesktop.Models;
using LanMountainDesktop.Services;
using LanMountainDesktop.Theme;

namespace LanMountainDesktop.Views.Components;

public partial class StudyInterruptDensityWidget : UserControl, IDesktopComponentWidget, IDesktopPageVisibilityAwareComponentWidget
{
    private static readonly Color[] PrimaryColorCandidates =
    {
        Color.Parse("#FFEAF5FF"),
        Color.Parse("#FFDDEEFF"),
        Color.Parse("#FFCEE3FA"),
        Color.Parse("#FF1B2E45"),
        Color.Parse("#FF233A54"),
        Color.Parse("#FFFFFFFF"),
        Color.Parse("#FF101C2A")
    };

    private static readonly Color[] SecondaryColorCandidates =
    {
        Color.Parse("#FFC7D9EC"),
        Color.Parse("#FFBAD0E8"),
        Color.Parse("#FFD9E8F6"),
        Color.Parse("#FF2F4763"),
        Color.Parse("#FF385673"),
        Color.Parse("#FFEAF3FA"),
        Color.Parse("#FF1A2C40")
    };

    private readonly IStudyAnalyticsService _studyAnalyticsService = StudyAnalyticsServiceFactory.CreateDefault();
    private readonly StudyAnalyticsMonitoringLeaseCoordinator _monitoringLeaseCoordinator = StudyAnalyticsMonitoringLeaseCoordinatorFactory.CreateDefault();
    private LanMountainDesktop.AirAppSdk.ISettingsService _settingsService = LanMountainDesktop.Services.Settings.HostSettingsFacadeProvider.GetOrCreate().Settings;
    private readonly LocalizationService _localizationService = new();
    private readonly StudySnapshotRenderGate _renderGate;

    private double _currentCellSize = ComponentDesignMetrics.BaseCellSize;
    private bool _isAttached;
    private bool _isOnActivePage = true;
    private bool _isSubscribed;
    private bool _isCompactMode;
    private bool _isUltraCompactMode;
    private bool _studyEnabled = true;
    private string _languageCode = LocalizationService.DefaultLanguageCode;
    private IDisposable? _monitoringLease;

    // 通知相关字段
    private DateTime _lastAlertTime = DateTime.MinValue;
    private readonly TimeSpan _alertCooldown = TimeSpan.FromMinutes(2); // 2分钟冷却时间
    private DensityLevelKind _lastLevelKind = DensityLevelKind.Calm;

    private enum DensityLevelKind
    {
        Calm = 0,
        Normal = 1,
        Frequent = 2,
        Severe = 3
    }

    private readonly record struct InterruptDensityMetrics(
        double DensityPerMin,
        int SegmentCount,
        TimeSpan Duration,
        double ThresholdPerMin,
        DensityLevelKind LevelKind);

    public StudyInterruptDensityWidget()
    {
        InitializeComponent();

        _renderGate = new StudySnapshotRenderGate(CanRenderSnapshot, ApplySnapshot);
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
        SizeChanged += OnSizeChanged;
        ActualThemeVariantChanged += OnActualThemeVariantChanged;
        ReloadLanguageCode();
        ApplyCellSize(_currentCellSize);
        ApplySnapshot(_studyAnalyticsService.GetSnapshot());
    }

    public void ApplyCellSize(double cellSize)
    {
        ComponentDesignMetrics.ApplyCellSize(
            ref _currentCellSize, cellSize, UpdateAdaptiveLayout);
    }

    public void SetDesktopPageContext(bool isOnActivePage, bool isEditMode) =>
        StudyComponentLifecycle.ApplyPageContext(
            ref _isOnActivePage,
            isOnActivePage,
            isEditMode,
            UpdateMonitoringLeaseState,
            RefreshVisual);

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        StudyComponentLifecycle.Attach(
            ref _isAttached, ref _isSubscribed, _studyAnalyticsService, _renderGate,
            ReloadLanguageCode, UpdateMonitoringLeaseState, RefreshVisual);
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        StudyComponentLifecycle.Detach(
            ref _isAttached, ref _monitoringLease, ref _isSubscribed, _studyAnalyticsService, _renderGate);
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        StudyComponentLifecycle.RefreshOnResize(RootBorder, UpdateAdaptiveLayout, ApplyTypographyByBackground);
    }

    private void OnActualThemeVariantChanged(object? sender, EventArgs e)
    {
        RefreshVisual();
    }

    private bool CanRenderSnapshot()
    {
        return _isAttached && _isOnActivePage;
    }

    private void UpdateMonitoringLeaseState() =>
        StudyMonitoringLease.Sync(ref _monitoringLease, _monitoringLeaseCoordinator, _studyEnabled, _isAttached, _isOnActivePage);

    private void RefreshVisual()
    {
        _renderGate.Queue(_studyAnalyticsService.GetSnapshot());
    }

    private void ApplySnapshot(StudyAnalyticsSnapshot snapshot)
    {
        var panelColor = StudyPanelPalette.Resolve(this, RootBorder.Background);
        ApplyTypographyByBackground(panelColor);
        ApplyLocalizedLabels();

        if (!_studyEnabled)
        {
            ModeTextBlock.Text = L("study.widget.disabled_hint", "请在设置中开启");
            ApplyModeBadgeColor(panelColor, StudyPanelPalette.DisabledBadge);
            DensityValueTextBlock.Text = "--";
            DensityUnitTextBlock.Text = "";
            return;
        }

        var isSessionRunning = snapshot.Session.State == StudySessionRuntimeState.Running;
        var isSessionReport = snapshot.DataMode == StudyDataMode.SessionReport && snapshot.LastSessionReport is not null;
        var isSessionView = isSessionRunning || isSessionReport;
        ModeTextBlock.Text = isSessionView
            ? L("study.interrupt_density.mode.session", "Session")
            : L("study.interrupt_density.mode.realtime", "Realtime");
        ApplyModeBadgeColor(panelColor, isSessionView ? StudyPanelPalette.SuccessBadge : StudyPanelPalette.RealtimeBadge);

        InterruptDensityMetrics? metrics;
        if (isSessionReport && snapshot.LastSessionReport is not null)
        {
            metrics = ComputeReportDensity(snapshot.LastSessionReport, snapshot.Config);
        }
        else
        {
            metrics = isSessionRunning
                ? ComputeSessionDensity(snapshot)
                : ComputeRealtimeDensity(snapshot);
        }

        if (metrics is null)
        {
            ApplyUnavailable(snapshot.Config.MaxSegmentsPerMin);
            return;
        }

        var m = metrics.Value;
        DensityValueTextBlock.Text = string.Format(
            CultureInfo.InvariantCulture,
            L("study.interrupt_density.density_value_format", "{0:F1}"),
            m.DensityPerMin);
        CountValueTextBlock.Text = string.Format(
            CultureInfo.InvariantCulture,
            L("study.interrupt_density.segment_count_value_format", "{0}"),
            m.SegmentCount);
        DurationValueTextBlock.Text = FormatDuration(m.Duration);
        DensityLevelTextBlock.Text = string.Format(
            CultureInfo.InvariantCulture,
            L("study.interrupt_density.level_format", "Level {0}"),
            ResolveLevelText(m.LevelKind));
        ThresholdTextBlock.Text = string.Format(
            CultureInfo.InvariantCulture,
            L("study.interrupt_density.threshold_format", "Threshold {0:F1}/min"),
            m.ThresholdPerMin);

        // 检查并发送通知
        CheckAndSendAlert(m, snapshot.Config);
    }

    private void ApplyLocalizedLabels()
    {
        TitleTextBlock.Text = L("study.interrupt_density.title", "Interrupt Density");
        DensityUnitTextBlock.Text = L("study.interrupt_density.unit", "/min");
        CountLabelTextBlock.Text = _isUltraCompactMode
            ? L("study.interrupt_density.segment_count_short", "Count")
            : L("study.interrupt_density.segment_count", "Interrupts");
        DurationLabelTextBlock.Text = _isUltraCompactMode
            ? L("study.interrupt_density.duration_short", "Time")
            : L("study.interrupt_density.duration", "Duration");
    }

    private void ApplyUnavailable(double thresholdPerMin)
    {
        var unavailable = L("study.interrupt_density.unavailable", "--");
        DensityValueTextBlock.Text = unavailable;
        CountValueTextBlock.Text = unavailable;
        DurationValueTextBlock.Text = unavailable;
        DensityLevelTextBlock.Text = string.Format(
            CultureInfo.InvariantCulture,
            L("study.interrupt_density.level_format", "Level {0}"),
            unavailable);
        ThresholdTextBlock.Text = string.Format(
            CultureInfo.InvariantCulture,
            L("study.interrupt_density.threshold_format", "Threshold {0:F1}/min"),
            Math.Max(1, thresholdPerMin));
    }

    private void UpdateAdaptiveLayout()
    {
        var cellScale = Math.Clamp(_currentCellSize / ComponentDesignMetrics.BaseCellSize, 0.76, 2.4);
        var widthScale = Bounds.Width > 1 ? Bounds.Width / 420d : cellScale;
        var heightScale = Bounds.Height > 1 ? Bounds.Height / 220d : cellScale;
        var boundsScale = Math.Clamp(Math.Min(widthScale, heightScale), 0.52, 2.2);
        var scale = Math.Clamp(Math.Min(cellScale, boundsScale * 1.08), 0.52, 2.2);

        _isCompactMode = scale < 0.92 || (Bounds.Width > 1 && Bounds.Width < 350) || (Bounds.Height > 1 && Bounds.Height < 170);
        _isUltraCompactMode = scale < 0.72 || (Bounds.Width > 1 && Bounds.Width < 295) || (Bounds.Height > 1 && Bounds.Height < 130);

        var compactMultiplier = _isUltraCompactMode ? 0.76 : _isCompactMode ? 0.88 : 1.0;
        var mainRectangleCornerRadius = ComponentChromeCornerRadiusHelper.ResolveMainRectangleRadius();
        RootBorder.CornerRadius = mainRectangleCornerRadius;
        RootBorder.Padding = new Thickness(
            Math.Clamp(12 * scale * compactMultiplier, 6, 18),
            Math.Clamp(9 * scale * compactMultiplier, 5, 16));

        ContentRootGrid.RowSpacing = _isUltraCompactMode
            ? Math.Clamp(3 * scale, 2, 5)
            : _isCompactMode
                ? Math.Clamp(5 * scale, 3, 7)
                : Math.Clamp(8 * scale, 4, 10);
        HeaderGrid.ColumnSpacing = _isUltraCompactMode
            ? Math.Clamp(6 * scale, 3, 8)
            : Math.Clamp(8 * scale, 4, 10);
        MainGrid.ColumnSpacing = _isUltraCompactMode
            ? Math.Clamp(6 * scale, 3, 8)
            : Math.Clamp(10 * scale, 5, 12);
        StatsPanel.Spacing = _isUltraCompactMode
            ? Math.Clamp(3 * scale, 1, 5)
            : _isCompactMode
                ? Math.Clamp(4 * scale, 2, 6)
                : Math.Clamp(6 * scale, 3, 8);

        TitleTextBlock.FontSize = Math.Clamp(13 * scale, 9, 20);
        ModeTextBlock.FontSize = Math.Clamp(11 * scale, 8, 16);
        DensityValueTextBlock.FontSize = Math.Clamp(58 * scale, 18, 94);
        DensityUnitTextBlock.FontSize = Math.Clamp(15 * scale, 9, 24);
        DensityLevelTextBlock.FontSize = Math.Clamp(13 * scale, 8, 18);
        CountLabelTextBlock.FontSize = Math.Clamp(11 * scale, 8, 14);
        DurationLabelTextBlock.FontSize = Math.Clamp(11 * scale, 8, 14);
        CountValueTextBlock.FontSize = Math.Clamp(22 * scale, 10, 36);
        DurationValueTextBlock.FontSize = Math.Clamp(20 * scale, 9, 32);
        ThresholdTextBlock.FontSize = Math.Clamp(11 * scale, 8, 14);

        DensityValueStack.Spacing = Math.Clamp(6 * scale, 2, 10);
        DensityStackPanel.Spacing = _isUltraCompactMode ? Math.Clamp(1.5 * scale, 1, 3) : Math.Clamp(3 * scale, 1.5, 5);

        ModeBadgeBorder.Padding = new Thickness(
            Math.Clamp(8 * scale * compactMultiplier, 4, 12),
            Math.Clamp(3 * scale * compactMultiplier, 1.5, 6));
        ModeBadgeBorder.CornerRadius = new CornerRadius(Math.Clamp(8 * scale, 4, 12));

        var cardPadding = new Thickness(
            Math.Clamp(10 * scale * compactMultiplier, 5, 14),
            Math.Clamp(6 * scale * compactMultiplier, 3, 9));
        CountCardBorder.Padding = cardPadding;
        DurationCardBorder.Padding = cardPadding;
        CountCardBorder.CornerRadius = mainRectangleCornerRadius;
        DurationCardBorder.CornerRadius = mainRectangleCornerRadius;

        TitleTextBlock.IsVisible = !_isUltraCompactMode;
        ThresholdTextBlock.IsVisible = !_isUltraCompactMode;
        DensityUnitTextBlock.IsVisible = !_isUltraCompactMode;
        CountLabelTextBlock.IsVisible = !_isUltraCompactMode;
        DurationLabelTextBlock.IsVisible = !_isUltraCompactMode;

        ApplyVariableWeights(scale);
        ApplyLocalizedLabels();
    }

    private void ApplyTypographyByBackground(Color panelColor)
    {
        var samples = StudyPanelPalette.BuildSamples(panelColor);
        var primary = AdaptiveBrushFactory.Create(samples, PrimaryColorCandidates, minContrast: 4.5);
        var secondary = AdaptiveBrushFactory.Create(samples, SecondaryColorCandidates, minContrast: 4.5);

        TitleTextBlock.Foreground = secondary;
        DensityUnitTextBlock.Foreground = secondary;
        CountLabelTextBlock.Foreground = secondary;
        DurationLabelTextBlock.Foreground = secondary;
        ThresholdTextBlock.Foreground = secondary;

        DensityValueTextBlock.Foreground = primary;
        DensityLevelTextBlock.Foreground = primary;
        CountValueTextBlock.Foreground = primary;
        DurationValueTextBlock.Foreground = primary;
    }

    private void ApplyModeBadgeColor(Color panelColor, Color baseColor) =>
        StudyPanelPalette.ApplyModeBadge(ModeBadgeBorder, ModeTextBlock, panelColor, baseColor, PrimaryColorCandidates);

    private static InterruptDensityMetrics? ComputeRealtimeDensity(StudyAnalyticsSnapshot snapshot)
    {
        var points = snapshot.RealtimeBuffer;
        if (points.Count < 2)
        {
            return null;
        }

        var weightedDurationMs = 0d;
        var segmentCount = 0;
        var segmentOpen = false;
        DateTimeOffset? lastOverThresholdAt = null;

        for (var i = 0; i < points.Count - 1; i++)
        {
            var current = points[i];
            var next = points[i + 1];
            var dtMs = (next.Timestamp - current.Timestamp).TotalMilliseconds;
            if (dtMs <= 0)
            {
                continue;
            }

            weightedDurationMs += dtMs;

            if (current.IsOverThreshold)
            {
                if (segmentOpen)
                {
                    lastOverThresholdAt = current.Timestamp;
                }
                else
                {
                    var canMerge = lastOverThresholdAt.HasValue &&
                                   (current.Timestamp - lastOverThresholdAt.Value).TotalMilliseconds <= snapshot.Config.SegmentMergeGapMs;
                    if (!canMerge)
                    {
                        segmentCount++;
                    }

                    segmentOpen = true;
                    lastOverThresholdAt = current.Timestamp;
                }
            }
            else if (segmentOpen && lastOverThresholdAt.HasValue)
            {
                var silentGapMs = (current.Timestamp - lastOverThresholdAt.Value).TotalMilliseconds;
                if (silentGapMs > snapshot.Config.SegmentMergeGapMs)
                {
                    segmentOpen = false;
                }
            }
        }

        if (weightedDurationMs <= 0)
        {
            weightedDurationMs = points.Count * snapshot.Config.FrameMs;
        }

        if (weightedDurationMs <= Math.Max(300, snapshot.Config.FrameMs * 3))
        {
            return null;
        }

        var minutes = Math.Max(1d / 60d, weightedDurationMs / 60000d);
        var density = Math.Max(0, segmentCount / minutes);
        var threshold = Math.Max(1, snapshot.Config.MaxSegmentsPerMin);
        var levelKind = ResolveLevelKind(density, threshold);

        return new InterruptDensityMetrics(
            DensityPerMin: Math.Round(density, 2),
            SegmentCount: Math.Max(0, segmentCount),
            Duration: TimeSpan.FromMilliseconds(weightedDurationMs),
            ThresholdPerMin: threshold,
            LevelKind: levelKind);
    }

    private static InterruptDensityMetrics? ComputeSessionDensity(StudyAnalyticsSnapshot snapshot)
    {
        var metrics = snapshot.Session.Metrics;
        if (metrics.EffectiveDuration.TotalMilliseconds <= Math.Max(300, snapshot.Config.FrameMs * 3))
        {
            return null;
        }

        var minutes = Math.Max(1d / 60d, metrics.EffectiveDuration.TotalMinutes);
        var density = Math.Max(0, metrics.TotalSegmentCount / minutes);
        var threshold = Math.Max(1, snapshot.Config.MaxSegmentsPerMin);
        var levelKind = ResolveLevelKind(density, threshold);

        return new InterruptDensityMetrics(
            DensityPerMin: Math.Round(density, 2),
            SegmentCount: Math.Max(0, metrics.TotalSegmentCount),
            Duration: metrics.EffectiveDuration,
            ThresholdPerMin: threshold,
            LevelKind: levelKind);
    }

    private static InterruptDensityMetrics? ComputeReportDensity(StudySessionReport report, StudyAnalyticsConfig config)
    {
        if (!StudySessionReportProjection.TryAggregate(report, config, out var aggregate))
        {
            return null;
        }

        var threshold = Math.Max(1, config.MaxSegmentsPerMin);
        var levelKind = ResolveLevelKind(aggregate.SegmentsPerMin, threshold);
        return new InterruptDensityMetrics(
            DensityPerMin: Math.Round(aggregate.SegmentsPerMin, 2),
            SegmentCount: aggregate.SegmentCount,
            Duration: aggregate.Duration,
            ThresholdPerMin: threshold,
            LevelKind: levelKind);
    }

    private static DensityLevelKind ResolveLevelKind(double densityPerMin, double thresholdPerMin)
    {
        var ratio = densityPerMin / Math.Max(1, thresholdPerMin);
        if (ratio < 0.33)
        {
            return DensityLevelKind.Calm;
        }

        if (ratio < 0.66)
        {
            return DensityLevelKind.Normal;
        }

        if (ratio < 1.0)
        {
            return DensityLevelKind.Frequent;
        }

        return DensityLevelKind.Severe;
    }

    private string ResolveLevelText(DensityLevelKind levelKind)
    {
        return levelKind switch
        {
            DensityLevelKind.Calm => L("study.interrupt_density.level.calm", "Calm"),
            DensityLevelKind.Normal => L("study.interrupt_density.level.normal", "Normal"),
            DensityLevelKind.Frequent => L("study.interrupt_density.level.frequent", "Frequent"),
            DensityLevelKind.Severe => L("study.interrupt_density.level.severe", "Severe"),
            _ => L("study.interrupt_density.level.normal", "Normal")
        };
    }

    private string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalHours >= 1)
        {
            return duration.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture);
        }

        return duration.ToString(@"mm\:ss", CultureInfo.InvariantCulture);
    }

    private void ReloadLanguageCode()
    {
        StudyComponentSettings.Reload(
            ref _languageCode, ref _studyEnabled, _settingsService, _localizationService);
    }

    private void ApplyVariableWeights(double scale)
    {
        var weightProgress = Math.Clamp((scale - 0.52) / 1.5, 0, 1);
        var compactDelta = _isUltraCompactMode ? 40 : _isCompactMode ? 20 : 0;

        TitleTextBlock.FontWeight = ComponentTypography.ToVariableWeight(ComponentTypography.LerpClamped(560, 680, weightProgress));
        ModeTextBlock.FontWeight = ComponentTypography.ToVariableWeight(ComponentTypography.LerpClamped(560, 700, weightProgress));
        DensityValueTextBlock.FontWeight = ComponentTypography.ToVariableWeight(ComponentTypography.LerpClamped(660 + compactDelta, 820, weightProgress));
        DensityUnitTextBlock.FontWeight = ComponentTypography.ToVariableWeight(ComponentTypography.LerpClamped(520, 640, weightProgress));
        DensityLevelTextBlock.FontWeight = ComponentTypography.ToVariableWeight(ComponentTypography.LerpClamped(560, 700, weightProgress));
        CountLabelTextBlock.FontWeight = ComponentTypography.ToVariableWeight(ComponentTypography.LerpClamped(520, 620, weightProgress));
        CountValueTextBlock.FontWeight = ComponentTypography.ToVariableWeight(ComponentTypography.LerpClamped(620 + compactDelta, 780, weightProgress));
        DurationLabelTextBlock.FontWeight = ComponentTypography.ToVariableWeight(ComponentTypography.LerpClamped(520, 620, weightProgress));
        DurationValueTextBlock.FontWeight = ComponentTypography.ToVariableWeight(ComponentTypography.LerpClamped(620 + compactDelta, 760, weightProgress));
        ThresholdTextBlock.FontWeight = ComponentTypography.ToVariableWeight(ComponentTypography.LerpClamped(500, 620, weightProgress));
    }

    private string L(string key, string fallback)
    {
        return _localizationService.GetString(_languageCode, key, fallback);
    }

    private void CheckAndSendAlert(InterruptDensityMetrics metrics, StudyAnalyticsConfig config)
    {
        // 检查提醒开关是否启用
        if (!config.AlertSoundEnabled)
        {
            _lastLevelKind = metrics.LevelKind;
            return;
        }

        // 只在级别变化时发送通知
        if (metrics.LevelKind == _lastLevelKind)
        {
            return;
        }

        // 检查冷却时间
        if (DateTime.Now - _lastAlertTime < _alertCooldown)
        {
            _lastLevelKind = metrics.LevelKind;
            return;
        }

        // 只在严重级别时发送通知
        if (metrics.LevelKind != DensityLevelKind.Severe)
        {
            _lastLevelKind = metrics.LevelKind;
            return;
        }

        _lastAlertTime = DateTime.Now;
        _lastLevelKind = metrics.LevelKind;

        // 发送通知
        try
        {
            var densityStr = metrics.DensityPerMin.ToString("F1");
            var thresholdStr = metrics.ThresholdPerMin.ToString("F1");

            // 判断是否需要显示在正中央（过于吵闹）
            var isSevere = metrics.DensityPerMin > metrics.ThresholdPerMin * 1.5;

            if (isSevere)
            {
                // 严重干扰：显示在正中央
                var title = L("study.alert.severe_interrupt_title", "严重噪音干扰");
                var message = string.Format(
                    CultureInfo.CurrentCulture,
                    L("study.alert.severe_interrupt_message", "环境噪音过于嘈杂，严重影响学习效率\n当前打断密度: {0}次/分钟\n建议：寻找更安静的学习环境"),
                    densityStr);

                App.CurrentNotificationService?.ShowWarning(title, message, NotificationPosition.Center);
            }
            else
            {
                // 一般提醒：显示在右上角
                var title = L("study.alert.noise_interrupt_title", "噪音打断提醒");
                var message = string.Format(
                    CultureInfo.CurrentCulture,
                    L("study.alert.noise_interrupt_message", "当前打断密度: {0}次/分钟\n已超过阈值: {1}次/分钟"),
                    densityStr,
                    thresholdStr);

                App.CurrentNotificationService?.ShowWarning(title, message, NotificationPosition.TopRight);
            }
        }
        catch
        {
            // 静默处理通知发送失败
        }
    }
}
