using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using LanMontainDesktop.Models;
using LanMontainDesktop.Services;

namespace LanMontainDesktop.Views.Components;

public partial class StudyNoiseCurveWidget : UserControl, IDesktopComponentWidget, IDesktopPageVisibilityAwareComponentWidget
{
    private readonly object _snapshotSync = new();
    private readonly IStudyAnalyticsService _studyAnalyticsService = StudyAnalyticsServiceFactory.CreateDefault();
    private readonly AppSettingsService _settingsService = new();
    private readonly LocalizationService _localizationService = new();
    private readonly DispatcherTimer _renderTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(33)
    };

    private StudyAnalyticsSnapshot? _pendingSnapshot;
    private bool _hasPendingSnapshot;
    private double _currentCellSize = 48;
    private string _languageCode = "zh-CN";
    private bool _isAttached;
    private bool _isOnActivePage = true;
    private bool _isSubscribed;
    private int _framesSinceCompaction;

    public StudyNoiseCurveWidget()
    {
        InitializeComponent();

        _renderTimer.Tick += OnRenderTimerTick;
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
        SizeChanged += OnSizeChanged;

        ReloadLanguageCode();
        ApplyCellSize(_currentCellSize);
        ApplyDefaultXAxisLabels();
    }

    public void ApplyCellSize(double cellSize)
    {
        _currentCellSize = Math.Max(1, cellSize);
        var scale = Math.Clamp(_currentCellSize / 48d, 0.78, 2.4);

        RootBorder.CornerRadius = new CornerRadius(Math.Clamp(_currentCellSize * 0.44, 14, 42));
        RootBorder.Padding = new Thickness(
            Math.Clamp(14 * scale, 8, 22),
            Math.Clamp(10 * scale, 6, 16));

        StatusTextBlock.FontSize = Math.Clamp(16 * scale, 11, 30);
        RealtimeValueTextBlock.FontSize = Math.Clamp(18 * scale, 11, 34);

        var axisFontSize = Math.Clamp(10 * scale, 8.5, 18);
        YTopTextBlock.FontSize = axisFontSize;
        YUpperTextBlock.FontSize = axisFontSize;
        YMiddleTextBlock.FontSize = axisFontSize;
        YLowerTextBlock.FontSize = axisFontSize;
        YBottomTextBlock.FontSize = axisFontSize;
        XLeftTextBlock.FontSize = axisFontSize;
        XCenterTextBlock.FontSize = axisFontSize;
        XRightTextBlock.FontSize = axisFontSize;
    }

    public void SetDesktopPageContext(bool isOnActivePage, bool isEditMode)
    {
        _ = isEditMode;
        _isOnActivePage = isOnActivePage;
        UpdateRenderLoopState();
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _isAttached = true;
        ReloadLanguageCode();

        if (!_isSubscribed)
        {
            _studyAnalyticsService.SnapshotUpdated += OnStudySnapshotUpdated;
            _isSubscribed = true;
        }

        _ = _studyAnalyticsService.StartOrResumeMonitoring();

        lock (_snapshotSync)
        {
            _pendingSnapshot = _studyAnalyticsService.GetSnapshot();
            _hasPendingSnapshot = true;
        }

        UpdateRenderLoopState();
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _isAttached = false;
        _renderTimer.Stop();

        if (_isSubscribed)
        {
            _studyAnalyticsService.SnapshotUpdated -= OnStudySnapshotUpdated;
            _isSubscribed = false;
        }
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        ApplyCellSize(_currentCellSize);
    }

    private void OnStudySnapshotUpdated(object? sender, StudyAnalyticsSnapshotChangedEventArgs e)
    {
        lock (_snapshotSync)
        {
            _pendingSnapshot = e.Snapshot;
            _hasPendingSnapshot = true;
        }
    }

    private void OnRenderTimerTick(object? sender, EventArgs e)
    {
        StudyAnalyticsSnapshot? snapshot = null;
        lock (_snapshotSync)
        {
            if (_hasPendingSnapshot)
            {
                snapshot = _pendingSnapshot;
                _hasPendingSnapshot = false;
            }
        }

        if (snapshot is null)
        {
            return;
        }

        ApplySnapshot(snapshot);
        _framesSinceCompaction++;
        if (_framesSinceCompaction >= 900)
        {
            ChartControl.CompactCaches();
            _framesSinceCompaction = 0;
        }
    }

    private void UpdateRenderLoopState()
    {
        if (_isAttached && _isOnActivePage)
        {
            if (!_renderTimer.IsEnabled)
            {
                _renderTimer.Start();
            }

            return;
        }

        _renderTimer.Stop();
    }

    private void ApplySnapshot(StudyAnalyticsSnapshot snapshot)
    {
        StatusTextBlock.Text = ResolveStatusText(snapshot);
        StatusTextBlock.Foreground = ResolveStatusBrush(snapshot);

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
        UpdateXAxisLabels(snapshot);
    }

    private void UpdateXAxisLabels(StudyAnalyticsSnapshot snapshot)
    {
        var buffer = snapshot.RealtimeBuffer;
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
        XRightTextBlock.Text = L("study.noise_curve.axis.now", "现在");
    }

    private void ApplyDefaultXAxisLabels()
    {
        XLeftTextBlock.Text = "-12s";
        XCenterTextBlock.Text = "-6s";
        XRightTextBlock.Text = L("study.noise_curve.axis.now", "现在");
    }

    private string ResolveStatusText(StudyAnalyticsSnapshot snapshot)
    {
        if (snapshot.State == StudyAnalyticsRuntimeState.Unsupported)
        {
            return L("study.environment.status.unsupported", "不支持");
        }

        if (snapshot.State == StudyAnalyticsRuntimeState.Error || snapshot.StreamStatus == NoiseStreamStatus.Error)
        {
            return L("study.environment.status.error", "错误");
        }

        if (snapshot.State == StudyAnalyticsRuntimeState.Paused)
        {
            return L("study.environment.status.paused", "已暂停");
        }

        if (snapshot.StreamStatus == NoiseStreamStatus.Noisy)
        {
            return L("study.environment.status.noisy", "嘈杂");
        }

        if (snapshot.State == StudyAnalyticsRuntimeState.Running && snapshot.StreamStatus == NoiseStreamStatus.Quiet)
        {
            return L("study.environment.status.quiet", "安静");
        }

        if (snapshot.State == StudyAnalyticsRuntimeState.Ready)
        {
            return L("study.environment.status.ready", "待机");
        }

        return L("study.environment.status.initializing", "初始化中");
    }

    private IBrush ResolveStatusBrush(StudyAnalyticsSnapshot snapshot)
    {
        if (snapshot.State == StudyAnalyticsRuntimeState.Unsupported ||
            snapshot.State == StudyAnalyticsRuntimeState.Error ||
            snapshot.StreamStatus == NoiseStreamStatus.Error)
        {
            return new SolidColorBrush(Color.Parse("#FFFF9D9D"));
        }

        if (snapshot.StreamStatus == NoiseStreamStatus.Noisy)
        {
            return new SolidColorBrush(Color.Parse("#FFFFD791"));
        }

        if (snapshot.State == StudyAnalyticsRuntimeState.Running && snapshot.StreamStatus == NoiseStreamStatus.Quiet)
        {
            return new SolidColorBrush(Color.Parse("#FFCBFFE8"));
        }

        return TryResolveThemeBrush("AdaptiveTextPrimaryBrush", "#FF1E293B");
    }

    private void ReloadLanguageCode()
    {
        var snapshot = _settingsService.Load();
        _languageCode = _localizationService.NormalizeLanguageCode(snapshot.LanguageCode);
    }

    private string L(string key, string fallback)
    {
        return _localizationService.GetString(_languageCode, key, fallback);
    }

    private IBrush TryResolveThemeBrush(string resourceKey, string fallbackHex)
    {
        if (TryGetResource(resourceKey, null, out var resource) && resource is IBrush brush)
        {
            return brush;
        }

        return new SolidColorBrush(Color.Parse(fallbackHex));
    }
}
