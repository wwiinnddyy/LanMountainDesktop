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

public partial class StudyNoiseCurveWidget : UserControl, IDesktopComponentWidget, IDesktopPageVisibilityAwareComponentWidget, IDisposable
{
    private const double NormalTextMinContrast = 4.5;
    private const double LargeTextMinContrast = 4.5;

    // Prefer cool-toned colors first (not plain white), then dark variants when background is bright.
    private static readonly Color[] ValueToneCandidates =
    {
        Color.Parse("#FFEAF5FF"),
        Color.Parse("#FFDCEEFF"),
        Color.Parse("#FFCEE6FA"),
        Color.Parse("#FF1A2D42"),
        Color.Parse("#FF233A54"),
        Color.Parse("#FFFFFFFF"),
        Color.Parse("#FF101C2A")
    };

    private static readonly Color[] AxisToneCandidates =
    {
        Color.Parse("#FFC7D9EC"),
        Color.Parse("#FFBAD0E8"),
        Color.Parse("#FFD9E8F6"),
        Color.Parse("#FF2C445F"),
        Color.Parse("#FF35516F"),
        Color.Parse("#FFEAF3FA"),
        Color.Parse("#FF1A2C40")
    };

    private static readonly Color[] StatusTextToneCandidates =
    {
        Color.Parse("#FFF5FAFF"),
        Color.Parse("#FFE6F1FB"),
        Color.Parse("#FF18283A"),
        Color.Parse("#FF122032"),
        Color.Parse("#FFFFFFFF"),
        Color.Parse("#FF111B29")
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
    private bool _isSubscribed;
    private bool _isDisposed;
    private bool _studyEnabled = true;
    private int _framesSinceCompaction;
    private IDisposable? _monitoringLease;

    private enum StatusVisualKind
    {
        Default = 0,
        Quiet = 1,
        Noisy = 2,
        Error = 3
    }

    public StudyNoiseCurveWidget()
    {
        InitializeComponent();

        _renderGate = new StudySnapshotRenderGate(CanRenderSnapshot, ApplySnapshot, AfterSnapshotRendered);
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
        SizeChanged += OnSizeChanged;
        ActualThemeVariantChanged += OnActualThemeVariantChanged;

        ReloadLanguageCode();
        ApplyCellSize(_currentCellSize);
        ApplyDefaultXAxisLabels();

        var panelColor = StudyPanelPalette.Resolve(this, RootBorder.Background);
        ApplyTypographyByBackground(panelColor);
        ApplyStatusBadgeStyle(StatusVisualKind.Default, panelColor);
    }

    public void ApplyCellSize(double cellSize)
    {
        _currentCellSize = Math.Max(1, cellSize);
        var scale = Math.Clamp(_currentCellSize / ComponentDesignMetrics.BaseCellSize, 0.78, 2.4);

        RootBorder.CornerRadius = ComponentChromeCornerRadiusHelper.ResolveMainRectangleRadius();
        RootBorder.Padding = new Thickness(
            Math.Clamp(14 * scale, 8, 22),
            Math.Clamp(10 * scale, 6, 16));

        StatusTextBlock.FontSize = Math.Clamp(16 * scale, 12, 30);
        RealtimeValueTextBlock.FontSize = Math.Clamp(18 * scale, 12, 34);

        StatusBadgeBorder.Padding = new Thickness(
            Math.Clamp(8 * scale, 4, 11),
            Math.Clamp(3 * scale, 2, 6));
        StatusBadgeBorder.CornerRadius = new CornerRadius(Math.Clamp(8 * scale, 5, 12));
        StatusBadgeBorder.BorderThickness = new Thickness(Math.Clamp(1 * scale, 0.8, 1.5));

        var axisFontSize = Math.Clamp(10 * scale, 9.5, 18);
        YTopTextBlock.FontSize = axisFontSize;
        YUpperTextBlock.FontSize = axisFontSize;
        YMiddleTextBlock.FontSize = axisFontSize;
        YLowerTextBlock.FontSize = axisFontSize;
        YBottomTextBlock.FontSize = axisFontSize;
        XLeftTextBlock.FontSize = axisFontSize;
        XCenterTextBlock.FontSize = axisFontSize;
        XRightTextBlock.FontSize = axisFontSize;
    }

    public void SetDesktopPageContext(bool isOnActivePage, bool isEditMode) =>
        StudyComponentLifecycle.ApplyPageContext(
            ref _isOnActivePage,
            isOnActivePage,
            isEditMode,
            UpdateMonitoringLeaseState,
            () => _renderGate.Queue(_studyAnalyticsService.GetSnapshot()));

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        StudyComponentLifecycle.Attach(
            ref _isAttached, ref _isSubscribed, _studyAnalyticsService, _renderGate,
            ReloadLanguageCode, UpdateMonitoringLeaseState,
            () => _renderGate.Queue(_studyAnalyticsService.GetSnapshot()));
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        StudyComponentLifecycle.Detach(
            ref _isAttached, ref _monitoringLease, ref _isSubscribed, _studyAnalyticsService, _renderGate);
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        ApplyCellSize(_currentCellSize);
        var panelColor = StudyPanelPalette.Resolve(this, RootBorder.Background);
        ApplyTypographyByBackground(panelColor);
    }

