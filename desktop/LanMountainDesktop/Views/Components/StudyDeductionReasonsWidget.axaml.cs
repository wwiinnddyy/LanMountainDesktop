using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using LanMountainDesktop.Models;
using LanMountainDesktop.Services;
using LanMountainDesktop.Theme;

namespace LanMountainDesktop.Views.Components;

public partial class StudyDeductionReasonsWidget : UserControl, IDesktopComponentWidget, IDesktopPageVisibilityAwareComponentWidget
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

    private readonly record struct DeductionMetrics(
        double SustainedPenalty,
        double TimePenalty,
        double SegmentPenalty,
        double TotalPenalty,
        double Score,
        double P50Dbfs,
        double OverRatio,
        double SegmentsPerMin);

    public StudyDeductionReasonsWidget()
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

        if (!_studyEnabled)
        {
            ModeTextBlock.Text = L("study.widget.disabled_hint", "请在设置中开启");
            ApplyModeBadgeColor(panelColor, StudyPanelPalette.DisabledBadge);
            ApplyLocalizedLabels();
            ApplyUnavailableMetrics();
            return;
        }

        var isSessionRunning = snapshot.Session.State == StudySessionRuntimeState.Running;
        var isSessionReport = snapshot.DataMode == StudyDataMode.SessionReport && snapshot.LastSessionReport is not null;
        var isSessionView = isSessionRunning || isSessionReport;
        ModeTextBlock.Text = isSessionView
            ? L("study.deduction.mode.session", "Session")
            : L("study.deduction.mode.realtime", "Realtime");
        ApplyModeBadgeColor(panelColor, isSessionView ? StudyPanelPalette.SuccessBadge : StudyPanelPalette.RealtimeBadge);

        ApplyLocalizedLabels();

        var metrics = isSessionReport && snapshot.LastSessionReport is not null
            ? ComputeReportDeduction(snapshot.LastSessionReport, snapshot.Config)
            : ComputeRealtimeDeduction(snapshot);
        if (metrics is null)
        {
            ApplyUnavailableMetrics();
            return;
        }

        var m = metrics.Value;
        var sustainedLoss = 100d * 0.40d * m.SustainedPenalty;
        var timeLoss = 100d * 0.30d * m.TimePenalty;
        var segmentLoss = 100d * 0.30d * m.SegmentPenalty;
        var totalLoss = Math.Max(0, 100d * m.TotalPenalty);

        SustainedMetricTextBlock.Text = _isUltraCompactMode
            ? string.Format(CultureInfo.InvariantCulture, L("study.deduction.metric.sustained_short_format", "p50 {0:F1}"), m.P50Dbfs)
            : string.Format(CultureInfo.InvariantCulture, L("study.deduction.metric.sustained_format", "p50 {0:F1} dBFS"), m.P50Dbfs);
        TimeMetricTextBlock.Text = _isUltraCompactMode
            ? string.Format(CultureInfo.InvariantCulture, L("study.deduction.metric.time_short_format", "{0:F1}%"), m.OverRatio * 100d)
            : string.Format(CultureInfo.InvariantCulture, L("study.deduction.metric.time_format", "over {0:F1}%"), m.OverRatio * 100d);
        SegmentMetricTextBlock.Text = _isUltraCompactMode
            ? string.Format(CultureInfo.InvariantCulture, L("study.deduction.metric.segment_short_format", "{0:F1}/m"), m.SegmentsPerMin)
            : string.Format(CultureInfo.InvariantCulture, L("study.deduction.metric.segment_format", "{0:F1}/min"), m.SegmentsPerMin);

        SustainedLossTextBlock.Text = string.Format(CultureInfo.InvariantCulture, L("study.deduction.loss_format", "-{0:F1}"), sustainedLoss);
        TimeLossTextBlock.Text = string.Format(CultureInfo.InvariantCulture, L("study.deduction.loss_format", "-{0:F1}"), timeLoss);
        SegmentLossTextBlock.Text = string.Format(CultureInfo.InvariantCulture, L("study.deduction.loss_format", "-{0:F1}"), segmentLoss);

        TotalLossTextBlock.Text = string.Format(CultureInfo.InvariantCulture, L("study.deduction.total_loss_format", "Total -{0:F1}"), totalLoss);
        ScoreTextBlock.Text = string.Format(CultureInfo.InvariantCulture, L("study.deduction.total_score_format", "Score {0:F1}"), m.Score);
    }

    private void ApplyUnavailableMetrics()
    {
        var unavailable = L("study.deduction.unavailable", "--");
        SustainedMetricTextBlock.Text = unavailable;
        TimeMetricTextBlock.Text = unavailable;
        SegmentMetricTextBlock.Text = unavailable;
        SustainedLossTextBlock.Text = unavailable;
        TimeLossTextBlock.Text = unavailable;
        SegmentLossTextBlock.Text = unavailable;
        TotalLossTextBlock.Text = string.Format(CultureInfo.InvariantCulture, L("study.deduction.total_loss_unavailable", "Total {0}"), unavailable);
        ScoreTextBlock.Text = string.Format(CultureInfo.InvariantCulture, L("study.deduction.total_score_unavailable", "Score {0}"), unavailable);
    }

    private void ApplyLocalizedLabels()
    {
        TitleTextBlock.Text = L("study.deduction.title", "Deduction Reasons");

        SustainedReasonTextBlock.Text = _isCompactMode
            ? L("study.deduction.reason.sustained_short", "Sustained")
            : L("study.deduction.reason.sustained", "Sustained Noise");
        TimeReasonTextBlock.Text = _isCompactMode
            ? L("study.deduction.reason.time_short", "Duration")
            : L("study.deduction.reason.time", "Over-threshold Time");
        SegmentReasonTextBlock.Text = _isCompactMode
            ? L("study.deduction.reason.segment_short", "Interrupt")
            : L("study.deduction.reason.segment", "Interrupt Frequency");
    }

    private void UpdateAdaptiveLayout()
    {
        var cellScale = Math.Clamp(_currentCellSize / ComponentDesignMetrics.BaseCellSize, 0.76, 2.4);
        var widthScale = Bounds.Width > 1 ? Bounds.Width / 420d : cellScale;
        var heightScale = Bounds.Height > 1 ? Bounds.Height / 220d : cellScale;
        var boundsScale = Math.Clamp(Math.Min(widthScale, heightScale), 0.52, 2.2);
        var scale = Math.Clamp(Math.Min(cellScale, boundsScale * 1.06), 0.52, 2.2);

        _isCompactMode = scale < 0.92 || (Bounds.Width > 1 && Bounds.Width < 360) || (Bounds.Height > 1 && Bounds.Height < 180);
        _isUltraCompactMode = scale < 0.72 || (Bounds.Width > 1 && Bounds.Width < 300) || (Bounds.Height > 1 && Bounds.Height < 145);

        var compactMultiplier = _isUltraCompactMode ? 0.76 : _isCompactMode ? 0.88 : 1.0;
        var mainRectangleCornerRadius = ComponentChromeCornerRadiusHelper.ResolveMainRectangleRadius();
        RootBorder.CornerRadius = mainRectangleCornerRadius;
        RootBorder.Padding = new Thickness(
            Math.Clamp(12 * scale * compactMultiplier, 6, 18),
            Math.Clamp(10 * scale * compactMultiplier, 5, 16));

        ContentRootGrid.RowSpacing = _isUltraCompactMode
            ? Math.Clamp(4 * scale, 2, 6)
            : _isCompactMode
                ? Math.Clamp(6 * scale, 3, 7)
                : Math.Clamp(8 * scale, 4, 10);
        HeaderGrid.ColumnSpacing = _isUltraCompactMode
            ? Math.Clamp(6 * scale, 3, 8)
            : Math.Clamp(8 * scale, 4, 10);
        ReasonsListPanel.Spacing = _isUltraCompactMode
            ? Math.Clamp(3 * scale, 1, 5)
            : _isCompactMode
                ? Math.Clamp(4 * scale, 2, 6)
                : Math.Clamp(6 * scale, 3, 8);

        TitleTextBlock.FontSize = Math.Clamp(13 * scale, 9, 20);
        ModeTextBlock.FontSize = Math.Clamp(11 * scale, 8, 16);
        SustainedReasonTextBlock.FontSize = Math.Clamp(13 * scale, 9, 18);
        TimeReasonTextBlock.FontSize = Math.Clamp(13 * scale, 9, 18);
        SegmentReasonTextBlock.FontSize = Math.Clamp(13 * scale, 9, 18);

        SustainedMetricTextBlock.FontSize = Math.Clamp(11 * scale, 8, 14);
        TimeMetricTextBlock.FontSize = Math.Clamp(11 * scale, 8, 14);
        SegmentMetricTextBlock.FontSize = Math.Clamp(11 * scale, 8, 14);

        SustainedLossTextBlock.FontSize = Math.Clamp(19 * scale, 11, 28);
        TimeLossTextBlock.FontSize = Math.Clamp(19 * scale, 11, 28);
        SegmentLossTextBlock.FontSize = Math.Clamp(19 * scale, 11, 28);

        TotalLossTextBlock.FontSize = Math.Clamp(12 * scale, 8, 16);
        ScoreTextBlock.FontSize = Math.Clamp(12 * scale, 8, 16);

        ModeBadgeBorder.Padding = new Thickness(
            Math.Clamp(8 * scale * compactMultiplier, 4, 12),
            Math.Clamp(3 * scale * compactMultiplier, 1.5, 6));
        ModeBadgeBorder.CornerRadius = new CornerRadius(Math.Clamp(8 * scale, 4, 12));

        var rowPadding = new Thickness(
            Math.Clamp(10 * scale * compactMultiplier, 5, 14),
            Math.Clamp(7 * scale * compactMultiplier, 3, 10));
        SustainedRowBorder.Padding = rowPadding;
        TimeRowBorder.Padding = rowPadding;
        SegmentRowBorder.Padding = rowPadding;
        SustainedRowBorder.CornerRadius = mainRectangleCornerRadius;
        TimeRowBorder.CornerRadius = mainRectangleCornerRadius;
        SegmentRowBorder.CornerRadius = mainRectangleCornerRadius;

        SustainedMetricTextBlock.IsVisible = !_isUltraCompactMode;
        TimeMetricTextBlock.IsVisible = !_isUltraCompactMode;
        SegmentMetricTextBlock.IsVisible = !_isUltraCompactMode;
        TitleTextBlock.IsVisible = !_isUltraCompactMode;

        ApplyVariableWeights(scale);
        ApplyLocalizedLabels();
    }

    private void ApplyTypographyByBackground(Color panelColor)
    {
        var samples = StudyPanelPalette.BuildSamples(panelColor);
        var primary = AdaptiveBrushFactory.Create(samples, PrimaryColorCandidates, minContrast: 4.5);
        var secondary = AdaptiveBrushFactory.Create(samples, SecondaryColorCandidates, minContrast: 4.5);

        TitleTextBlock.Foreground = secondary;
        SustainedMetricTextBlock.Foreground = secondary;
        TimeMetricTextBlock.Foreground = secondary;
        SegmentMetricTextBlock.Foreground = secondary;
        TotalLossTextBlock.Foreground = secondary;

        SustainedReasonTextBlock.Foreground = primary;
        TimeReasonTextBlock.Foreground = primary;
        SegmentReasonTextBlock.Foreground = primary;
        SustainedLossTextBlock.Foreground = primary;
        TimeLossTextBlock.Foreground = primary;
        SegmentLossTextBlock.Foreground = primary;
        ScoreTextBlock.Foreground = primary;
    }

    private void ApplyModeBadgeColor(Color panelColor, Color baseColor) =>
        StudyPanelPalette.ApplyModeBadge(ModeBadgeBorder, ModeTextBlock, panelColor, baseColor, PrimaryColorCandidates);

    private static DeductionMetrics? ComputeRealtimeDeduction(StudyAnalyticsSnapshot snapshot)
    {
        var points = snapshot.RealtimeBuffer;
        if (points.Count < 2)
        {
            return null;
        }

        var start = points[0].Timestamp;
        var end = points[^1].Timestamp;
        var totalDurationMs = (end - start).TotalMilliseconds;
        if (totalDurationMs <= Math.Max(300, snapshot.Config.FrameMs * 3))
        {
            return null;
        }

        var dbfsValues = points.Select(p => p.Dbfs).OrderBy(v => v).ToArray();
        var p50Dbfs = StudyStatistics.Percentile(dbfsValues, 0.50, emptySentinel: -100);

        var overDurationMs = 0d;
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
                overDurationMs += dtMs;
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

        var overRatio = Math.Clamp(overDurationMs / Math.Max(1, weightedDurationMs), 0, 1);
        var minutes = Math.Max(1d / 60d, weightedDurationMs / 60000d);
        var segmentsPerMin = segmentCount / minutes;

        var sustainedPenalty = Clamp01((p50Dbfs - snapshot.Config.ScoreThresholdDbfs) / 6d);
        var timePenalty = Clamp01(overRatio / 0.30d);
        var segmentPenalty = Clamp01(segmentsPerMin / Math.Max(1, snapshot.Config.MaxSegmentsPerMin));
        var totalPenalty = (0.40d * sustainedPenalty) + (0.30d * timePenalty) + (0.30d * segmentPenalty);
        var score = Math.Clamp(100d * (1d - totalPenalty), 0, 100);

        return new DeductionMetrics(
            SustainedPenalty: Math.Round(sustainedPenalty, 4),
            TimePenalty: Math.Round(timePenalty, 4),
            SegmentPenalty: Math.Round(segmentPenalty, 4),
            TotalPenalty: Math.Round(totalPenalty, 4),
            Score: Math.Round(score, 1),
            P50Dbfs: Math.Round(p50Dbfs, 2),
            OverRatio: Math.Round(overRatio, 4),
            SegmentsPerMin: Math.Round(segmentsPerMin, 3));
    }

    private static DeductionMetrics? ComputeReportDeduction(StudySessionReport report, StudyAnalyticsConfig config)
    {
        if (!StudySessionReportProjection.TryAggregate(report, config, out var aggregate))
        {
            return null;
        }

        return new DeductionMetrics(
            SustainedPenalty: aggregate.SustainedPenalty,
            TimePenalty: aggregate.TimePenalty,
            SegmentPenalty: aggregate.SegmentPenalty,
            TotalPenalty: aggregate.TotalPenalty,
            Score: aggregate.Score,
            P50Dbfs: aggregate.P50Dbfs,
            OverRatio: aggregate.OverRatio,
            SegmentsPerMin: aggregate.SegmentsPerMin);
    }

    private static double Clamp01(double value)
    {
        return Math.Clamp(value, 0, 1);
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

        SustainedReasonTextBlock.FontWeight = ComponentTypography.ToVariableWeight(ComponentTypography.LerpClamped(560, 690, weightProgress));
        TimeReasonTextBlock.FontWeight = ComponentTypography.ToVariableWeight(ComponentTypography.LerpClamped(560, 690, weightProgress));
        SegmentReasonTextBlock.FontWeight = ComponentTypography.ToVariableWeight(ComponentTypography.LerpClamped(560, 690, weightProgress));

        SustainedMetricTextBlock.FontWeight = ComponentTypography.ToVariableWeight(ComponentTypography.LerpClamped(480, 600, weightProgress));
        TimeMetricTextBlock.FontWeight = ComponentTypography.ToVariableWeight(ComponentTypography.LerpClamped(480, 600, weightProgress));
        SegmentMetricTextBlock.FontWeight = ComponentTypography.ToVariableWeight(ComponentTypography.LerpClamped(480, 600, weightProgress));

        SustainedLossTextBlock.FontWeight = ComponentTypography.ToVariableWeight(ComponentTypography.LerpClamped(640 + compactDelta, 800, weightProgress));
        TimeLossTextBlock.FontWeight = ComponentTypography.ToVariableWeight(ComponentTypography.LerpClamped(640 + compactDelta, 800, weightProgress));
        SegmentLossTextBlock.FontWeight = ComponentTypography.ToVariableWeight(ComponentTypography.LerpClamped(640 + compactDelta, 800, weightProgress));

        TotalLossTextBlock.FontWeight = ComponentTypography.ToVariableWeight(ComponentTypography.LerpClamped(560, 700, weightProgress));
        ScoreTextBlock.FontWeight = ComponentTypography.ToVariableWeight(ComponentTypography.LerpClamped(560, 700, weightProgress));
    }

    private string L(string key, string fallback)
    {
        return _localizationService.GetString(_languageCode, key, fallback);
    }
}
