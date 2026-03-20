using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using LanMountainDesktop.DesktopComponents.Runtime;
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

    private static readonly FontFamily MiSansVariableFontFamily = new("MiSans VF, avares://LanMountainDesktop/Assets/Fonts#MiSans");
    private static readonly Color DarkSubstrate = Color.Parse("#FF0B1220");
    private static readonly Color LightSubstrate = Color.Parse("#FFF1F5FA");

    private readonly IStudyAnalyticsService _studyAnalyticsService = StudyAnalyticsServiceFactory.CreateDefault();
    private LanMountainDesktop.PluginSdk.ISettingsService _settingsService = LanMountainDesktop.Services.Settings.HostSettingsFacadeProvider.GetOrCreate().Settings;
    private readonly LocalizationService _localizationService = new();
    private readonly DispatcherTimer _uiTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(250)
    };

    private double _currentCellSize = 48;
    private bool _isAttached;
    private bool _isOnActivePage = true;
    private bool _isCompactMode;
    private bool _isUltraCompactMode;
    private string _languageCode = "zh-CN";

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

        _uiTimer.Tick += OnUiTimerTick;
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
        SizeChanged += OnSizeChanged;
        ActualThemeVariantChanged += OnActualThemeVariantChanged;

        ApplyVariableFontFamily();
        ReloadLanguageCode();
        ApplyCellSize(_currentCellSize);
        RefreshVisual();
    }

    public void ApplyCellSize(double cellSize)
    {
        _currentCellSize = Math.Max(1, cellSize);
        UpdateAdaptiveLayout();
    }

    public void SetDesktopPageContext(bool isOnActivePage, bool isEditMode)
    {
        _ = isEditMode;
        _isOnActivePage = isOnActivePage;
        UpdateTimerState();
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _isAttached = true;
        ReloadLanguageCode();
        _ = _studyAnalyticsService.StartOrResumeMonitoring();
        UpdateTimerState();
        RefreshVisual();
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _isAttached = false;
        _uiTimer.Stop();
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        UpdateAdaptiveLayout();
        ApplyTypographyByBackground(ResolvePanelBackgroundColor());
    }

    private void OnActualThemeVariantChanged(object? sender, EventArgs e)
    {
        RefreshVisual();
    }

    private void OnUiTimerTick(object? sender, EventArgs e)
    {
        RefreshVisual();
    }

    private void UpdateTimerState()
    {
        if (_isAttached && _isOnActivePage)
        {
            if (!_uiTimer.IsEnabled)
            {
                _uiTimer.Start();
            }

            return;
        }

        _uiTimer.Stop();
    }

    private void RefreshVisual()
    {
        var snapshot = _studyAnalyticsService.GetSnapshot();
        var panelColor = ResolvePanelBackgroundColor();
        ApplyTypographyByBackground(panelColor);

        var isSessionRunning = snapshot.Session.State == StudySessionRuntimeState.Running;
        var isSessionReport = snapshot.DataMode == StudyDataMode.SessionReport && snapshot.LastSessionReport is not null;
        var isSessionView = isSessionRunning || isSessionReport;
        ModeTextBlock.Text = isSessionView
            ? L("study.deduction.mode.session", "Session")
            : L("study.deduction.mode.realtime", "Realtime");
        ApplyModeBadgeColor(panelColor, isSessionView ? Color.Parse("#FF0F6B49") : Color.Parse("#FF2F5DA8"));

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
        var cellScale = Math.Clamp(_currentCellSize / 48d, 0.76, 2.4);
        var widthScale = Bounds.Width > 1 ? Bounds.Width / 420d : cellScale;
        var heightScale = Bounds.Height > 1 ? Bounds.Height / 220d : cellScale;
        var boundsScale = Math.Clamp(Math.Min(widthScale, heightScale), 0.52, 2.2);
        var scale = Math.Clamp(Math.Min(cellScale, boundsScale * 1.06), 0.52, 2.2);

        _isCompactMode = scale < 0.92 || (Bounds.Width > 1 && Bounds.Width < 360) || (Bounds.Height > 1 && Bounds.Height < 180);
        _isUltraCompactMode = scale < 0.72 || (Bounds.Width > 1 && Bounds.Width < 300) || (Bounds.Height > 1 && Bounds.Height < 145);

        var compactMultiplier = _isUltraCompactMode ? 0.76 : _isCompactMode ? 0.88 : 1.0;
        RootBorder.CornerRadius = ComponentChromeCornerRadiusHelper.Scale(_currentCellSize * 0.46, 12, 34);
        RootBorder.Padding = new Thickness(
            ComponentChromeCornerRadiusHelper.SafeValue(12 * scale * compactMultiplier, 6, 18),
            ComponentChromeCornerRadiusHelper.SafeValue(10 * scale * compactMultiplier, 5, 16));

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
            ComponentChromeCornerRadiusHelper.SafeValue(10 * scale * compactMultiplier, 5, 14),
            ComponentChromeCornerRadiusHelper.SafeValue(7 * scale * compactMultiplier, 3, 10));
        SustainedRowBorder.Padding = rowPadding;
        TimeRowBorder.Padding = rowPadding;
        SegmentRowBorder.Padding = rowPadding;

        SustainedMetricTextBlock.IsVisible = !_isUltraCompactMode;
        TimeMetricTextBlock.IsVisible = !_isUltraCompactMode;
        SegmentMetricTextBlock.IsVisible = !_isUltraCompactMode;
        TitleTextBlock.IsVisible = !_isUltraCompactMode;

        ApplyVariableWeights(scale);
        ApplyLocalizedLabels();

        var contentWidth = Math.Max(120, (Bounds.Width > 1 ? Bounds.Width : _currentCellSize * 8) - RootBorder.Padding.Left - RootBorder.Padding.Right);
        var contentHeight = Math.Max(78, (Bounds.Height > 1 ? Bounds.Height : _currentCellSize * 3) - RootBorder.Padding.Top - RootBorder.Padding.Bottom);

        var titleLayout = ComponentTypographyLayoutService.FitAdaptiveTextLayout(
            TitleTextBlock.Text,
            Math.Max(120, contentWidth * 0.44),
            Math.Max(18, contentHeight * 0.22),
            1,
            1,
            9,
            Math.Clamp(20 * scale, 9, 20),
            [TitleTextBlock.FontWeight],
            1.05);
        TitleTextBlock.FontSize = titleLayout.FontSize;
        TitleTextBlock.FontWeight = titleLayout.Weight;
        TitleTextBlock.MaxLines = 1;
        TitleTextBlock.TextWrapping = TextWrapping.NoWrap;
        TitleTextBlock.LineHeight = titleLayout.LineHeight;

        var modeBadgeBox = ComponentTypographyLayoutService.ResolveBadgeBox(
            Math.Max(64, contentWidth * 0.24),
            Math.Max(20, contentHeight * 0.14),
            preferredSizeScale: 0.48d,
            minSize: 18,
            maxSize: 42,
            insetScale: 0.18d);
        ModeBadgeBorder.Padding = modeBadgeBox.Padding;
        ModeBadgeBorder.CornerRadius = new CornerRadius(Math.Clamp(modeBadgeBox.Size * 0.36, 5, 12));
        var modeLayout = ComponentTypographyLayoutService.FitAdaptiveTextLayout(
            ModeTextBlock.Text,
            Math.Max(54, modeBadgeBox.Width),
            Math.Max(18, modeBadgeBox.Height),
            1,
            1,
            8,
            Math.Clamp(16 * scale, 8, 16),
            [ModeTextBlock.FontWeight],
            1.02);
        ModeTextBlock.FontSize = modeLayout.FontSize;
        ModeTextBlock.FontWeight = modeLayout.Weight;
        ModeTextBlock.MaxLines = 1;
        ModeTextBlock.TextWrapping = TextWrapping.NoWrap;
        ModeTextBlock.LineHeight = modeLayout.LineHeight;

        foreach (var block in new[] { SustainedReasonTextBlock, TimeReasonTextBlock, SegmentReasonTextBlock })
        {
            var layout = ComponentTypographyLayoutService.FitAdaptiveTextLayout(
                block.Text,
                Math.Max(84, contentWidth * 0.34),
                Math.Max(18, contentHeight * 0.14),
                1,
                _isUltraCompactMode ? 1 : 2,
                9,
                Math.Clamp(18 * scale, 9, 18),
                [block.FontWeight],
                1.05);
            block.FontSize = layout.FontSize;
            block.FontWeight = layout.Weight;
            block.MaxLines = layout.MaxLines;
            block.TextWrapping = layout.MaxLines > 1 ? TextWrapping.Wrap : TextWrapping.NoWrap;
            block.LineHeight = layout.LineHeight;
        }

        foreach (var block in new[] { SustainedMetricTextBlock, TimeMetricTextBlock, SegmentMetricTextBlock, TotalLossTextBlock, ScoreTextBlock })
        {
            var layout = ComponentTypographyLayoutService.FitAdaptiveTextLayout(
                block.Text,
                Math.Max(72, contentWidth * 0.22),
                Math.Max(16, contentHeight * 0.10),
                1,
                1,
                8,
                Math.Clamp(16 * scale, 8, 16),
                [block.FontWeight],
                1.02);
            block.FontSize = layout.FontSize;
            block.FontWeight = layout.Weight;
            block.MaxLines = 1;
            block.TextWrapping = TextWrapping.NoWrap;
            block.LineHeight = layout.LineHeight;
        }
    }

    private void ApplyTypographyByBackground(Color panelColor)
    {
        var samples = BuildPanelBackgroundSamples(panelColor);
        var primary = CreateAdaptiveBrush(samples, PrimaryColorCandidates, minContrast: 4.5);
        var secondary = CreateAdaptiveBrush(samples, SecondaryColorCandidates, minContrast: 4.5);

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

    private void ApplyModeBadgeColor(Color panelColor, Color baseColor)
    {
        var panelLuminance = RelativeLuminance(ToOpaqueAgainst(panelColor, DarkSubstrate));
        var badgeAlpha = panelLuminance > 0.58
            ? (byte)0xE2
            : panelLuminance > 0.46
                ? (byte)0xD8
                : (byte)0xC8;

        var badgeColor = Color.FromArgb(badgeAlpha, baseColor.R, baseColor.G, baseColor.B);
        var badgeComposite = ToOpaqueAgainst(badgeColor, ToOpaqueAgainst(panelColor, DarkSubstrate));

        ModeBadgeBorder.Background = new SolidColorBrush(badgeColor);
        ModeBadgeBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(0x96, 0xFF, 0xFF, 0xFF));
        ModeTextBlock.Foreground = CreateAdaptiveBrush(new[] { badgeComposite }, PrimaryColorCandidates, minContrast: 4.5);
    }

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

    private static IReadOnlyList<Color> BuildPanelBackgroundSamples(Color panelColor)
    {
        var opaqueOnDark = ToOpaqueAgainst(panelColor, DarkSubstrate);
        var opaqueOnLight = ToOpaqueAgainst(panelColor, LightSubstrate);

        return
        [
            opaqueOnDark,
            opaqueOnLight,
            ColorMath.Blend(opaqueOnDark, DarkSubstrate, 0.28),
            ColorMath.Blend(opaqueOnDark, Color.Parse("#FFFFFFFF"), 0.16),
            ColorMath.Blend(opaqueOnLight, Color.Parse("#FFFFFFFF"), 0.08),
            ColorMath.Blend(opaqueOnLight, DarkSubstrate, 0.18)
        ];
    }

    private static SolidColorBrush CreateAdaptiveBrush(
        IReadOnlyList<Color> backgroundSamples,
        IReadOnlyList<Color> colorCandidates,
        double minContrast)
    {
        if (colorCandidates.Count == 0)
        {
            return new SolidColorBrush(Color.Parse("#FFFFFFFF"));
        }

        for (var i = 0; i < colorCandidates.Count; i++)
        {
            var candidate = colorCandidates[i];
            if (MinContrastRatio(candidate, backgroundSamples) >= minContrast)
            {
                return new SolidColorBrush(candidate);
            }
        }

        var best = colorCandidates[0];
        var bestContrast = MinContrastRatio(best, backgroundSamples);
        for (var i = 1; i < colorCandidates.Count; i++)
        {
            var candidate = colorCandidates[i];
            var contrast = MinContrastRatio(candidate, backgroundSamples);
            if (contrast > bestContrast)
            {
                best = candidate;
                bestContrast = contrast;
            }
        }

        return new SolidColorBrush(best);
    }

    private static double MinContrastRatio(Color foreground, IReadOnlyList<Color> backgrounds)
    {
        if (backgrounds.Count == 0)
        {
            return 21;
        }

        var minimum = double.MaxValue;
        for (var i = 0; i < backgrounds.Count; i++)
        {
            var background = backgrounds[i];
            var visibleForeground = foreground.A >= 0xFF
                ? Color.FromArgb(0xFF, foreground.R, foreground.G, foreground.B)
                : ToOpaqueAgainst(foreground, background);

            var ratio = ColorMath.ContrastRatio(visibleForeground, background);
            if (ratio < minimum)
            {
                minimum = ratio;
            }
        }

        return minimum;
    }

    private static Color ToOpaqueAgainst(Color foreground, Color background)
    {
        if (foreground.A >= 0xFF)
        {
            return Color.FromArgb(0xFF, foreground.R, foreground.G, foreground.B);
        }

        var alpha = foreground.A / 255d;
        var red = (byte)Math.Round((foreground.R * alpha) + (background.R * (1 - alpha)));
        var green = (byte)Math.Round((foreground.G * alpha) + (background.G * (1 - alpha)));
        var blue = (byte)Math.Round((foreground.B * alpha) + (background.B * (1 - alpha)));
        return Color.FromArgb(0xFF, red, green, blue);
    }

    private static double RelativeLuminance(Color color)
    {
        static double ToLinear(byte channel)
        {
            var c = channel / 255d;
            return c <= 0.03928
                ? c / 12.92
                : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        var r = ToLinear(color.R);
        var g = ToLinear(color.G);
        var b = ToLinear(color.B);
        return 0.2126 * r + 0.7152 * g + 0.0722 * b;
    }

    private Color ResolvePanelBackgroundColor()
    {
        if (RootBorder.Background is ISolidColorBrush solidBackground)
        {
            return solidBackground.Color;
        }

        if (this.TryFindResource("AdaptiveGlassStrongBackgroundBrush", out var resource) &&
            resource is ISolidColorBrush solidBrush)
        {
            return solidBrush.Color;
        }

        return Color.Parse("#FF1E293B");
    }

    private void ReloadLanguageCode()
    {
        var snapshot = _settingsService.Load();
        _languageCode = _localizationService.NormalizeLanguageCode(snapshot.LanguageCode);
    }

    private void ApplyVariableFontFamily()
    {
        TitleTextBlock.FontFamily = MiSansVariableFontFamily;
        ModeTextBlock.FontFamily = MiSansVariableFontFamily;

        SustainedReasonTextBlock.FontFamily = MiSansVariableFontFamily;
        SustainedMetricTextBlock.FontFamily = MiSansVariableFontFamily;
        SustainedLossTextBlock.FontFamily = MiSansVariableFontFamily;

        TimeReasonTextBlock.FontFamily = MiSansVariableFontFamily;
        TimeMetricTextBlock.FontFamily = MiSansVariableFontFamily;
        TimeLossTextBlock.FontFamily = MiSansVariableFontFamily;

        SegmentReasonTextBlock.FontFamily = MiSansVariableFontFamily;
        SegmentMetricTextBlock.FontFamily = MiSansVariableFontFamily;
        SegmentLossTextBlock.FontFamily = MiSansVariableFontFamily;

        TotalLossTextBlock.FontFamily = MiSansVariableFontFamily;
        ScoreTextBlock.FontFamily = MiSansVariableFontFamily;
    }

    private void ApplyVariableWeights(double scale)
    {
        var weightProgress = Math.Clamp((scale - 0.52) / 1.5, 0, 1);
        var compactDelta = _isUltraCompactMode ? 40 : _isCompactMode ? 20 : 0;

        TitleTextBlock.FontWeight = ToVariableWeight(Lerp(560, 680, weightProgress));
        ModeTextBlock.FontWeight = ToVariableWeight(Lerp(560, 700, weightProgress));

        SustainedReasonTextBlock.FontWeight = ToVariableWeight(Lerp(560, 690, weightProgress));
        TimeReasonTextBlock.FontWeight = ToVariableWeight(Lerp(560, 690, weightProgress));
        SegmentReasonTextBlock.FontWeight = ToVariableWeight(Lerp(560, 690, weightProgress));

        SustainedMetricTextBlock.FontWeight = ToVariableWeight(Lerp(480, 600, weightProgress));
        TimeMetricTextBlock.FontWeight = ToVariableWeight(Lerp(480, 600, weightProgress));
        SegmentMetricTextBlock.FontWeight = ToVariableWeight(Lerp(480, 600, weightProgress));

        SustainedLossTextBlock.FontWeight = ToVariableWeight(Lerp(640 + compactDelta, 800, weightProgress));
        TimeLossTextBlock.FontWeight = ToVariableWeight(Lerp(640 + compactDelta, 800, weightProgress));
        SegmentLossTextBlock.FontWeight = ToVariableWeight(Lerp(640 + compactDelta, 800, weightProgress));

        TotalLossTextBlock.FontWeight = ToVariableWeight(Lerp(560, 700, weightProgress));
        ScoreTextBlock.FontWeight = ToVariableWeight(Lerp(560, 700, weightProgress));
    }

    private static double Lerp(double from, double to, double ratio)
    {
        ratio = Math.Clamp(ratio, 0, 1);
        return from + ((to - from) * ratio);
    }

    private static FontWeight ToVariableWeight(double weight)
    {
        return (FontWeight)(int)Math.Clamp(Math.Round(weight), 1, 1000);
    }

    private string L(string key, string fallback)
    {
        return _localizationService.GetString(_languageCode, key, fallback);
    }
}
