using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;

namespace LanMountainDesktop.Views.Components;

public partial class TimerWidget : UserControl, IDesktopComponentWidget
{
    private const int MaxTimerSeconds = 60;
    private const double DialSize = 224;
    private const double Center = DialSize / 2;

    private readonly DispatcherTimer _timer = new()
    {
        Interval = TimeSpan.FromSeconds(1)
    };

    private double _currentCellSize = 48;
    private bool _isRunning;
    private int _remainingSeconds;
    private bool? _isNightModeApplied;

    public TimerWidget()
    {
        InitializeComponent();

        _timer.Tick += OnTimerTick;
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
        SizeChanged += OnSizeChanged;
        PlayButtonBorder.PointerPressed += OnPlayButtonPointerPressed;

        UpdateVisual();
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        UpdateVisual();
        _timer.Start();
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _timer.Stop();
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        ApplyCellSize(_currentCellSize);
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        ApplyModeVisualIfNeeded();

        if (!_isRunning)
        {
            return;
        }

        if (_remainingSeconds > 0)
        {
            _remainingSeconds--;
        }

        if (_remainingSeconds <= 0)
        {
            _remainingSeconds = 0;
            _isRunning = false;
        }

        UpdateVisual();
    }

    private void OnPlayButtonPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (_isRunning)
        {
            _isRunning = false;
        }
        else
        {
            if (_remainingSeconds <= 0)
            {
                _remainingSeconds = MaxTimerSeconds;
            }

            _isRunning = true;
        }