    private void OnActualThemeVariantChanged(object? sender, EventArgs e)
    {
        var panelColor = StudyPanelPalette.Resolve(this, RootBorder.Background);
        ApplyTypographyByBackground(panelColor);
        ApplyStatusBadgeStyle(StatusVisualKind.Default, panelColor);

        _renderGate.Queue(_studyAnalyticsService.GetSnapshot());
    }

    private bool CanRenderSnapshot()
    {
        return _isAttached && _isOnActivePage;
    }

    private void AfterSnapshotRendered()
    {
        _framesSinceCompaction++;
        if (_framesSinceCompaction >= 900)
        {
            ChartControl.CompactCaches();
            _framesSinceCompaction = 0;
        }
    }

    private void UpdateMonitoringLeaseState() =>
        StudyMonitoringLease.Sync(ref _monitoringLease, _monitoringLeaseCoordinator, _studyEnabled, _isAttached, _isOnActivePage);

    private void ApplySnapshot(StudyAnalyticsSnapshot snapshot)
    {
        var panelColor = StudyPanelPalette.Resolve(this, RootBorder.Background);
        ApplyTypographyByBackground(panelColor);

        if (!_studyEnabled)
        {
            StatusTextBlock.Text = L("study.widget.disabled_title", "自习功能未启用");
            RealtimeValueTextBlock.Text = L("study.widget.disabled_hint", "请在设置中开启");
            ApplyStatusBadgeStyle(StatusVisualKind.Default, panelColor);
            ChartControl.UpdateSeries([]);
            return;
        }

        var isSessionReport = snapshot.DataMode == StudyDataMode.SessionReport && snapshot.LastSessionReport is not null;
        if (isSessionReport && snapshot.LastSessionReport is not null)
        {
            StatusTextBlock.Text = L("study.score_overview.mode.session", "Session");
            ApplyStatusBadgeStyle(StatusVisualKind.Quiet, panelColor);

            var reportPoints = StudySessionReportProjection.BuildSyntheticRealtimePoints(snapshot.LastSessionReport, snapshot.Config);
            ChartControl.UpdateSeries(reportPoints);
            UpdateXAxisLabels(reportPoints);

            if (StudySessionReportProjection.TryAggregate(snapshot.LastSessionReport, snapshot.Config, out var aggregate))
            {
                RealtimeValueTextBlock.Text = string.Format(
                    CultureInfo.InvariantCulture,
                    L("study.noise_curve.value_format", "{0:F1} dB"),
                    aggregate.AverageDisplayDb);
            }
            else
            {
                RealtimeValueTextBlock.Text = L("study.environment.value.unavailable", "--");
            }

            return;
        }

        var statusKind = ResolveStatusVisualKind(snapshot);
        StatusTextBlock.Text = StudyNoiseStatusText.Describe(snapshot, L);
        ApplyStatusBadgeStyle(statusKind, panelColor);

        if (snapshot.LatestRealtimePoint is { } latestPoint)
        {
            RealtimeValueTextBlock.Text = string.Format(
                CultureInfo.InvariantCulture,
                L("study.noise_curve.value_format", "{0:F1} dB"),
                latestPoint.DisplayDb);
        }
        else
        {
            RealtimeValueTextBlock.Text = L("study.environment.value.unavailable", "--");
        }

        ChartControl.UpdateSeries(snapshot.RealtimeBuffer);
        UpdateXAxisLabels(snapshot.RealtimeBuffer);
    }

