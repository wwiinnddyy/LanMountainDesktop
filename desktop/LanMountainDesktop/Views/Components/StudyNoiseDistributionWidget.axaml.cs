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

public partial class StudyNoiseDistributionWidget : UserControl, IDesktopComponentWidget, IDesktopPageVisibilityAwareComponentWidget, IDisposable
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

    private double _currentCellSize = ComponentDesignMetrics.BaseCellSize;
    private string _languageCode = LocalizationService.DefaultLanguageCode;
    private bool _isAttached;
    private bool _isOnActivePage = true;
    private bool _isDisposed;
    private bool _isCompactMode;
    private bool _isSubscribed;
    private bool _isUltraCompactMode;
    private bool _studyEnabled = true;
    private IDisposable? _monitoringLease;

    private readonly record struct DistributionStats(
        NoiseDistributionLevel LatestLevel,
        NoiseDistributionLevel DominantLevel,
        TimeSpan Duration,
        int QuietCount,
        int NormalCount,
        int NoisyCount,
        int ExtremeCount);

    public StudyNoiseDistributionWidget()
    {
        InitializeComponent();

        _renderGate = new StudySnapshotRenderGate(CanRenderSnapshot, ApplySnapshot);
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
        SizeChanged += OnSizeChanged;
        ActualThemeVariantChanged += OnActualThemeVariantChanged;
        ReloadLanguageCode();
        ApplyCellSize(_currentCellSize);
        ApplyDefaultXAxisLabels();
        ApplyLocalizedAxisLabels();
        _renderGate.Queue(_studyAnalyticsService.GetSnapshot());
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
            _renderGate.Queue(_studyAnalyticsService.GetSnapshot());
        }
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _isAttached = true;
        ReloadLanguageCode();

        StudySnapshotSubscription.Subscribe(ref _isSubscribed, _studyAnalyticsService, _renderGate.HandleSnapshotUpdated);

        UpdateMonitoringLeaseState();
        _renderGate.Queue(_studyAnalyticsService.GetSnapshot());
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
        _renderGate.Queue(_studyAnalyticsService.GetSnapshot());
    }

    private void UpdateMonitoringLeaseState() =>
        StudyMonitoringLease.Sync(ref _monitoringLease, _monitoringLeaseCoordinator, _studyEnabled, _isAttached, _isOnActivePage);

    private bool CanRenderSnapshot()
    {
        return _isAttached && _isOnActivePage;
    }

    private void ApplySnapshot(StudyAnalyticsSnapshot snapshot)
    {
        var panelColor = StudyPanelPalette.Resolve(this, RootBorder.Background);
        ApplyTypographyByBackground(panelColor);

        TitleTextBlock.Text = L("study.noise_distribution.title", "Noise Level Distribution");
        ApplyLocalizedAxisLabels();

        if (!_studyEnabled)
        {
            ModeTextBlock.Text = L("study.widget.disabled_hint", "请在设置中开启");
            ApplyModeBadgeColor(panelColor, StudyPanelPalette.DisabledBadge);
            ChartControl.UpdateSeries([], 45);
            SummaryTextBlock.Text = "--";
            return;
        }

        var isSessionRunning = snapshot.Session.State == StudySessionRuntimeState.Running;
        var isSessionReport = snapshot.DataMode == StudyDataMode.SessionReport && snapshot.LastSessionReport is not null;
        var isSessionView = isSessionRunning || isSessionReport;
        ModeTextBlock.Text = isSessionView
            ? L("study.noise_distribution.mode.session", "Session")
            : L("study.noise_distribution.mode.realtime", "Realtime");
        ApplyModeBadgeColor(panelColor, isSessionView ? StudyPanelPalette.SuccessBadge : StudyPanelPalette.RealtimeBadge);

        var points = isSessionReport && snapshot.LastSessionReport is not null
            ? StudySessionReportProjection.BuildSyntheticRealtimePoints(snapshot.LastSessionReport, snapshot.Config)
            : snapshot.RealtimeBuffer;

        ChartControl.UpdateSeries(points, snapshot.Config.BaselineDb, isSessionReport);
        UpdateXAxisLabels(points);

        var stats = ComputeDistributionStats(points, snapshot.Config.BaselineDb);
        if (stats is null)
        {
            SummaryTextBlock.Text = string.Format(
                CultureInfo.InvariantCulture,
                L("study.noise_distribution.summary.latest_format", "Latest: {0}"),
                L("study.environment.value.unavailable", "--"));
            return;
        }

        var distribution = stats.Value;
        var dominant = ResolveLevelText(distribution.DominantLevel);
        var latest = ResolveLevelText(distribution.LatestLevel);

        SummaryTextBlock.Text = _isUltraCompactMode
            ? string.Format(
                CultureInfo.InvariantCulture,
                L("study.noise_distribution.summary.compact_format", "Main {0} · New {1}"),
                dominant,
                latest)
            : string.Format(
                CultureInfo.InvariantCulture,
                "{0} · {1}",
                string.Format(CultureInfo.InvariantCulture, L("study.noise_distribution.summary.mainly_format", "Mainly: {0}"), dominant),
                string.Format(CultureInfo.InvariantCulture, L("study.noise_distribution.summary.latest_format", "Latest: {0}"), latest));
    }

    private static DistributionStats? ComputeDistributionStats(IReadOnlyList<NoiseRealtimePoint> points, double baselineDb)
    {
        if (points.Count < 2)
        {
            return null;
        }

        var start = points[0].Timestamp;
        var end = points[^1].Timestamp;
        var duration = end - start;
        if (duration.TotalMilliseconds <= 300)
        {
            return null;
        }

        var quiet = 0;
        var normal = 0;
        var noisy = 0;
        var extreme = 0;

        for (var i = 0; i < points.Count; i++)
        {
            switch (ResolveLevel(points[i].DisplayDb, baselineDb))
            {
                case NoiseDistributionLevel.Quiet:
                    quiet++;
                    break;
                case NoiseDistributionLevel.Normal:
                    normal++;
                    break;
                case NoiseDistributionLevel.Noisy:
                    noisy++;
                    break;
                case NoiseDistributionLevel.Extreme:
                    extreme++;
                    break;
            }
        }

        var dominantLevel = NoiseDistributionLevel.Quiet;
        var dominantCount = quiet;
        if (normal > dominantCount)
        {
            dominantLevel = NoiseDistributionLevel.Normal;
            dominantCount = normal;
        }

        if (noisy > dominantCount)
        {
            dominantLevel = NoiseDistributionLevel.Noisy;
            dominantCount = noisy;
        }

        if (extreme > dominantCount)
        {
            dominantLevel = NoiseDistributionLevel.Extreme;
        }

        var latestLevel = ResolveLevel(points[^1].DisplayDb, baselineDb);
        return new DistributionStats(
            LatestLevel: latestLevel,
            DominantLevel: dominantLevel,
            Duration: duration,
            QuietCount: quiet,
            NormalCount: normal,
            NoisyCount: noisy,
            ExtremeCount: extreme);
    }

    private static NoiseDistributionLevel ResolveLevel(double displayDb, double baselineDb)
    {
        var quietUpper = baselineDb;
        var normalUpper = baselineDb + 10d;
        var noisyUpper = baselineDb + 20d;

        if (displayDb < quietUpper)
        {
            return NoiseDistributionLevel.Quiet;
        }

        if (displayDb < normalUpper)
        {
            return NoiseDistributionLevel.Normal;
        }

        if (displayDb < noisyUpper)
        {
            return NoiseDistributionLevel.Noisy;
        }

        return NoiseDistributionLevel.Extreme;
    }

    private void UpdateAdaptiveLayout()
    {
        var cellScale = Math.Clamp(_currentCellSize / ComponentDesignMetrics.BaseCellSize, 0.76, 2.4);
        var widthScale = Bounds.Width > 1 ? Bounds.Width / 520d : cellScale;
        var heightScale = Bounds.Height > 1 ? Bounds.Height / 240d : cellScale;
        var boundsScale = Math.Clamp(Math.Min(widthScale, heightScale), 0.52, 2.3);
        var scale = Math.Clamp(Math.Min(cellScale, boundsScale * 1.06), 0.52, 2.3);

        _isCompactMode = scale < 0.92 || (Bounds.Width > 1 && Bounds.Width < 360) || (Bounds.Height > 1 && Bounds.Height < 180);
        _isUltraCompactMode = scale < 0.74 || (Bounds.Width > 1 && Bounds.Width < 300) || (Bounds.Height > 1 && Bounds.Height < 142);

        var compactMultiplier = _isUltraCompactMode ? 0.76 : _isCompactMode ? 0.88 : 1.0;
        RootBorder.CornerRadius = ComponentChromeCornerRadiusHelper.ResolveMainRectangleRadius();
        RootBorder.Padding = new Thickness(
            Math.Clamp(12 * scale * compactMultiplier, 6, 18),
            Math.Clamp(9 * scale * compactMultiplier, 5, 16));

        ContentRootGrid.RowSpacing = _isUltraCompactMode
            ? Math.Clamp(4 * scale, 2, 5)
            : _isCompactMode
                ? Math.Clamp(6 * scale, 3, 8)
                : Math.Clamp(8 * scale, 4, 11);
        HeaderGrid.ColumnSpacing = _isUltraCompactMode
            ? Math.Clamp(5 * scale, 2, 7)
            : Math.Clamp(8 * scale, 4, 10);

        TitleTextBlock.FontSize = Math.Clamp(13 * scale, 9, 22);
        SummaryTextBlock.FontSize = Math.Clamp(12 * scale, 8, 20);
        ModeTextBlock.FontSize = Math.Clamp(11 * scale, 8, 18);

        YExtremeTextBlock.FontSize = Math.Clamp(10 * scale, 8, 16);
        YNoisyTextBlock.FontSize = Math.Clamp(10 * scale, 8, 16);
        YNormalTextBlock.FontSize = Math.Clamp(10 * scale, 8, 16);
        YQuietTextBlock.FontSize = Math.Clamp(10 * scale, 8, 16);
        XLeftTextBlock.FontSize = Math.Clamp(10 * scale, 8, 16);
        XCenterTextBlock.FontSize = Math.Clamp(10 * scale, 8, 16);
        XRightTextBlock.FontSize = Math.Clamp(10 * scale, 8, 16);

        ModeBadgeBorder.Padding = new Thickness(
            Math.Clamp(8 * scale * compactMultiplier, 4, 12),
            Math.Clamp(3 * scale * compactMultiplier, 1.6, 6));
        ModeBadgeBorder.CornerRadius = new CornerRadius(Math.Clamp(8 * scale, 4, 12));

        TitleTextBlock.IsVisible = !_isUltraCompactMode;
        SummaryTextBlock.IsVisible = true;

        ApplyVariableWeights(scale);
    }

    private void ApplyTypographyByBackground(Color panelColor)
    {
        var samples = StudyPanelPalette.BuildSamples(panelColor);
        var primary = AdaptiveBrushFactory.Create(samples, ValueColorCandidates, minContrast: 4.5);
        var secondary = AdaptiveBrushFactory.Create(samples, SecondaryColorCandidates, minContrast: 4.5);

        TitleTextBlock.Foreground = secondary;
        YExtremeTextBlock.Foreground = secondary;
        YNoisyTextBlock.Foreground = secondary;
        YNormalTextBlock.Foreground = secondary;
        YQuietTextBlock.Foreground = secondary;
        XLeftTextBlock.Foreground = secondary;
        XCenterTextBlock.Foreground = secondary;
        XRightTextBlock.Foreground = secondary;

        SummaryTextBlock.Foreground = primary;
    }

    private void ApplyModeBadgeColor(Color panelColor, Color baseColor)
    {
        var badgeColor = StudyPanelPalette.ResolveBadgeColor(panelColor, baseColor);
        ModeBadgeBorder.Background = new SolidColorBrush(badgeColor);
        ModeBadgeBorder.BorderBrush = StudyPanelPalette.BadgeBorderBrush;
        ModeTextBlock.Foreground = StudyPanelPalette.ResolveBadgeForeground(panelColor, badgeColor, ValueColorCandidates);
    }

    private void UpdateXAxisLabels(IReadOnlyList<NoiseRealtimePoint> buffer)
    {
        if (buffer.Count < 2)
        {
            ApplyDefaultXAxisLabels();
            return;
        }

        var duration = (buffer[^1].Timestamp - buffer[0].Timestamp).TotalSeconds;
        if (double.IsNaN(duration) || double.IsInfinity(duration) || duration <= 1)
        {
            duration = 12;
        }

        duration = Math.Clamp(duration, 4, 60);
        var leftSeconds = Math.Round(duration, MidpointRounding.AwayFromZero);
        var centerSeconds = Math.Round(duration / 2d, MidpointRounding.AwayFromZero);
        XLeftTextBlock.Text = $"-{leftSeconds:0}s";
        XCenterTextBlock.Text = $"-{centerSeconds:0}s";
        XRightTextBlock.Text = L("study.noise_distribution.axis.now", "Now");
    }

    private void ApplyDefaultXAxisLabels()
    {
        XLeftTextBlock.Text = "-12s";
        XCenterTextBlock.Text = "-6s";
        XRightTextBlock.Text = L("study.noise_distribution.axis.now", "Now");
    }

    private void ApplyLocalizedAxisLabels()
    {
        YExtremeTextBlock.Text = L("study.noise_distribution.axis.extreme", "Extreme");
        YNoisyTextBlock.Text = L("study.noise_distribution.axis.noisy", "Noisy");
        YNormalTextBlock.Text = L("study.noise_distribution.axis.normal", "Normal");
        YQuietTextBlock.Text = L("study.noise_distribution.axis.quiet", "Quiet");
    }

    private string ResolveLevelText(NoiseDistributionLevel level)
    {
        return level switch
        {
            NoiseDistributionLevel.Quiet => L("study.noise_distribution.level.quiet", "Quiet"),
            NoiseDistributionLevel.Normal => L("study.noise_distribution.level.normal", "Normal"),
            NoiseDistributionLevel.Noisy => L("study.noise_distribution.level.noisy", "Noisy"),
            NoiseDistributionLevel.Extreme => L("study.noise_distribution.level.extreme", "Extreme"),
            _ => L("study.noise_distribution.level.normal", "Normal")
        };
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
        SummaryTextBlock.FontWeight = ComponentTypography.ToVariableWeight(ComponentTypography.LerpClamped(550 + compactDelta, 700, weightProgress));
        ModeTextBlock.FontWeight = ComponentTypography.ToVariableWeight(ComponentTypography.LerpClamped(560, 700, weightProgress));
        YExtremeTextBlock.FontWeight = ComponentTypography.ToVariableWeight(ComponentTypography.LerpClamped(500, 620, weightProgress));
        YNoisyTextBlock.FontWeight = ComponentTypography.ToVariableWeight(ComponentTypography.LerpClamped(500, 620, weightProgress));
        YNormalTextBlock.FontWeight = ComponentTypography.ToVariableWeight(ComponentTypography.LerpClamped(500, 620, weightProgress));
        YQuietTextBlock.FontWeight = ComponentTypography.ToVariableWeight(ComponentTypography.LerpClamped(500, 620, weightProgress));
        XLeftTextBlock.FontWeight = ComponentTypography.ToVariableWeight(ComponentTypography.LerpClamped(500, 620, weightProgress));
        XCenterTextBlock.FontWeight = ComponentTypography.ToVariableWeight(ComponentTypography.LerpClamped(500, 620, weightProgress));
        XRightTextBlock.FontWeight = ComponentTypography.ToVariableWeight(ComponentTypography.LerpClamped(500, 620, weightProgress));
    }

    private string L(string key, string fallback)
    {
        return _localizationService.GetString(_languageCode, key, fallback);
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;

        AttachedToVisualTree -= OnAttachedToVisualTree;
        DetachedFromVisualTree -= OnDetachedFromVisualTree;
        SizeChanged -= OnSizeChanged;
        ActualThemeVariantChanged -= OnActualThemeVariantChanged;
        _renderGate.Dispose();

        StudySnapshotSubscription.Unsubscribe(ref _isSubscribed, _studyAnalyticsService, _renderGate.HandleSnapshotUpdated);

        StudyMonitoringLease.Release(ref _monitoringLease);
    }
}
