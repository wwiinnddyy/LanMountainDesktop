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

public partial class StudyScoreOverviewWidget : UserControl, IDesktopComponentWidget, IDesktopPageVisibilityAwareComponentWidget
{
    private static readonly Color[] ValueColorCandidates =
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

    private readonly Queue<(DateTimeOffset Timestamp, double Score)> _realtimeHistory = new();

    private double _currentCellSize = ComponentDesignMetrics.BaseCellSize;
    private bool _isAttached;
    private bool _isOnActivePage = true;
    private bool _isSubscribed;
    private bool _isCompactMode;
    private bool _isUltraCompactMode;
    private bool _isExpandedMode;
    private bool _studyEnabled = true;
    private string _languageCode = LocalizationService.DefaultLanguageCode;
    private IDisposable? _monitoringLease;

    public StudyScoreOverviewWidget()
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

    public void SetDesktopPageContext(bool isOnActivePage, bool isEditMode)
    {
        _ = isEditMode;
        var wasOnActivePage = _isOnActivePage;
        _isOnActivePage = isOnActivePage;
        UpdateMonitoringLeaseState();
        if (isOnActivePage && !wasOnActivePage)
        {
            RefreshVisual();
        }
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _isAttached = true;
        ReloadLanguageCode();
        StudySnapshotSubscription.Subscribe(ref _isSubscribed, _studyAnalyticsService, _renderGate.HandleSnapshotUpdated);

        UpdateMonitoringLeaseState();
        RefreshVisual();
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _isAttached = false;
        StudyMonitoringLease.Release(ref _monitoringLease);
        _renderGate.Clear();

        StudySnapshotSubscription.Unsubscribe(ref _isSubscribed, _studyAnalyticsService, _renderGate.HandleSnapshotUpdated);
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        UpdateAdaptiveLayout();
        ApplyTypographyByBackground(StudyPanelPalette.Resolve(this, RootBorder.Background));
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
        ApplyLocalizedLabels();

        var panelColor = StudyPanelPalette.Resolve(this, RootBorder.Background);
        ApplyTypographyByBackground(panelColor);

        if (!_studyEnabled)
        {
            TitleTextBlock.Text = L("study.widget.disabled_title", "自习功能未启用");
            ModeTextBlock.Text = L("study.widget.disabled_hint", "请在设置中开启");
            CurrentScoreTextBlock.Text = "--";
            CurrentLabelTextBlock.Text = "";
            return;
        }

        var realtimeScore = ComputeRealtimeScore(snapshot);
        if (snapshot.DataMode == StudyDataMode.Realtime && realtimeScore is { } score)
        {
            PushRealtimeScore(score, DateTimeOffset.UtcNow);
        }

        var isSessionRunning = snapshot.Session.State == StudySessionRuntimeState.Running;
        var isSessionReport = snapshot.DataMode == StudyDataMode.SessionReport && snapshot.LastSessionReport is not null;
        if (isSessionReport)
        {
            ApplySessionReportMode(snapshot, panelColor);
            return;
        }

        if (isSessionRunning)
        {
            ApplySessionMode(snapshot, realtimeScore, panelColor);
            return;
        }

        ApplyRealtimeMode(snapshot, realtimeScore, panelColor);
    }

    private void ApplySessionMode(StudyAnalyticsSnapshot snapshot, double? realtimeScore, Color panelColor)
    {
        var currentScore = realtimeScore ?? snapshot.Session.Metrics.CurrentScore;
        var avgScore = snapshot.Session.Metrics.AvgScore;
        var minScore = snapshot.Session.Metrics.MinScore;
        var maxScore = snapshot.Session.Metrics.MaxScore;

        ModeTextBlock.Text = L("study.score_overview.mode.session", "Session");
        ApplyModeBadgeColor(panelColor, StudyPanelPalette.SuccessBadge);

        CurrentScoreTextBlock.Text = FormatScoreOrUnavailable(currentScore);
        AverageValueTextBlock.Text = FormatScoreOrUnavailable(avgScore);
        MinimumValueTextBlock.Text = FormatScoreOrUnavailable(minScore);
        MaximumValueTextBlock.Text = FormatScoreOrUnavailable(maxScore);
    }