    private void ApplyTypographyByBackground(Color panelColor)
    {
        var samples = StudyPanelPalette.BuildSamples(panelColor);
        var valueBrush = AdaptiveBrushFactory.Create(samples, ValueToneCandidates, LargeTextMinContrast);
        var axisBrush = AdaptiveBrushFactory.Create(samples, AxisToneCandidates, NormalTextMinContrast);

        RealtimeValueTextBlock.Foreground = valueBrush;
        YTopTextBlock.Foreground = axisBrush;
        YUpperTextBlock.Foreground = axisBrush;
        YMiddleTextBlock.Foreground = axisBrush;
        YLowerTextBlock.Foreground = axisBrush;
        YBottomTextBlock.Foreground = axisBrush;
        XLeftTextBlock.Foreground = axisBrush;
        XCenterTextBlock.Foreground = axisBrush;
        XRightTextBlock.Foreground = axisBrush;
    }

    private void ApplyStatusBadgeStyle(StatusVisualKind kind, Color panelColor)
    {
        var badgeBaseColor = kind switch
        {
            StatusVisualKind.Quiet => StudyPanelPalette.SuccessBadge,
            StatusVisualKind.Noisy => Color.Parse("#FF805018"),
            StatusVisualKind.Error => Color.Parse("#FF8D2A3A"),
            _ => Color.Parse("#FF213547")
        };

        var badgeColor = StudyPanelPalette.ResolveBadgeColor(panelColor, badgeBaseColor, StudyPanelPalette.StatusBadgeTiers);
        StatusBadgeBorder.Background = new SolidColorBrush(badgeColor);
        StatusBadgeBorder.BorderBrush = StudyPanelPalette.BadgeBorderBrush;
        StatusTextBlock.Foreground = StudyPanelPalette.ResolveBadgeForeground(panelColor, badgeColor, StatusTextToneCandidates, NormalTextMinContrast);
    }

    private static StatusVisualKind ResolveStatusVisualKind(StudyAnalyticsSnapshot snapshot)
    {
        if (snapshot.State == StudyAnalyticsRuntimeState.Unsupported ||
            snapshot.State == StudyAnalyticsRuntimeState.Error ||
            snapshot.StreamStatus == NoiseStreamStatus.Error)
        {
            return StatusVisualKind.Error;
        }

        if (snapshot.StreamStatus == NoiseStreamStatus.Noisy)
        {
            return StatusVisualKind.Noisy;
        }

        if (snapshot.State == StudyAnalyticsRuntimeState.Running && snapshot.StreamStatus == NoiseStreamStatus.Quiet)
        {
            return StatusVisualKind.Quiet;
        }

        return StatusVisualKind.Default;
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
        XRightTextBlock.Text = L("study.noise_curve.axis.now", "Now");
    }

    private void ApplyDefaultXAxisLabels()
    {
        XLeftTextBlock.Text = "-12s";
        XCenterTextBlock.Text = "-6s";
        XRightTextBlock.Text = L("study.noise_curve.axis.now", "Now");
    }

    private void ReloadLanguageCode()
    {
        StudyComponentSettings.Reload(
            ref _languageCode, ref _studyEnabled, _settingsService, _localizationService);
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

        _renderGate.Dispose();
        AttachedToVisualTree -= OnAttachedToVisualTree;
        DetachedFromVisualTree -= OnDetachedFromVisualTree;
        SizeChanged -= OnSizeChanged;
        ActualThemeVariantChanged -= OnActualThemeVariantChanged;

        StudySnapshotSubscription.Unsubscribe(ref _isSubscribed, _studyAnalyticsService, _renderGate.HandleSnapshotUpdated);

        StudyMonitoringLease.Release(ref _monitoringLease);
    }
}
