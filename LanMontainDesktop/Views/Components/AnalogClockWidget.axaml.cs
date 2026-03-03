using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using LanMontainDesktop.Services;

namespace LanMontainDesktop.Views.Components;

public partial class AnalogClockWidget : UserControl, IDesktopComponentWidget, ITimeZoneAwareComponentWidget
{
    private readonly DispatcherTimer _timer = new()
    {
        Interval = TimeSpan.FromSeconds(1)
    };

    private const double DialSize = 258;
    private const double Center = DialSize / 2;

    private TimeZoneService? _timeZoneService;
    private double _currentCellSize = 48;
    private bool _dialInitialized;
    private bool _handsInitialized;
    private bool? _isNightModeApplied;
    private readonly Line _hourHandLine = CreateHandLine("#1A2A46", 12);
    private readonly Line _minuteHandLine = CreateHandLine("#29406B", 8);
    private readonly Line _secondHandLine = CreateHandLine("#1A74F2", 4);

    public AnalogClockWidget()
    {
        InitializeComponent();

        _timer.Tick += OnTimerTick;
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
        SizeChanged += OnSizeChanged;

        InitializeDialIfNeeded();
        InitializeHandsIfNeeded();
        UpdateClock();
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
        if (_timeZoneService is null)
        {
            return;
        }

        _timeZoneService.TimeZoneChanged -= OnTimeZoneChanged;
        _timeZoneService = null;
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        InitializeDialIfNeeded();
        InitializeHandsIfNeeded();
        UpdateClock();
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
        UpdateClock();
    }

    private void OnTimeZoneChanged(object? sender, EventArgs e)
    {
        UpdateClock();
    }

    private void InitializeDialIfNeeded()
    {
        if (_dialInitialized)
        {
            return;
        }

        BuildTicks(isNightMode: true);
        BuildNumbers(isNightMode: true);
        _dialInitialized = true;
    }

    private void InitializeHandsIfNeeded()
    {
        if (_handsInitialized)
        {
            return;
        }

        HandsCanvas.Children.Clear();
        HandsCanvas.Children.Add(_hourHandLine);
        HandsCanvas.Children.Add(_minuteHandLine);
        HandsCanvas.Children.Add(_secondHandLine);
        _handsInitialized = true;
    }

    private void BuildTicks(bool isNightMode)
    {
        TickCanvas.Children.Clear();
        var majorBrush = CreateBrush(isNightMode ? "#1A1A1A" : "#1E2430");
        var minorBrush = CreateBrush(isNightMode ? "#D0D0D0" : "#D7DCE5");
        var majorThickness = isNightMode ? 3.0 : 2.8;
        var minorThickness = isNightMode ? 1.4 : 1.2;

        for (var i = 0; i < 60; i++)
        {
            var angle = (i * 6 - 90) * Math.PI / 180d;
            var isHourTick = i % 5 == 0;
            var outerRadius = Center - 7;
            var innerRadius = outerRadius - (isHourTick ? 16 : 8);

            var x1 = Center + Math.Cos(angle) * innerRadius;
            var y1 = Center + Math.Sin(angle) * innerRadius;
            var x2 = Center + Math.Cos(angle) * outerRadius;
            var y2 = Center + Math.Sin(angle) * outerRadius;

            var tick = new Line
            {
                StartPoint = new Point(x1, y1),
                EndPoint = new Point(x2, y2),
                Stroke = isHourTick ? majorBrush : minorBrush,
                StrokeThickness = isHourTick ? majorThickness : minorThickness,
                StrokeLineCap = PenLineCap.Round
            };

            TickCanvas.Children.Add(tick);
        }
    }

    private void BuildNumbers(bool isNightMode)
    {
        NumberCanvas.Children.Clear();
        var foreground = CreateBrush(isNightMode ? "#101010" : "#0F131A");
        var fontWeight = isNightMode ? FontWeight.Bold : FontWeight.SemiBold;

        for (var number = 1; number <= 12; number++)
        {
            var angle = (number * 30 - 90) * Math.PI / 180d;
            var radius = 88;
            var x = Center + Math.Cos(angle) * radius;
            var y = Center + Math.Sin(angle) * radius;
            var isDoubleDigit = number >= 10;
            var width = isDoubleDigit ? 44 : 28;
            var height = 34;

            var text = new TextBlock
            {
                Text = number.ToString(CultureInfo.InvariantCulture),
                Width = width,
                Height = height,
                TextAlignment = TextAlignment.Center,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                FontSize = 18,
                FontWeight = fontWeight,
                Foreground = foreground
            };

            Canvas.SetLeft(text, x - width / 2d);
            Canvas.SetTop(text, y - height / 2d);
            NumberCanvas.Children.Add(text);
        }
    }