        UpdateVisual();
        e.Handled = true;
    }

    private void UpdateVisual()
    {
        ApplyModeVisualIfNeeded();
        UpdateNumberVisual();
        UpdateHandGeometry();
        UpdatePlayButtonVisual();
    }

    private void UpdateNumberVisual()
    {
        var current = Math.Clamp(_remainingSeconds, 0, MaxTimerSeconds);
        var top = current == 0 ? MaxTimerSeconds : current - 1;
        var next = (current + 1) % (MaxTimerSeconds + 1);
        var nextNext = (current + 2) % (MaxTimerSeconds + 1);

        TopNumberTextBlock.Text = top.ToString(CultureInfo.InvariantCulture);
        MainNumberTextBlock.Text = current.ToString(CultureInfo.InvariantCulture);
        NextNumberTextBlock.Text = next.ToString(CultureInfo.InvariantCulture);
        NextNextNumberTextBlock.Text = nextNext.ToString(CultureInfo.InvariantCulture);
    }

    private void UpdateHandGeometry()
    {
        var angleDeg = (_remainingSeconds % (MaxTimerSeconds + 1)) / 60d * 360d;
        var radians = angleDeg * Math.PI / 180d;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);

        var start = new Point(Center - cos * 2, Center - sin * 2);
        var end = new Point(Center + cos * 68, Center + sin * 68);
        var glowEnd = new Point(Center + cos * 74, Center + sin * 74);

        HandLine.StartPoint = start;
        HandLine.EndPoint = end;
        HandGlowLine.StartPoint = start;
        HandGlowLine.EndPoint = glowEnd;
    }

    private void UpdatePlayButtonVisual()
    {
        PlayIconPath.Data = Geometry.Parse(_isRunning
            ? "M 0,0 L 4,0 L 4,14 L 0,14 Z M 8,0 L 12,0 L 12,14 L 8,14 Z"
            : "M 0,0 L 0,14 L 11,7 Z");
    }

    private void ApplyModeVisualIfNeeded()
    {
        var isNightMode = ResolveIsNightMode();
        if (_isNightModeApplied.HasValue && _isNightModeApplied.Value == isNightMode)
        {
            return;
        }

        _isNightModeApplied = isNightMode;
        ApplyModeVisual(isNightMode);
    }

    private void ApplyModeVisual(bool isNightMode)
    {
        RootBorder.Background = CreateBrush(isNightMode ? "#313540" : "#E8EAEE");
        TimerPanelBorder.Background = isNightMode
            ? CreateLinearGradientBrush("#2F3441", "#202632")
            : CreateLinearGradientBrush("#FBFCFE", "#F3F5F9");
        TimerPanelBorder.BorderBrush = CreateBrush(isNightMode ? "#3B4353" : "#E2E7F0");

        CenterDivider.Background = CreateBrush(isNightMode ? "#434B5C" : "#D5DAE3");

        TopNumberTextBlock.Foreground = CreateBrush(isNightMode ? "#7A8397" : "#AEB4C1");
        MainNumberTextBlock.Foreground = CreateBrush(isNightMode ? "#F3F6FE" : "#0F141C");
        NextNumberTextBlock.Foreground = CreateBrush(isNightMode ? "#8089A0" : "#B2B8C4");
        NextNextNumberTextBlock.Foreground = CreateBrush(isNightMode ? "#6A7388" : "#C8CDD7");

        var markBrush = CreateBrush(isNightMode ? "#5A657D" : "#D0D6E1");
        ScaleMark1.Background = markBrush;
        ScaleMark2.Background = markBrush;
        ScaleMark3.Background = markBrush;
        ScaleMark4.Background = markBrush;

        PlayButtonBorder.BorderBrush = CreateBrush(isNightMode ? "#4A5367" : "#D3D9E4");
        PlayIconPath.Fill = CreateBrush(isNightMode ? "#8E98AF" : "#98A2B8");

        CenterDotRing.Fill = CreateBrush(isNightMode ? "#EAF0FF" : "#FDFEFF");
        CenterDotRing.Stroke = CreateBrush(isNightMode ? "#A9B8D5" : "#E3E8F0");
        CenterDotCore.Fill = CreateBrush("#FF4D63");
        HandLine.Stroke = CreateBrush("#FF4D63");
        HandGlowLine.Stroke = CreateBrush(isNightMode ? "#FF6A6E" : "#FF7A78");
        HandGlowLine.Opacity = isNightMode ? 0.28 : 0.20;
    }

    public void ApplyCellSize(double cellSize)
    {
        _currentCellSize = Math.Max(1, cellSize);
        var scale = ResolveScale();

        RootBorder.CornerRadius = ComponentChromeCornerRadiusHelper.Scale(34 * scale, 12, 48);
        RootBorder.Padding = new Thickness(
            ComponentChromeCornerRadiusHelper.SafeValue(14 * scale, 7, 22));
        TimerPanelBorder.CornerRadius = ComponentChromeCornerRadiusHelper.Scale(32 * scale, 12, 42);

        PlayButtonBorder.Width = Math.Clamp(42 * scale, 28, 58);
        PlayButtonBorder.Height = Math.Clamp(42 * scale, 28, 58);
        PlayButtonBorder.CornerRadius = new CornerRadius(PlayButtonBorder.Width / 2d);

        ApplyModeVisualIfNeeded();
    }

    private double ResolveScale()
    {
        var cellScale = Math.Clamp(_currentCellSize / 44d, 0.60, 1.90);
        var heightScale = Bounds.Height > 1 ? Math.Clamp(Bounds.Height / 300d, 0.58, 2.0) : 1;
        var widthScale = Bounds.Width > 1 ? Math.Clamp(Bounds.Width / 300d, 0.58, 2.0) : 1;
        return Math.Clamp(Math.Min(cellScale, Math.Min(heightScale, widthScale) * 1.05), 0.58, 1.95);
    }

    private bool ResolveIsNightMode()
    {
        if (ActualThemeVariant == ThemeVariant.Dark)
        {
            return true;
        }

        if (ActualThemeVariant == ThemeVariant.Light)
        {
            return false;
        }

        if (this.TryFindResource("AdaptiveSurfaceBaseBrush", out var value) &&
            value is ISolidColorBrush solidBrush)
        {
            return CalculateRelativeLuminance(solidBrush.Color) < 0.45;
        }

        return false;
    }

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
}