    private void ApplyRealtimeMode(StudyAnalyticsSnapshot snapshot, double? realtimeScore, Color panelColor)
    {
        ModeTextBlock.Text = L("study.score_overview.mode.realtime", "Realtime");
        ApplyModeBadgeColor(panelColor, StudyPanelPalette.RealtimeBadge);

        var currentScore = realtimeScore ?? snapshot.LatestSlice?.Score;
        var historyStats = GetHistoryStats();

        CurrentScoreTextBlock.Text = FormatScoreOrUnavailable(currentScore);
        AverageValueTextBlock.Text = FormatScoreOrUnavailable(historyStats.Average);
        MinimumValueTextBlock.Text = FormatScoreOrUnavailable(historyStats.Minimum);
        MaximumValueTextBlock.Text = FormatScoreOrUnavailable(historyStats.Maximum);
    }

    private void ApplySessionReportMode(StudyAnalyticsSnapshot snapshot, Color panelColor)
    {
        var report = snapshot.LastSessionReport;
        if (report is null)
        {
            ApplyRealtimeMode(snapshot, realtimeScore: null, panelColor);
            return;
        }

        ModeTextBlock.Text = L("study.score_overview.mode.session", "Session");
        ApplyModeBadgeColor(panelColor, StudyPanelPalette.SuccessBadge);

        CurrentScoreTextBlock.Text = FormatScoreOrUnavailable(report.Metrics.CurrentScore);
        AverageValueTextBlock.Text = FormatScoreOrUnavailable(report.Metrics.AvgScore);
        MinimumValueTextBlock.Text = FormatScoreOrUnavailable(report.Metrics.MinScore);
        MaximumValueTextBlock.Text = FormatScoreOrUnavailable(report.Metrics.MaxScore);
    }

    private void ApplyLocalizedLabels()
    {
        TitleTextBlock.Text = L("study.score_overview.title", "Study Score");
        CurrentLabelTextBlock.Text = L("study.score_overview.current", "Current");
        AverageLabelTextBlock.Text = _isCompactMode
            ? L("study.score_overview.average_short", "Avg")
            : L("study.score_overview.average", "Average");
        MinimumLabelTextBlock.Text = _isCompactMode
            ? L("study.score_overview.minimum_short", "Min")
            : L("study.score_overview.minimum", "Minimum");
        MaximumLabelTextBlock.Text = _isCompactMode
            ? L("study.score_overview.maximum_short", "Max")
            : L("study.score_overview.maximum", "Maximum");
    }

