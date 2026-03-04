using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using LanMountainDesktop.Models;
using LanMountainDesktop.Services;
using LanMountainDesktop.Theme;

namespace LanMountainDesktop.Views.Components;

public partial class StudyNoiseCurveWidget : UserControl, IDesktopComponentWidget, IDesktopPageVisibilityAwareComponentWidget
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

    private static readonly Color DarkSubstrate = Color.Parse("#FF0B1220");
    private static readonly Color LightSubstrate = Color.Parse("#FFF1F5FA");

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

        _renderTimer.Tick += OnRenderTimerTick;
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
        SizeChanged += OnSizeChanged;

        ReloadLanguageCode();
        ApplyCellSize(_currentCellSize);
        ApplyDefaultXAxisLabels();

        var panelColor = ResolvePanelBackgroundColor();
        ApplyTypographyByBackground(panelColor);
        ApplyStatusBadgeStyle(StatusVisualKind.Default, panelColor);
    }

    public void ApplyCellSize(double cellSize)
    {
        _currentCellSize = Math.Max(1, cellSize);
        var scale = Math.Clamp(_currentCellSize / 48d, 0.78, 2.4);

        RootBorder.CornerRadius = new CornerRadius(Math.Clamp(_currentCellSize * 0.44, 14, 42));
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
        var panelColor = ResolvePanelBackgroundColor();
        ApplyTypographyByBackground(panelColor);
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
        var panelColor = ResolvePanelBackgroundColor();
        ApplyTypographyByBackground(panelColor);

        var statusKind = ResolveStatusVisualKind(snapshot);
        StatusTextBlock.Text = ResolveStatusText(snapshot);
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
        UpdateXAxisLabels(snapshot);
    }

    private void ApplyTypographyByBackground(Color panelColor)
    {
        var samples = BuildPanelBackgroundSamples(panelColor);
        var valueBrush = CreateAdaptiveBrush(samples, ValueToneCandidates, LargeTextMinContrast);
        var axisBrush = CreateAdaptiveBrush(samples, AxisToneCandidates, NormalTextMinContrast);

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
            StatusVisualKind.Quiet => Color.Parse("#FF0F6B49"),
            StatusVisualKind.Noisy => Color.Parse("#FF805018"),
            StatusVisualKind.Error => Color.Parse("#FF8D2A3A"),
            _ => Color.Parse("#FF213547")
        };

        var panelLuminance = RelativeLuminance(ToOpaqueAgainst(panelColor, DarkSubstrate));
        var badgeAlpha = panelLuminance > 0.58
            ? (byte)0xE6
            : panelLuminance > 0.46
                ? (byte)0xDB
                : (byte)0xCC;

        var badgeColor = Color.FromArgb(badgeAlpha, badgeBaseColor.R, badgeBaseColor.G, badgeBaseColor.B);
        var badgeComposite = ToOpaqueAgainst(badgeColor, ToOpaqueAgainst(panelColor, DarkSubstrate));

        StatusBadgeBorder.Background = new SolidColorBrush(badgeColor);
        StatusBadgeBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(0x96, 0xFF, 0xFF, 0xFF));
        StatusTextBlock.Foreground = CreateAdaptiveBrush(new[] { badgeComposite }, StatusTextToneCandidates, NormalTextMinContrast);
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

    private Color ResolvePanelBackgroundColor()
    {
        if (RootBorder.Background is ISolidColorBrush solidBackground)
        {
            return solidBackground.Color;
        }

        if (Resources.TryGetResource("AdaptiveGlassStrongBackgroundBrush", ActualThemeVariant, out var resource) &&
            resource is ISolidColorBrush solidBrush)
        {
            return solidBrush.Color;
        }

        return Color.Parse("#FF1E293B");
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

        // If none reaches the target, pick the highest-contrast candidate.
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
        XRightTextBlock.Text = L("study.noise_curve.axis.now", "Now");
    }

    private void ApplyDefaultXAxisLabels()
    {
        XLeftTextBlock.Text = "-12s";
        XCenterTextBlock.Text = "-6s";
        XRightTextBlock.Text = L("study.noise_curve.axis.now", "Now");
    }

    private string ResolveStatusText(StudyAnalyticsSnapshot snapshot)
    {
        if (snapshot.State == StudyAnalyticsRuntimeState.Unsupported)
        {
            return L("study.environment.status.unsupported", "Unsupported");
        }

        if (snapshot.State == StudyAnalyticsRuntimeState.Error || snapshot.StreamStatus == NoiseStreamStatus.Error)
        {
            return L("study.environment.status.error", "Error");
        }

        if (snapshot.State == StudyAnalyticsRuntimeState.Paused)
        {
            return L("study.environment.status.paused", "Paused");
        }

        if (snapshot.StreamStatus == NoiseStreamStatus.Noisy)
        {
            return L("study.environment.status.noisy", "Noisy");
        }

        if (snapshot.State == StudyAnalyticsRuntimeState.Running && snapshot.StreamStatus == NoiseStreamStatus.Quiet)
        {
            return L("study.environment.status.quiet", "Quiet");
        }

        if (snapshot.State == StudyAnalyticsRuntimeState.Ready)
        {
            return L("study.environment.status.ready", "Ready");
        }

        return L("study.environment.status.initializing", "Initializing");
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
}
