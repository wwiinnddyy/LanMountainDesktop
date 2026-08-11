using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using LanMountainDesktop.ComponentSystem;
using LanMountainDesktop.Models;
using LanMountainDesktop.PluginSdk;
using LanMountainDesktop.Services;
using LanMountainDesktop.Services.Settings;
using LanMountainDesktop.Theme;

namespace LanMountainDesktop.Views.Components;

public partial class StandbyDigitalClockWidget : UserControl,
    IDesktopComponentWidget,
    ITimeZoneAwareComponentWidget,
    IComponentPlacementContextAware,
    IComponentRuntimeContextAware
{
    private const double BaseCellSize = 48d;
    private const int BaseWidthCells = 4;
    private const int BaseHeightCells = 2;
    private const double DigitHeight = 130d;

    private readonly DispatcherTimer _timer = new()
    {
        Interval = TimeSpan.FromSeconds(1)
    };

    private string _componentId = BuiltInComponentIds.DesktopStandbyDigitalClock;
    private string _placementId = string.Empty;
    private DesktopComponentRenderMode _renderMode = DesktopComponentRenderMode.Live;
    private ISettingsService _settingsService = HostSettingsFacadeProvider.GetOrCreate().Settings;
    private readonly LocalizationService _localizationService = new();
    private TimeZoneService? _timeZoneService;
    private double _currentCellSize = 48;
    private TimeZoneInfo _clockTimeZone = WorldClockTimeZoneCatalog.ResolveTimeZoneOrLocal("China Standard Time");
    private string _languageCode = "zh-CN";
    private string? _componentColorScheme;

    // Track previous digit chars to detect changes
    private char _prevH1, _prevH2, _prevM1, _prevM2;
    private bool _colonVisible = true;
    private bool? _isNightModeApplied;

    // Digit state: track the current TextBlock for each digit position
    private TextBlock _h1Current, _h2Current, _m1Current, _m2Current;
    private bool _isAnimatingH1, _isAnimatingH2, _isAnimatingM1, _isAnimatingM2;

    public StandbyDigitalClockWidget()
    {
        InitializeComponent();

        _h1Current = H1Text;
        _h2Current = H2Text;
        _m1Current = M1Text;
        _m2Current = M2Text;

        _timer.Tick += OnTimerTick;
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
        PointerReleased += OnPointerReleased;

        LoadClockSettings();
        InitializeDigitsWithoutAnimation();
    }

    // ─── Interface implementations ───────────────────────────────

    public void ApplyCellSize(double cellSize)
    {
        _currentCellSize = Math.Max(1, cellSize);
        var mainRectangleCornerRadius = ComponentChromeCornerRadiusHelper.ResolveMainRectangleRadius();
        RootBorder.CornerRadius = mainRectangleCornerRadius;

        var scale = ResolveScale();
        RootBorder.Padding = new Thickness(Math.Clamp(14 * scale, 6, 28));
        ApplyModeVisualIfNeeded();
    }

    public void SetTimeZoneService(TimeZoneService timeZoneService)
    {
        ClearTimeZoneService();
        _timeZoneService = timeZoneService;
        _timeZoneService.TimeZoneChanged += OnTimeZoneChanged;
        UpdateClock();
    }

    public void ClearTimeZoneService()
    {
        if (_timeZoneService is null) return;
        _timeZoneService.TimeZoneChanged -= OnTimeZoneChanged;
        _timeZoneService = null;
    }

    public void SetComponentPlacementContext(string componentId, string? placementId)
    {
        _componentId = string.IsNullOrWhiteSpace(componentId)
            ? BuiltInComponentIds.DesktopStandbyDigitalClock
            : componentId.Trim();
        _placementId = placementId?.Trim() ?? string.Empty;
        LoadClockSettings();
        UpdateClock();
    }

    public void SetComponentRuntimeContext(DesktopComponentRuntimeContext context)
    {
        _componentId = string.IsNullOrWhiteSpace(context.ComponentId)
            ? BuiltInComponentIds.DesktopStandbyDigitalClock
            : context.ComponentId.Trim();
        _placementId = context.PlacementId?.Trim() ?? string.Empty;
        _renderMode = context.RenderMode;
    }

    // ─── Lifecycle ──────────────────────────────────────────────

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        LoadClockSettings();
        InitializeDigitsWithoutAnimation();
        _timer.Start();
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _timer.Stop();
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        UpdateClock();
    }

    private void OnTimeZoneChanged(object? sender, EventArgs e)
    {
        LoadClockSettings();
        InitializeDigitsWithoutAnimation();
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != MouseButton.Left ||
            _renderMode != DesktopComponentRenderMode.Live)
        {
            return;
        }

        AppLogger.Info(
            "AirAppLauncher",
            $"StandBy digital clock clicked. ComponentId='{_componentId}'; PlacementId='{_placementId}'.");
        AirAppLauncherServiceProvider.GetOrCreate().OpenWorldClock(_componentId, _placementId);
        e.Handled = true;
    }

    // ─── Clock update ───────────────────────────────────────────

    private void InitializeDigitsWithoutAnimation()
    {
        var now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _clockTimeZone);
        var h = now.Hour.ToString("D2", CultureInfo.InvariantCulture);
        var m = now.Minute.ToString("D2", CultureInfo.InvariantCulture);

        _prevH1 = h[0]; _prevH2 = h[1];
        _prevM1 = m[0]; _prevM2 = m[1];

        H1Text.Text = h[0].ToString();
        H2Text.Text = h[1].ToString();
        M1Text.Text = m[0].ToString();
        M2Text.Text = m[1].ToString();

        _h1Current = H1Text;
        _h2Current = H2Text;
        _m1Current = M1Text;
        _m2Current = M2Text;

        UpdateDateText(now);
        ApplyModeVisualIfNeeded();
    }

    private void UpdateClock()
    {
        var now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _clockTimeZone);
        var h = now.Hour.ToString("D2", CultureInfo.InvariantCulture);
        var m = now.Minute.ToString("D2", CultureInfo.InvariantCulture);

        ApplyModeVisualIfNeeded();

        // Detect digit changes and animate
        if (h[0] != _prevH1) AnimateDigit(H1Stack, _h1Current, h[0], _isAnimatingH1, value => _h1Current = value, value => _isAnimatingH1 = value);
        if (h[1] != _prevH2) AnimateDigit(H2Stack, _h2Current, h[1], _isAnimatingH2, value => _h2Current = value, value => _isAnimatingH2 = value);
        if (m[0] != _prevM1) AnimateDigit(M1Stack, _m1Current, m[0], _isAnimatingM1, value => _m1Current = value, value => _isAnimatingM1 = value);
        if (m[1] != _prevM2) AnimateDigit(M2Stack, _m2Current, m[1], _isAnimatingM2, value => _m2Current = value, value => _isAnimatingM2 = value);

        _prevH1 = h[0]; _prevH2 = h[1];
        _prevM1 = m[0]; _prevM2 = m[1];

        // Colon breathing
        ToggleColonOpacity();

        // Date
        UpdateDateText(now);
    }

    // ─── Digit scroll animation ─────────────────────────────────

    private void AnimateDigit(
        StackPanel stack,
        TextBlock currentTextBlock,
        char newDigit,
        bool isAnimating,
        Action<TextBlock> setCurrentTextBlock,
        Action<bool> setAnimating)
    {
        if (isAnimating)
        {
            // If still animating, just set the text directly and skip animation
            currentTextBlock.Text = newDigit.ToString();
            return;
        }

        setAnimating(true);
        var oldText = currentTextBlock;

        var newTextBlock = CreateDigitTextBlock(newDigit);
        stack.Children.Add(newTextBlock);

        // Apply TranslateTransform with transition for smooth animation
        var transform = new TranslateTransform { Y = 0 };
        stack.RenderTransform = transform;
        stack.RenderTransformOrigin = new RelativePoint(0, 0, RelativeUnit.Relative);

        stack.Transitions = new Transitions
        {
            new DoubleTransition
            {
                Property = TranslateTransform.YProperty,
                Duration = FluttermotionToken.Standard,
                Easing = new CubicEaseOut()
            }
        };

        // Trigger the animation: slide up by one digit height
        transform.Y = -DigitHeight;

        // After animation completes, clean up
        var cleanupTimer = new DispatcherTimer
        {
            Interval = FluttermotionToken.Standard + TimeSpan.FromMilliseconds(20)
        };
        cleanupTimer.Tick += (_, _) =>
        {
            cleanupTimer.Stop();

            // Remove transitions to prevent re-animation on reset
            stack.Transitions = null;

            // Remove the old TextBlock and reset position
            stack.Children.Remove(oldText);
            transform.Y = 0;

            setCurrentTextBlock(newTextBlock);
            setAnimating(false);
        };
        cleanupTimer.Start();
    }

    private TextBlock CreateDigitTextBlock(char digit)
    {
        var accentBrush = ResolveAccentBrush();
        return new TextBlock
        {
            Text = digit.ToString(),
            FontSize = 120,
            FontWeight = FontWeight.Bold,
            Foreground = accentBrush,
            Width = 88,
            Height = DigitHeight,
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            LineHeight = DigitHeight
        };
    }

    // ─── Colon breathing ────────────────────────────────────────

    private void ToggleColonOpacity()
    {
        _colonVisible = !_colonVisible;

        if (ColonText.Transitions is null)
        {
            ColonText.Transitions = new Transitions
            {
                new DoubleTransition
                {
                    Property = OpacityProperty,
                    Duration = TimeSpan.FromMilliseconds(400),
                    Easing = new CubicEaseInOut()
                }
            };
        }

        ColonText.Opacity = _colonVisible ? 1.0 : 0.25;
    }

    // ─── Color system ───────────────────────────────────────────

    private IBrush ResolveAccentBrush()
    {
        var useMonetColor = ComponentColorSchemeHelper.ShouldUseMonetColor(
            _componentColorScheme,
            ComponentColorSchemeHelper.GetCurrentGlobalThemeColorMode());

        var isNight = ResolveIsNightMode();

        if (useMonetColor)
        {
            // Use the Monet accent brush from dynamic resources
            if (this.TryFindResource("AdaptiveAccentBrush", out var accentRes) && accentRes is IBrush accentBrush)
            {
                return accentBrush;
            }

            // Fallback: compute from SystemAccentColor
            if (this.TryFindResource("SystemAccentColor", out var sysAccent) && sysAccent is Color sysColor)
            {
                return new SolidColorBrush(isNight ? Lighten(sysColor, 0.3) : sysColor);
            }
        }

        // Native / fallback: warm orange-red accent (iPhone StandBy inspired)
        return isNight
            ? CreateBrush("#FF8A65")
            : CreateBrush("#E84530");
    }

    private static Color Lighten(Color color, double amount)
    {
        var r = (byte)Math.Min(255, color.R + (255 - color.R) * amount);
        var g = (byte)Math.Min(255, color.G + (255 - color.G) * amount);
        var b = (byte)Math.Min(255, color.B + (255 - color.B) * amount);
        return new Color(color.A, r, g, b);
    }

    // ─── Night / Day mode ───────────────────────────────────────

    private void ApplyModeVisualIfNeeded()
    {
        var isNightMode = ResolveIsNightMode();
        if (_isNightModeApplied.HasValue && _isNightModeApplied.Value == isNightMode)
            return;

        _isNightModeApplied = isNightMode;
        ApplyModeVisual(isNightMode);
    }

    private void ApplyModeVisual(bool isNightMode)
    {
        RootBorder.Background = isNightMode
            ? CreateLinearGradientBrush("#1F2C4B", "#131B33")
            : CreateLinearGradientBrush("#EEF2FA", "#E7ECF6");

        var accentBrush = ResolveAccentBrush();

        // Update current digit TextBlocks with accent color
        foreach (var tb in new[] { _h1Current, _h2Current, _m1Current, _m2Current })
        {
            if (tb is not null) tb.Foreground = accentBrush;
        }

        // Also update the named XAML TextBlocks (in case they haven't been replaced yet)
        H1Text.Foreground = accentBrush;
        H2Text.Foreground = accentBrush;
        M1Text.Foreground = accentBrush;
        M2Text.Foreground = accentBrush;

        ColonText.Foreground = accentBrush;

        // Date text uses muted brush from dynamic resource
        if (this.TryFindResource("AdaptiveTextMutedBrush", out var mutedRes) && mutedRes is IBrush mutedBrush)
        {
            DateTextBlock.Foreground = mutedBrush;
        }
        else
        {
            DateTextBlock.Foreground = CreateBrush(isNightMode ? "#7E8593" : "#7E8593");
        }
    }

    private bool ResolveIsNightMode()
    {
        if (ActualThemeVariant == ThemeVariant.Dark) return true;
        if (ActualThemeVariant == ThemeVariant.Light) return false;

        if (this.TryFindResource("AdaptiveSurfaceBaseBrush", out var value) &&
            value is ISolidColorBrush solidBrush)
        {
            return CalculateRelativeLuminance(solidBrush.Color) < 0.45;
        }

        return false;
    }

    private static double CalculateRelativeLuminance(Color color)
    {
        static double ToLinear(double channel)
        {
            return channel <= 0.03928
                ? channel / 12.92
                : Math.Pow((channel + 0.055) / 1.055, 2.4);
        }

        var r = ToLinear(color.R / 255d);
        var g = ToLinear(color.G / 255d);
        var b = ToLinear(color.B / 255d);
        return 0.2126 * r + 0.7152 * g + 0.0722 * b;
    }

    // ─── Date text ──────────────────────────────────────────────

    private void UpdateDateText(DateTime now)
    {
        var culture = string.Equals(_languageCode, "zh-CN", StringComparison.OrdinalIgnoreCase)
            ? new CultureInfo("zh-CN")
            : CultureInfo.CurrentUICulture;

        var dateStr = now.ToString("M", culture);
        var dayStr = now.ToString("dddd", culture);
        DateTextBlock.Text = $"{dateStr}  {dayStr}";
    }

    // ─── Settings ───────────────────────────────────────────────

    private void LoadClockSettings()
    {
        var appSnapshot = _settingsService.LoadSnapshot<AppSettingsSnapshot>(SettingsScope.App);
        var componentSnapshot = _settingsService.LoadSnapshot<ComponentSettingsSnapshot>(
            SettingsScope.ComponentInstance,
            _componentId,
            _placementId);

        _languageCode = _localizationService.NormalizeLanguageCode(appSnapshot.LanguageCode);

        var configuredTimeZoneId = string.IsNullOrWhiteSpace(componentSnapshot.DesktopClockTimeZoneId)
            ? "China Standard Time"
            : componentSnapshot.DesktopClockTimeZoneId.Trim();

        _clockTimeZone = WorldClockTimeZoneCatalog.ResolveTimeZoneOrLocal(configuredTimeZoneId);
        _componentColorScheme = componentSnapshot.ColorSchemeSource;
    }

    // ─── Scaling ────────────────────────────────────────────────

    private double ResolveScale()
    {
        var cellScale = Math.Clamp(_currentCellSize / BaseCellSize, 0.60, 1.90);
        var heightScale = Bounds.Height > 1 ? Math.Clamp(Bounds.Height / (BaseCellSize * BaseHeightCells), 0.58, 2.0) : 1;
        var widthScale = Bounds.Width > 1 ? Math.Clamp(Bounds.Width / (BaseCellSize * BaseWidthCells), 0.58, 2.0) : 1;
        return Math.Clamp(Math.Min(cellScale, Math.Min(heightScale, widthScale) * 1.05), 0.58, 1.95);
    }

    // ─── Brush helpers ──────────────────────────────────────────

    private static IBrush CreateBrush(string colorHex)
    {
        return new SolidColorBrush(Color.Parse(colorHex));
    }

    private static IBrush CreateLinearGradientBrush(string fromColorHex, string toColorHex)
    {
        return new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops = new GradientStops
            {
                new GradientStop(Color.Parse(fromColorHex), 0),
                new GradientStop(Color.Parse(toColorHex), 1)
            }
        };
    }
}