    private void UpdateAdaptiveLayout()
    {
        var cellScale = Math.Clamp(_currentCellSize / ComponentDesignMetrics.BaseCellSize, 0.76, 2.4);
        var widthScale = Bounds.Width > 1 ? Bounds.Width / 360d : cellScale;
        var heightScale = Bounds.Height > 1 ? Bounds.Height / 360d : cellScale;
        var boundsScale = Math.Clamp(Math.Min(widthScale, heightScale), 0.52, 2.4);
        var scale = Math.Clamp(Math.Min(cellScale, boundsScale * 1.06), 0.52, 2.4);

        _isCompactMode = scale < 0.92 || (Bounds.Width > 1 && Bounds.Width < 320) || (Bounds.Height > 1 && Bounds.Height < 300);
        _isUltraCompactMode = scale < 0.72 || (Bounds.Width > 1 && Bounds.Width < 270) || (Bounds.Height > 1 && Bounds.Height < 250);
        _isExpandedMode = !_isCompactMode && (scale > 1.12 || (Bounds.Width > 1 && Bounds.Width >= 430) || (Bounds.Height > 1 && Bounds.Height >= 430));

        var compactMultiplier = _isUltraCompactMode ? 0.76 : _isCompactMode ? 0.88 : 1.0;
        var expandedMultiplier = _isExpandedMode ? 1.12 : 1.0;
        var mainRectangleCornerRadius = ComponentChromeCornerRadiusHelper.ResolveMainRectangleRadius();
        RootBorder.CornerRadius = mainRectangleCornerRadius;
        RootBorder.Padding = new Thickness(
            Math.Clamp(16 * scale * compactMultiplier * expandedMultiplier, 8, 30),
            Math.Clamp(14 * scale * compactMultiplier * expandedMultiplier, 6, 26));

        ContentRootGrid.RowSpacing = _isUltraCompactMode
            ? Math.Clamp(4 * scale, 2, 5)
            : _isCompactMode
                ? Math.Clamp(6 * scale, 3, 7)
                : _isExpandedMode
                    ? Math.Clamp(10 * scale, 6, 16)
                    : Math.Clamp(8 * scale, 4, 10);
        TopRowGrid.ColumnSpacing = _isUltraCompactMode
            ? Math.Clamp(6 * scale, 3, 8)
            : Math.Clamp(8 * scale, 4, 10);
        SummaryGrid.ColumnSpacing = _isUltraCompactMode
            ? Math.Clamp(5 * scale, 3, 7)
            : _isCompactMode
                ? Math.Clamp(7 * scale, 4, 9)
                : _isExpandedMode
                    ? Math.Clamp(14 * scale, 8, 20)
                    : Math.Clamp(10 * scale, 6, 12);

        var headlineFactor = _isUltraCompactMode ? 0.62 : _isCompactMode ? 0.80 : _isExpandedMode ? 1.22 : 1.02;
        var statFactor = _isUltraCompactMode ? 0.74 : _isCompactMode ? 0.90 : _isExpandedMode ? 1.36 : 1.04;
        var labelFactor = _isUltraCompactMode ? 0.84 : _isCompactMode ? 0.92 : _isExpandedMode ? 1.14 : 1.0;

        TitleTextBlock.FontSize = Math.Clamp(14 * scale * labelFactor, 9, 30);
        ModeTextBlock.FontSize = Math.Clamp(12 * scale * labelFactor, 8, 22);
        CurrentLabelTextBlock.FontSize = Math.Clamp(12 * scale * labelFactor, 8, 22);
        CurrentScoreTextBlock.FontSize = Math.Clamp(76 * scale * headlineFactor, 22, 190);

        AverageLabelTextBlock.FontSize = Math.Clamp(11 * scale * labelFactor, 8, 20);
        MinimumLabelTextBlock.FontSize = Math.Clamp(11 * scale * labelFactor, 8, 20);
        MaximumLabelTextBlock.FontSize = Math.Clamp(11 * scale * labelFactor, 8, 20);
        AverageValueTextBlock.FontSize = Math.Clamp(22 * scale * statFactor, 11, 64);
        MinimumValueTextBlock.FontSize = Math.Clamp(22 * scale * statFactor, 11, 64);
        MaximumValueTextBlock.FontSize = Math.Clamp(22 * scale * statFactor, 11, 64);

        ModeBadgeBorder.Padding = new Thickness(
            Math.Clamp(8 * scale * compactMultiplier, 4, 12),
            Math.Clamp(3 * scale * compactMultiplier, 1.6, 6));
        ModeBadgeBorder.CornerRadius = new CornerRadius(Math.Clamp(8 * scale, 5, 14));

        var cardPadding = new Thickness(
            Math.Clamp(10 * scale * compactMultiplier * expandedMultiplier, 6, 20),
            Math.Clamp(8 * scale * compactMultiplier * expandedMultiplier, 4, 16));
        var cardCornerRadius = mainRectangleCornerRadius;
        AverageCardBorder.Padding = cardPadding;
        MinimumCardBorder.Padding = cardPadding;
        MaximumCardBorder.Padding = cardPadding;
        AverageCardBorder.CornerRadius = cardCornerRadius;
        MinimumCardBorder.CornerRadius = cardCornerRadius;
        MaximumCardBorder.CornerRadius = cardCornerRadius;

        SummaryGrid.Margin = new Thickness(
            0,
            _isUltraCompactMode ? 0 : _isExpandedMode ? Math.Clamp(8 * scale, 4, 18) : Math.Clamp(3 * scale, 1, 8),
            0,
            0);
        CurrentScoreTextBlock.Margin = new Thickness(
            0,
            _isUltraCompactMode ? 0 : Math.Clamp(2 * scale, 1, 5),
            0,
            _isExpandedMode ? Math.Clamp(8 * scale, 4, 16) : Math.Clamp(4 * scale, 2, 8));

        TitleTextBlock.IsVisible = !_isUltraCompactMode;
        CurrentLabelTextBlock.IsVisible = !_isUltraCompactMode;
        AverageLabelTextBlock.IsVisible = !_isUltraCompactMode;
        MinimumLabelTextBlock.IsVisible = !_isUltraCompactMode;
        MaximumLabelTextBlock.IsVisible = !_isUltraCompactMode;

        AverageStack.Spacing = _isUltraCompactMode ? 0 : Math.Clamp(2 * scale, 1, 4);
        MinimumStack.Spacing = _isUltraCompactMode ? 0 : Math.Clamp(2 * scale, 1, 4);
        MaximumStack.Spacing = _isUltraCompactMode ? 0 : Math.Clamp(2 * scale, 1, 4);

        ApplyVariableWeights(scale);
        ApplyLocalizedLabels();
    }

