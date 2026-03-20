using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
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

    private static readonly Color DarkSubstrate = Color.Parse("#FF0B1220");
    private static readonly Color LightSubstrate = Color.Parse("#FFF1F5FA");
    private static readonly FontFamily MiSansVariableFontFamily = new("MiSans VF, avares://LanMountainDesktop/Assets/Fonts#MiSans");

    private readonly IStudyAnalyticsService _studyAnalyticsService = StudyAnalyticsServiceFactory.CreateDefault();
    private readonly StudyAnalyticsMonitoringLeaseCoordinator _monitoringLeaseCoordinator = StudyAnalyticsMonitoringLeaseCoordinatorFactory.CreateDefault();
    private LanMountainDesktop.PluginSdk.ISettingsService _settingsService = LanMountainDesktop.Services.Settings.HostSettingsFacadeProvider.GetOrCreate().Settings;
    private readonly LocalizationService _localizationService = new();
    private readonly DispatcherTimer _uiTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(100)
    };

    private double _currentCellSize = 48;
    private string _languageCode = "zh-CN";
    private bool _isAttached;
    private bool _isOnActivePage = true;
    private bool _isDisposed;
    private bool _isCompactMode;
    private bool _isUltraCompactMode;
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

        _uiTimer.Tick += OnUiTimerTick;
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
        SizeChanged += OnSizeChanged;
        ActualThemeVariantChanged += OnActualThemeVariantChanged;

        ApplyVariableFontFamily();
        ReloadLanguageCode();
        ApplyCellSize(_currentCellSize);
        ApplyDefaultXAxisLabels();
        ApplyLocalizedAxisLabels();
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
        var wasOnActivePage = _isOnActivePage;
        _isOnActivePage = isOnActivePage;
        
        UpdateMonitoringLeaseState();
        
        if (isOnActivePage && !wasOnActivePage)
        {
            RefreshVisual();
        }
        
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
        if (_isAttached)
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

        TitleTextBlock.Text = L("study.noise_distribution.title", "Noise Level Distribution");
        ApplyLocalizedAxisLabels();

        var isSessionRunning = snapshot.Session.State == StudySessionRuntimeState.Running;
        var isSessionReport = snapshot.DataMode == StudyDataMode.SessionReport && snapshot.LastSessionReport is not null;
        var isSessionView = isSessionRunning || isSessionReport;
        ModeTextBlock.Text = isSessionView
            ? L("study.noise_distribution.mode.session", "Session")
            : L("study.noise_distribution.mode.realtime", "Realtime");
        ApplyModeBadgeColor(panelColor, isSessionView ? Color.Parse("#FF0F6B49") : Color.Parse("#FF2F5DA8"));

        var points = isSessionReport && snapshot.LastSessionReport is not null
            ? StudySessionReportProjection.BuildSyntheticRealtimePoints(snapshot.LastSessionReport, snapshot.Config)
            : snapshot.RealtimeBuffer;

        ChartControl.UpdateSeries(points, snapshot.Config.BaselineDb);
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
        var cellScale = Math.Clamp(_currentCellSize / 48d, 0.76, 2.4);
        var widthScale = Bounds.Width > 1 ? Bounds.Width / 520d : cellScale;
        var heightScale = Bounds.Height > 1 ? Bounds.Height / 240d : cellScale;
        var boundsScale = Math.Clamp(Math.Min(widthScale, heightScale), 0.52, 2.3);
        var scale = Math.Clamp(Math.Min(cellScale, boundsScale * 1.06), 0.52, 2.3);

        _isCompactMode = scale < 0.92 || (Bounds.Width > 1 && Bounds.Width < 360) || (Bounds.Height > 1 && Bounds.Height < 180);
        _isUltraCompactMode = scale < 0.74 || (Bounds.Width > 1 && Bounds.Width < 300) || (Bounds.Height > 1 && Bounds.Height < 142);

        var compactMultiplier = _isUltraCompactMode ? 0.76 : _isCompactMode ? 0.88 : 1.0;
        RootBorder.CornerRadius = ComponentChromeCornerRadiusHelper.Scale(_currentCellSize * 0.44, 12, 34);
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
        var samples = BuildPanelBackgroundSamples(panelColor);
        var primary = CreateAdaptiveBrush(samples, ValueColorCandidates, minContrast: 4.5);
        var secondary = CreateAdaptiveBrush(samples, SecondaryColorCandidates, minContrast: 4.5);

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
        ModeTextBlock.Foreground = CreateAdaptiveBrush(new[] { badgeComposite }, ValueColorCandidates, minContrast: 4.5);
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
        var snapshot = _settingsService.Load();
        _languageCode = _localizationService.NormalizeLanguageCode(snapshot.LanguageCode);
    }

    private void ApplyVariableFontFamily()
    {
        TitleTextBlock.FontFamily = MiSansVariableFontFamily;
        SummaryTextBlock.FontFamily = MiSansVariableFontFamily;
        ModeTextBlock.FontFamily = MiSansVariableFontFamily;
        YExtremeTextBlock.FontFamily = MiSansVariableFontFamily;
        YNoisyTextBlock.FontFamily = MiSansVariableFontFamily;
        YNormalTextBlock.FontFamily = MiSansVariableFontFamily;
        YQuietTextBlock.FontFamily = MiSansVariableFontFamily;
        XLeftTextBlock.FontFamily = MiSansVariableFontFamily;
        XCenterTextBlock.FontFamily = MiSansVariableFontFamily;
        XRightTextBlock.FontFamily = MiSansVariableFontFamily;
    }

    private void ApplyVariableWeights(double scale)
    {
        var weightProgress = Math.Clamp((scale - 0.52) / 1.5, 0, 1);
        var compactDelta = _isUltraCompactMode ? 40 : _isCompactMode ? 20 : 0;

        TitleTextBlock.FontWeight = ToVariableWeight(Lerp(560, 680, weightProgress));
        SummaryTextBlock.FontWeight = ToVariableWeight(Lerp(550 + compactDelta, 700, weightProgress));
        ModeTextBlock.FontWeight = ToVariableWeight(Lerp(560, 700, weightProgress));
        YExtremeTextBlock.FontWeight = ToVariableWeight(Lerp(500, 620, weightProgress));
        YNoisyTextBlock.FontWeight = ToVariableWeight(Lerp(500, 620, weightProgress));
        YNormalTextBlock.FontWeight = ToVariableWeight(Lerp(500, 620, weightProgress));
        YQuietTextBlock.FontWeight = ToVariableWeight(Lerp(500, 620, weightProgress));
        XLeftTextBlock.FontWeight = ToVariableWeight(Lerp(500, 620, weightProgress));
        XCenterTextBlock.FontWeight = ToVariableWeight(Lerp(500, 620, weightProgress));
        XRightTextBlock.FontWeight = ToVariableWeight(Lerp(500, 620, weightProgress));
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

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;

        _uiTimer.Stop();
        _uiTimer.Tick -= OnUiTimerTick;
        AttachedToVisualTree -= OnAttachedToVisualTree;
        DetachedFromVisualTree -= OnDetachedFromVisualTree;
        SizeChanged -= OnSizeChanged;
        ActualThemeVariantChanged -= OnActualThemeVariantChanged;

        _monitoringLease?.Dispose();
        _monitoringLease = null;
    }
}
