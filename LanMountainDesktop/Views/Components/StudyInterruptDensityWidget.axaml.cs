using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using LanMountainDesktop.DesktopComponents.Runtime;
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

    private static readonly FontFamily MiSansVariableFontFamily = new("MiSans VF, avares://LanMountainDesktop/Assets/Fonts#MiSans");
    private static readonly Color DarkSubstrate = Color.Parse("#FF0B1220");
    private static readonly Color LightSubstrate = Color.Parse("#FFF1F5FA");

    private readonly IStudyAnalyticsService _studyAnalyticsService = StudyAnalyticsServiceFactory.CreateDefault();
    private readonly StudyAnalyticsMonitoringLeaseCoordinator _monitoringLeaseCoordinator = StudyAnalyticsMonitoringLeaseCoordinatorFactory.CreateDefault();
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
    private IDisposable? _monitoringLease;

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
        UpdateMonitoringLeaseState();
        UpdateTimerState();
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _isAttached = true;
        ReloadLanguageCode();
        UpdateMonitoringLeaseState();
        UpdateTimerState();
        RefreshVisual();
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _isAttached = false;
        _monitoringLease?.Dispose();
        _monitoringLease = null;
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

    private void UpdateMonitoringLeaseState()
    {
        var shouldMonitor = _isAttached && _isOnActivePage;
        if (shouldMonitor)
        {
            _monitoringLease ??= _monitoringLeaseCoordinator.AcquireLease();
            return;
        }

        _monitoringLease?.Dispose();
        _monitoringLease = null;
    }

    private void RefreshVisual()
    {
        var snapshot = _studyAnalyticsService.GetSnapshot();
        var panelColor = ResolvePanelBackgroundColor();
        ApplyTypographyByBackground(panelColor);
        ApplyLocalizedLabels();

        var isSessionRunning = snapshot.Session.State == StudySessionRuntimeState.Running;
        var isSessionReport = snapshot.DataMode == StudyDataMode.SessionReport && snapshot.LastSessionReport is not null;
        var isSessionView = isSessionRunning || isSessionReport;
        ModeTextBlock.Text = isSessionView
            ? L("study.interrupt_density.mode.session", "Session")
            : L("study.interrupt_density.mode.realtime", "Realtime");
        ApplyModeBadgeColor(panelColor, isSessionView ? Color.Parse("#FF0F6B49") : Color.Parse("#FF2F5DA8"));

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
        var cellScale = Math.Clamp(_currentCellSize / 48d, 0.76, 2.4);
        var widthScale = Bounds.Width > 1 ? Bounds.Width / 420d : cellScale;
        var heightScale = Bounds.Height > 1 ? Bounds.Height / 220d : cellScale;
        var boundsScale = Math.Clamp(Math.Min(widthScale, heightScale), 0.52, 2.2);
        var scale = Math.Clamp(Math.Min(cellScale, boundsScale * 1.08), 0.52, 2.2);

        _isCompactMode = scale < 0.92 || (Bounds.Width > 1 && Bounds.Width < 350) || (Bounds.Height > 1 && Bounds.Height < 170);
        _isUltraCompactMode = scale < 0.72 || (Bounds.Width > 1 && Bounds.Width < 295) || (Bounds.Height > 1 && Bounds.Height < 130);

        var compactMultiplier = _isUltraCompactMode ? 0.76 : _isCompactMode ? 0.88 : 1.0;
        RootBorder.CornerRadius = ComponentChromeCornerRadiusHelper.Scale(_currentCellSize * 0.46, 12, 34);
        RootBorder.Padding = new Thickness(
            ComponentChromeCornerRadiusHelper.SafeValue(12 * scale * compactMultiplier, 6, 18),
            ComponentChromeCornerRadiusHelper.SafeValue(9 * scale * compactMultiplier, 5, 16));

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
            ComponentChromeCornerRadiusHelper.SafeValue(10 * scale * compactMultiplier, 5, 14),
            ComponentChromeCornerRadiusHelper.SafeValue(6 * scale * compactMultiplier, 3, 9));
        CountCardBorder.Padding = cardPadding;
        DurationCardBorder.Padding = cardPadding;

        TitleTextBlock.IsVisible = !_isUltraCompactMode;
        ThresholdTextBlock.IsVisible = !_isUltraCompactMode;
        DensityUnitTextBlock.IsVisible = !_isUltraCompactMode;
        CountLabelTextBlock.IsVisible = !_isUltraCompactMode;
        DurationLabelTextBlock.IsVisible = !_isUltraCompactMode;

        ApplyVariableWeights(scale);
        ApplyLocalizedLabels();

        var contentWidth = Math.Max(120, (Bounds.Width > 1 ? Bounds.Width : _currentCellSize * 8) - RootBorder.Padding.Left - RootBorder.Padding.Right);
        var contentHeight = Math.Max(78, (Bounds.Height > 1 ? Bounds.Height : _currentCellSize * 3) - RootBorder.Padding.Top - RootBorder.Padding.Bottom);

        var titleLayout = ComponentTypographyLayoutService.FitAdaptiveTextLayout(
            TitleTextBlock.Text,
            Math.Max(120, contentWidth * 0.38),
            Math.Max(18, contentHeight * 0.18),
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
            Math.Max(64, contentWidth * 0.22),
            Math.Max(20, contentHeight * 0.14),
            preferredSizeScale: 0.46d,
            minSize: 18,
            maxSize: 42,
            insetScale: 0.18d);
        ModeBadgeBorder.Padding = modeBadgeBox.Padding;
        ModeBadgeBorder.CornerRadius = new CornerRadius(Math.Clamp(modeBadgeBox.Size * 0.36, 4, 12));
        var modeLayout = ComponentTypographyLayoutService.FitAdaptiveTextLayout(
            ModeTextBlock.Text,
            Math.Max(52, modeBadgeBox.Width),
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

        foreach (var block in new[] { DensityValueTextBlock, CountValueTextBlock, DurationValueTextBlock })
        {
            var minFont = block == DensityValueTextBlock ? 18 : 10;
            var maxFont = block == DensityValueTextBlock ? Math.Clamp(94 * scale, 18, 94) : Math.Clamp(36 * scale, 10, 36);
            var maxWidth = block == DensityValueTextBlock ? Math.Max(86, contentWidth * 0.24) : Math.Max(64, contentWidth * 0.18);
            var maxHeight = block == DensityValueTextBlock ? Math.Max(24, contentHeight * 0.26) : Math.Max(18, contentHeight * 0.18);
            var layout = ComponentTypographyLayoutService.FitAdaptiveTextLayout(
                block.Text,
                maxWidth,
                maxHeight,
                1,
                1,
                minFont,
                maxFont,
                [block.FontWeight],
                1.02);
            block.FontSize = layout.FontSize;
            block.FontWeight = layout.Weight;
            block.MaxLines = 1;
            block.TextWrapping = TextWrapping.NoWrap;
            block.LineHeight = layout.LineHeight;
        }

        foreach (var block in new[] { DensityUnitTextBlock, DensityLevelTextBlock, CountLabelTextBlock, DurationLabelTextBlock, ThresholdTextBlock })
        {
            var layout = ComponentTypographyLayoutService.FitAdaptiveTextLayout(
                block.Text,
                Math.Max(64, contentWidth * 0.18),
                Math.Max(16, contentHeight * 0.14),
                1,
                1,
                8,
                Math.Clamp(18 * scale, 8, 18),
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
        DensityUnitTextBlock.Foreground = secondary;
        CountLabelTextBlock.Foreground = secondary;
        DurationLabelTextBlock.Foreground = secondary;
        ThresholdTextBlock.Foreground = secondary;

        DensityValueTextBlock.Foreground = primary;
        DensityLevelTextBlock.Foreground = primary;
        CountValueTextBlock.Foreground = primary;
        DurationValueTextBlock.Foreground = primary;
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
        DensityValueTextBlock.FontFamily = MiSansVariableFontFamily;
        DensityUnitTextBlock.FontFamily = MiSansVariableFontFamily;
        DensityLevelTextBlock.FontFamily = MiSansVariableFontFamily;
        CountLabelTextBlock.FontFamily = MiSansVariableFontFamily;
        CountValueTextBlock.FontFamily = MiSansVariableFontFamily;
        DurationLabelTextBlock.FontFamily = MiSansVariableFontFamily;
        DurationValueTextBlock.FontFamily = MiSansVariableFontFamily;
        ThresholdTextBlock.FontFamily = MiSansVariableFontFamily;
    }

    private void ApplyVariableWeights(double scale)
    {
        var weightProgress = Math.Clamp((scale - 0.52) / 1.5, 0, 1);
        var compactDelta = _isUltraCompactMode ? 40 : _isCompactMode ? 20 : 0;

        TitleTextBlock.FontWeight = ToVariableWeight(Lerp(560, 680, weightProgress));
        ModeTextBlock.FontWeight = ToVariableWeight(Lerp(560, 700, weightProgress));
        DensityValueTextBlock.FontWeight = ToVariableWeight(Lerp(660 + compactDelta, 820, weightProgress));
        DensityUnitTextBlock.FontWeight = ToVariableWeight(Lerp(520, 640, weightProgress));
        DensityLevelTextBlock.FontWeight = ToVariableWeight(Lerp(560, 700, weightProgress));
        CountLabelTextBlock.FontWeight = ToVariableWeight(Lerp(520, 620, weightProgress));
        CountValueTextBlock.FontWeight = ToVariableWeight(Lerp(620 + compactDelta, 780, weightProgress));
        DurationLabelTextBlock.FontWeight = ToVariableWeight(Lerp(520, 620, weightProgress));
        DurationValueTextBlock.FontWeight = ToVariableWeight(Lerp(620 + compactDelta, 760, weightProgress));
        ThresholdTextBlock.FontWeight = ToVariableWeight(Lerp(500, 620, weightProgress));
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

    private static IReadOnlyList<Color> BuildPanelBackgroundSamples(Color panelColor)
    {
        var opaqueOnDark = ToOpaqueAgainst(panelColor, DarkSubstrate);
        var opaqueOnLight = ToOpaqueAgainst(panelColor, LightSubstrate);

        return new[]
        {
            opaqueOnDark,
            opaqueOnLight,
            ColorMath.Blend(opaqueOnDark, DarkSubstrate, 0.28),
            ColorMath.Blend(opaqueOnDark, Color.Parse("#FFFFFFFF"), 0.16),
            ColorMath.Blend(opaqueOnLight, Color.Parse("#FFFFFFFF"), 0.08),
            ColorMath.Blend(opaqueOnLight, DarkSubstrate, 0.18)
        };
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

    private string L(string key, string fallback)
    {
        return _localizationService.GetString(_languageCode, key, fallback);
    }
}