    private void PushRealtimeScore(double score, DateTimeOffset now)
    {
        _realtimeHistory.Enqueue((now, score));

        var cutoff = now - TimeSpan.FromMinutes(8);
        while (_realtimeHistory.Count > 0 && _realtimeHistory.Peek().Timestamp < cutoff)
        {
            _realtimeHistory.Dequeue();
        }

        while (_realtimeHistory.Count > 960)
        {
            _realtimeHistory.Dequeue();
        }
    }

    private (double? Average, double? Minimum, double? Maximum) GetHistoryStats()
    {
        if (_realtimeHistory.Count == 0)
        {
            return (null, null, null);
        }

        var values = _realtimeHistory.Select(item => item.Score).ToArray();
        if (values.Length == 0)
        {
            return (null, null, null);
        }

        return
        (
            Average: values.Average(),
            Minimum: values.Min(),
            Maximum: values.Max()
        );
    }

    private static double? ComputeRealtimeScore(StudyAnalyticsSnapshot snapshot)
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
        var p50Dbfs = Percentile(dbfsValues, 0.50);

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

        var sustainedPenalty = Clamp01((p50Dbfs - snapshot.Config.ScoreThresholdDbfs) / 6d);
        var timePenalty = Clamp01(overRatio / 0.30d);
        var segmentsPerMin = segmentCount / minutes;
        var segmentPenalty = Clamp01(segmentsPerMin / Math.Max(1, snapshot.Config.MaxSegmentsPerMin));

