using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using LanMountainDesktop.Models;
using LanMountainDesktop.Services;
using LanMountainDesktop.Theme;
using Material.Icons;

namespace LanMountainDesktop.Views.Components;

public partial class StudySessionControlWidget : UserControl, IDesktopComponentWidget, IDesktopPageVisibilityAwareComponentWidget, IDisposable
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

    private static readonly Color[] WarningColorCandidates =
    {
        Color.Parse("#FFFFD4D4"),
        Color.Parse("#FFFEE2E2"),
        Color.Parse("#FF7F1D1D"),
        Color.Parse("#FF991B1B"),
        Color.Parse("#FFFFFFFF"),
        Color.Parse("#FF111827")
    };

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
    private string _languageCode = "zh-CN";
    private bool _isAttached;
    private bool _isOnActivePage = true;
    private bool _isDisposed;
    private bool _isCompactMode;
    private bool _isUltraCompactMode;
    private IDisposable? _monitoringLease;
    private string? _transientMessage;
    private DateTimeOffset _transientMessageExpireAt;

    public StudySessionControlWidget()
    {
        InitializeComponent();

        _uiTimer.Tick += OnUiTimerTick;
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
        SizeChanged += OnSizeChanged;
        ActualThemeVariantChanged += OnActualThemeVariantChanged;

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

    private void OnActionButtonClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var snapshot = _studyAnalyticsService.GetSnapshot();
        var isReportViewing = snapshot.DataMode == StudyDataMode.SessionReport && snapshot.LastSessionReport is not null;
        if (isReportViewing)
        {
            _studyAnalyticsService.ClearLastSessionReport();
            _transientMessage = null;
            RefreshVisual();
            return;
        }

        var isRunning = snapshot.Session.State == StudySessionRuntimeState.Running;

        var success = isRunning
            ? _studyAnalyticsService.StopStudySession()
            : _studyAnalyticsService.StartStudySession();

        if (!success)
        {
            _transientMessage = isRunning
                ? L("study.session_control.stop_failed", "Unable to stop session")
                : L("study.session_control.start_failed", "Unable to start session");
            _transientMessageExpireAt = DateTimeOffset.UtcNow.AddSeconds(2.2);
        }
        else
        {
            _transientMessage = null;
        }

        RefreshVisual();
    }

    private void RefreshVisual()
    {
        var snapshot = _studyAnalyticsService.GetSnapshot();
        var now = DateTimeOffset.UtcNow;
        var panelColor = ResolvePanelBackgroundColor();
        ApplyTypographyByBackground(panelColor);

        if (_transientMessage is not null && now > _transientMessageExpireAt)
        {
            _transientMessage = null;
        }

        var isReportViewing = snapshot.DataMode == StudyDataMode.SessionReport && snapshot.LastSessionReport is not null;
        if (isReportViewing)
        {
            PrimaryTextBlock.Text = L("study.session_control.report_preview", "Preview Report");
            SecondaryTextBlock.Text = _transientMessage ?? L("study.session_control.report_confirm_hint", "Tap right button to confirm");
            ActionIcon.Kind = MaterialIconKind.Check;
            ApplyActionBadgeStyle(panelColor, Color.Parse("#FF34D399"));
            ApplyTransientWarningTintIfNeeded(panelColor);
            return;
        }

        var isRunning = snapshot.Session.State == StudySessionRuntimeState.Running;
        if (isRunning)
        {
            PrimaryTextBlock.Text = L("study.session_control.action.stop", "Stop Study Session");
            SecondaryTextBlock.Text = _transientMessage ?? string.Format(
                L("study.session_control.running_elapsed_format", "Elapsed {0}"),
                FormatElapsed(snapshot.Session.Elapsed));
            ActionIcon.Kind = MaterialIconKind.Stop;
            ApplyActionBadgeStyle(panelColor, Color.Parse("#FFF97373"));
            ApplyTransientWarningTintIfNeeded(panelColor);
            return;
        }

        PrimaryTextBlock.Text = L("study.session_control.action.start", "Start Study Session");
        SecondaryTextBlock.Text = _transientMessage ?? ResolveIdleHint(snapshot);
        ActionIcon.Kind = MaterialIconKind.Play;
        ApplyActionBadgeStyle(panelColor, Color.Parse("#FF60A5FA"));
        ApplyTransientWarningTintIfNeeded(panelColor);
    }

    private string ResolveIdleHint(StudyAnalyticsSnapshot snapshot)
    {
        if (snapshot.State == StudyAnalyticsRuntimeState.Unsupported)
        {
            return L("study.environment.status.unsupported", "Unsupported");
        }

        if (snapshot.State == StudyAnalyticsRuntimeState.Error || snapshot.StreamStatus == NoiseStreamStatus.Error)
        {
            return L("study.environment.status.error", "Error");
        }

        if (snapshot.Session.State == StudySessionRuntimeState.Completed && snapshot.LastSessionReport is not null)
        {
            return string.Format(
                L("study.session_control.last_session_format", "Last {0}"),
                FormatElapsed(snapshot.LastSessionReport.Duration));
        }

        return L("study.session_control.idle_hint", "Tap the right button to start");
    }

    private void UpdateAdaptiveLayout()
    {
        var cellScale = Math.Clamp(_currentCellSize / 48d, 0.78, 2.4);
        var widthScale = Bounds.Width > 1 ? Bounds.Width / 280d : cellScale;
        var heightScale = Bounds.Height > 1 ? Bounds.Height / 140d : cellScale;
        var boundsScale = Math.Clamp(Math.Min(widthScale, heightScale), 0.56, 2.2);
        var scale = Math.Clamp(Math.Min(cellScale, boundsScale * 1.05), 0.56, 2.2);

        _isCompactMode = scale < 0.92 || (Bounds.Width > 1 && Bounds.Width < 220) || (Bounds.Height > 1 && Bounds.Height < 92);
        _isUltraCompactMode = scale < 0.74 || (Bounds.Width > 1 && Bounds.Width < 180) || (Bounds.Height > 1 && Bounds.Height < 76);

        var compactMultiplier = _isUltraCompactMode ? 0.78 : _isCompactMode ? 0.90 : 1.0;
        RootBorder.CornerRadius = new CornerRadius(Math.Clamp(_currentCellSize * 0.34, 10, 28));
        RootBorder.Padding = new Thickness(
            Math.Clamp(14 * scale * compactMultiplier, 7, 22),
            Math.Clamp(10 * scale * compactMultiplier, 5, 16));

        LayoutGrid.ColumnSpacing = _isUltraCompactMode
            ? Math.Clamp(6 * scale, 3, 8)
            : _isCompactMode
                ? Math.Clamp(8 * scale, 4, 10)
                : Math.Clamp(10 * scale, 6, 14);

        PrimaryTextBlock.FontSize = Math.Clamp(17 * scale, 10, 30);
        SecondaryTextBlock.FontSize = Math.Clamp(11 * scale, 8, 18);
        LeftTextStack.Spacing = _isUltraCompactMode ? 0 : Math.Clamp(2 * scale, 1, 4);

        var buttonSize = Math.Clamp(48 * scale * compactMultiplier, 28, 72);
        ActionButton.Width = buttonSize;
        ActionButton.Height = buttonSize;
        ActionIconBorder.Width = buttonSize;
        ActionIconBorder.Height = buttonSize;
        ActionIconBorder.CornerRadius = new CornerRadius(buttonSize / 2d);
        ActionIcon.Width = Math.Clamp(buttonSize * 0.44, 14, 30);
        ActionIcon.Height = Math.Clamp(buttonSize * 0.44, 14, 30);

        SecondaryTextBlock.IsVisible = !_isUltraCompactMode;
    }

    private void ApplyTypographyByBackground(Color panelColor)
    {
        var samples = BuildPanelBackgroundSamples(panelColor);
        var primary = CreateAdaptiveBrush(samples, PrimaryColorCandidates, minContrast: 4.5);
        var secondary = CreateAdaptiveBrush(samples, SecondaryColorCandidates, minContrast: 4.5);

        PrimaryTextBlock.Foreground = primary;
        SecondaryTextBlock.Foreground = secondary;
    }

    private void ApplyTransientWarningTintIfNeeded(Color panelColor)
    {
        if (string.IsNullOrWhiteSpace(_transientMessage))
        {
            return;
        }

        var samples = BuildPanelBackgroundSamples(panelColor);
        SecondaryTextBlock.Foreground = CreateAdaptiveBrush(samples, WarningColorCandidates, minContrast: 4.5);
    }

    private void ApplyActionBadgeStyle(Color panelColor, Color baseColor)
    {
        var panelLuminance = RelativeLuminance(ToOpaqueAgainst(panelColor, DarkSubstrate));
        var badgeAlpha = panelLuminance > 0.58
            ? (byte)0xE2
            : panelLuminance > 0.46
                ? (byte)0xD8
                : (byte)0xC8;

        var badgeColor = Color.FromArgb(badgeAlpha, baseColor.R, baseColor.G, baseColor.B);
        var badgeComposite = ToOpaqueAgainst(badgeColor, ToOpaqueAgainst(panelColor, DarkSubstrate));

        ActionIconBorder.Background = new SolidColorBrush(badgeColor);
        ActionIconBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(0x96, 0xFF, 0xFF, 0xFF));
        ActionIcon.Foreground = CreateAdaptiveBrush(new[] { badgeComposite }, PrimaryColorCandidates, minContrast: 4.5);
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

    private static string FormatElapsed(TimeSpan elapsed)
    {
        if (elapsed.TotalHours >= 1)
        {
            return elapsed.ToString(@"hh\:mm\:ss");
        }

        return elapsed.ToString(@"mm\:ss");
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
    }
}