    private void UpdateClock()
    {
        ApplyModeVisualIfNeeded();

        var now = _timeZoneService?.GetCurrentTime() ?? DateTime.Now;
        var hourAngle = (now.Hour % 12 + now.Minute / 60d + now.Second / 3600d) * 30d;
        var minuteAngle = (now.Minute + now.Second / 60d) * 6d;
        var secondAngle = (now.Second + now.Millisecond / 1000d) * 6d;

        SetHandGeometry(_hourHandLine, hourAngle, forwardLength: 52, backwardLength: 6);
        SetHandGeometry(_minuteHandLine, minuteAngle, forwardLength: 76, backwardLength: 8);
        SetHandGeometry(_secondHandLine, secondAngle, forwardLength: 94, backwardLength: 18);

        var isZh = CultureInfo.CurrentCulture.TwoLetterISOLanguageName.Equals("zh", StringComparison.OrdinalIgnoreCase);
        CityTextBlock.Text = isZh ? "\u5317\u4eac" : "Beijing";
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
        RootBorder.Background = isNightMode
            ? CreateLinearGradientBrush("#1F2C4B", "#131B33")
            : CreateLinearGradientBrush("#EEF2FA", "#E7ECF6");

        DialBorder.Background = CreateBrush(isNightMode ? "#F4F4F4" : "#FEFEFF");
        DialBorder.BorderBrush = CreateBrush(isNightMode ? "#E5E5E5" : "#DCE2EB");

        CityTextBlock.Foreground = CreateBrush(isNightMode ? "#757575" : "#7E8593");
        CenterDotOuter.Fill = CreateBrush(isNightMode ? "#1E3C6A" : "#30486E");
        CenterDotInner.Fill = CreateBrush("#1A74F2");

        _hourHandLine.Stroke = CreateBrush(isNightMode ? "#1A2A46" : "#2E3F5F");
        _minuteHandLine.Stroke = CreateBrush(isNightMode ? "#29406B" : "#3E557E");
        _secondHandLine.Stroke = CreateBrush("#1A74F2");

        BuildTicks(isNightMode);
        BuildNumbers(isNightMode);
    }

    private static void SetHandGeometry(Line hand, double angleDeg, double forwardLength, double backwardLength)
    {
        var radians = (angleDeg - 90) * Math.PI / 180d;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);

        var start = new Point(
            Center - cos * backwardLength,
            Center - sin * backwardLength);
        var end = new Point(
            Center + cos * forwardLength,
            Center + sin * forwardLength);

        hand.StartPoint = start;
        hand.EndPoint = end;
    }

    public void ApplyCellSize(double cellSize)
    {
        _currentCellSize = Math.Max(1, cellSize);
        var scale = ResolveScale();

        RootBorder.CornerRadius = new CornerRadius(Math.Clamp(42 * scale, 16, 56));
        RootBorder.Padding = new Thickness(Math.Clamp(14 * scale, 8, 26));
        ApplyModeVisualIfNeeded();
    }

    private double ResolveScale()
    {
        var cellScale = Math.Clamp(_currentCellSize / 44d, 0.60, 1.90);
        var heightScale = Bounds.Height > 1 ? Math.Clamp(Bounds.Height / 300d, 0.58, 2.0) : 1;
        var widthScale = Bounds.Width > 1 ? Math.Clamp(Bounds.Width / 300d, 0.58, 2.0) : 1;
        return Math.Clamp(Math.Min(cellScale, Math.Min(heightScale, widthScale) * 1.05), 0.58, 1.95);
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

    private static Line CreateHandLine(string strokeHex, double thickness)
    {
        return new Line
        {
            StartPoint = new Point(Center, Center),
            EndPoint = new Point(Center, Center - 40),
            Stroke = CreateBrush(strokeHex),
            StrokeThickness = thickness,
            StrokeLineCap = PenLineCap.Round
        };
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