        var totalPenalty = (0.40d * sustainedPenalty) + (0.30d * timePenalty) + (0.30d * segmentPenalty);
        var score = Math.Clamp(100d * (1d - totalPenalty), 0, 100);
        return Math.Round(score, 1);
    }

    private static double Percentile(double[] sortedValues, double percentile)
    {
        if (sortedValues.Length == 0)
        {
            return -100;
        }

        if (sortedValues.Length == 1)
        {
            return sortedValues[0];
        }

        var clamped = Math.Clamp(percentile, 0, 1);
        var position = (sortedValues.Length - 1) * clamped;
        var lower = (int)Math.Floor(position);
        var upper = (int)Math.Ceiling(position);
        if (lower == upper)
        {
            return sortedValues[lower];
        }

        var factor = position - lower;
        return sortedValues[lower] + ((sortedValues[upper] - sortedValues[lower]) * factor);
    }

    private static double Clamp01(double value)
    {
        return Math.Clamp(value, 0, 1);
    }

    private string FormatScoreOrUnavailable(double? score)
    {
        if (!score.HasValue || double.IsNaN(score.Value) || double.IsInfinity(score.Value))
        {
            return L("study.score_overview.unavailable", "--");
        }

        return score.Value.ToString("F1", CultureInfo.InvariantCulture);
    }

    private void ApplyTypographyByBackground(Color panelColor)
    {
        var samples = StudyPanelPalette.BuildSamples(panelColor);
        var primary = AdaptiveBrushFactory.Create(samples, ValueColorCandidates, minContrast: 4.5);
        var secondary = AdaptiveBrushFactory.Create(samples, SecondaryColorCandidates, minContrast: 4.5);
        var isBrightPanel = StudyPanelPalette.IsBrightPanel(panelColor);
        var cardBackground = isBrightPanel
            ? Color.FromArgb(0x42, 0x00, 0x00, 0x00)
            : Color.FromArgb(0x2A, 0xFF, 0xFF, 0xFF);
        var cardBorder = isBrightPanel
            ? Color.FromArgb(0x52, 0xFF, 0xFF, 0xFF)
            : Color.FromArgb(0x34, 0xFF, 0xFF, 0xFF);

        TitleTextBlock.Foreground = secondary;
        CurrentLabelTextBlock.Foreground = secondary;
        AverageLabelTextBlock.Foreground = secondary;
        MinimumLabelTextBlock.Foreground = secondary;
        MaximumLabelTextBlock.Foreground = secondary;

        CurrentScoreTextBlock.Foreground = primary;
        AverageValueTextBlock.Foreground = primary;
        MinimumValueTextBlock.Foreground = primary;
        MaximumValueTextBlock.Foreground = primary;

        AverageCardBorder.Background = new SolidColorBrush(cardBackground);
        MinimumCardBorder.Background = new SolidColorBrush(cardBackground);
        MaximumCardBorder.Background = new SolidColorBrush(cardBackground);
        AverageCardBorder.BorderBrush = new SolidColorBrush(cardBorder);
        MinimumCardBorder.BorderBrush = new SolidColorBrush(cardBorder);
        MaximumCardBorder.BorderBrush = new SolidColorBrush(cardBorder);
    }

    private void ApplyModeBadgeColor(Color panelColor, Color baseColor)
    {
        var badgeColor = StudyPanelPalette.ResolveBadgeColor(panelColor, baseColor);
        ModeBadgeBorder.Background = new SolidColorBrush(badgeColor);
        ModeBadgeBorder.BorderBrush = StudyPanelPalette.BadgeBorderBrush;
        ModeTextBlock.Foreground = StudyPanelPalette.ResolveBadgeForeground(panelColor, badgeColor, ValueColorCandidates);
    }

    private void ReloadLanguageCode()
    {
        var snapshot = _settingsService.Load();
        _languageCode = _localizationService.NormalizeLanguageCode(snapshot.LanguageCode);
        _studyEnabled = snapshot.StudyEnabled;
    }

    private void ApplyVariableWeights(double scale)
    {
        var weightProgress = Math.Clamp((scale - 0.52) / 1.6, 0, 1);
        var compactDelta = _isUltraCompactMode ? 40 : _isCompactMode ? 20 : 0;
        var expandedDelta = _isExpandedMode ? 18 : 0;

        TitleTextBlock.FontWeight = ComponentTypography.ToVariableWeight(ComponentTypography.LerpClamped(560, 680, weightProgress));
        ModeTextBlock.FontWeight = ComponentTypography.ToVariableWeight(ComponentTypography.LerpClamped(560, 700, weightProgress));
        CurrentLabelTextBlock.FontWeight = ComponentTypography.ToVariableWeight(ComponentTypography.LerpClamped(520, 640, weightProgress));
        CurrentScoreTextBlock.FontWeight = ComponentTypography.ToVariableWeight(ComponentTypography.LerpClamped(640 + compactDelta + expandedDelta, 830, weightProgress));

        AverageLabelTextBlock.FontWeight = ComponentTypography.ToVariableWeight(ComponentTypography.LerpClamped(500, 620, weightProgress));
        MinimumLabelTextBlock.FontWeight = ComponentTypography.ToVariableWeight(ComponentTypography.LerpClamped(500, 620, weightProgress));
        MaximumLabelTextBlock.FontWeight = ComponentTypography.ToVariableWeight(ComponentTypography.LerpClamped(500, 620, weightProgress));
        AverageValueTextBlock.FontWeight = ComponentTypography.ToVariableWeight(ComponentTypography.LerpClamped(620 + compactDelta + expandedDelta, 780, weightProgress));
        MinimumValueTextBlock.FontWeight = ComponentTypography.ToVariableWeight(ComponentTypography.LerpClamped(620 + compactDelta + expandedDelta, 780, weightProgress));
        MaximumValueTextBlock.FontWeight = ComponentTypography.ToVariableWeight(ComponentTypography.LerpClamped(620 + compactDelta + expandedDelta, 780, weightProgress));
    }

    private string L(string key, string fallback)
    {
        return _localizationService.GetString(_languageCode, key, fallback);
    }
}
