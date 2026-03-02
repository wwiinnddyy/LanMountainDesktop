using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using FluentIcons.Common;
using LanMontainDesktop.Models;
using LanMontainDesktop.Services;

namespace LanMontainDesktop.Views.Components;

public partial class WeatherClockWidget : UserControl, IDesktopComponentWidget, ITimeZoneAwareComponentWidget, IWeatherInfoAwareComponentWidget
{
    private sealed record WeatherClockConfig(
        string LanguageCode,
        string Locale,
        string LocationKey,
        double Latitude,
        double Longitude);

    private const double DialDesignSize = 104;
    private const double DialCenter = DialDesignSize / 2d;

    private static readonly IWeatherInfoService DefaultWeatherInfoService = new XiaomiWeatherService();

    private readonly DispatcherTimer _clockTimer = new()
    {
        Interval = TimeSpan.FromSeconds(1)
    };

    private readonly DispatcherTimer _weatherRefreshTimer = new()
    {
        Interval = TimeSpan.FromMinutes(12)
    };

    private readonly AppSettingsService _settingsService = new();
    private readonly LocalizationService _localizationService = new();
    private readonly Line _hourHandLine = CreateHandLine("#232938", 4.0);
    private readonly Line _minuteHandLine = CreateHandLine("#2F3749", 2.8);
    private readonly Line _secondHandLine = CreateHandLine("#1A74F2", 1.9);

    private IWeatherInfoService _weatherInfoService = DefaultWeatherInfoService;
    private TimeZoneService? _timeZoneService;
    private CancellationTokenSource? _refreshCts;
    private double _currentCellSize = 48;
    private bool _isAttached;
    private bool _dialInitialized;
    private bool _handsInitialized;
    private bool _isRefreshing;
    private bool? _isNightModeApplied;
    private string _languageCode = "zh-CN";
    private Symbol _activeWeatherSymbol = Symbol.WeatherPartlyCloudyDay;

    public WeatherClockWidget()
    {
        InitializeComponent();

        _clockTimer.Tick += OnClockTimerTick;
        _weatherRefreshTimer.Tick += OnWeatherRefreshTick;
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
        SizeChanged += OnSizeChanged;

        InitializeDialIfNeeded();
        InitializeHandsIfNeeded();
        ApplyCellSize(_currentCellSize);
        ApplyDefaultWeatherIcon();
        UpdateClockVisual();
    }

    public void SetTimeZoneService(TimeZoneService timeZoneService)
    {
        if (_timeZoneService is not null)
        {
            _timeZoneService.TimeZoneChanged -= OnTimeZoneChanged;
        }

        _timeZoneService = timeZoneService;
        _timeZoneService.TimeZoneChanged += OnTimeZoneChanged;
        UpdateClockVisual();
    }

    public void SetWeatherInfoService(IWeatherInfoService weatherInfoService)
    {
        _weatherInfoService = weatherInfoService ?? DefaultWeatherInfoService;
        if (_isAttached)
        {
            _ = RefreshWeatherAsync(forceRefresh: false);
        }
    }

    public void ApplyCellSize(double cellSize)
    {
        _currentCellSize = Math.Max(1, cellSize);
        var scale = ResolveScale();
        var targetHeight = Bounds.Height > 1
            ? Math.Clamp(Bounds.Height, 38, 160)
            : Math.Clamp(_currentCellSize * 0.92, 38, 120);
        var targetWidth = Bounds.Width > 1
            ? Math.Clamp(Bounds.Width, 48, 520)
            : Math.Clamp(_currentCellSize * 2.15, 88, 260);
        var compactness = Math.Clamp((170 - targetWidth) / 78d, 0, 1);
        var compactFactor = Lerp(1, 0.72, compactness);
        var cornerRadius = Math.Clamp(targetHeight * 0.40, 15, 36);

        var horizontalPadding = Math.Clamp(targetHeight * Lerp(0.18, 0.12, compactness), 5, 30);
        var verticalPadding = Math.Clamp(targetHeight * Lerp(0.14, 0.10, compactness), 3, 20);

        RootBorder.CornerRadius = new CornerRadius(cornerRadius);
        RootBorder.Padding = new Thickness(horizontalPadding, verticalPadding, horizontalPadding, verticalPadding);

        var columnSpacing = Math.Clamp(targetHeight * Lerp(0.16, 0.08, compactness), 3, 22);
        ContentGrid.ColumnSpacing = columnSpacing;
        LeftStack.Spacing = Math.Clamp(targetHeight * Lerp(0.06, 0.04, compactness), 1.5, 10);
        DateWeatherStack.Spacing = Math.Clamp(targetHeight * Lerp(0.10, 0.06, compactness), 3, 14);

        TimeTextBlock.FontSize = Math.Clamp(31 * scale * compactFactor, 14, 62);
        DateTextBlock.FontSize = Math.Clamp(15.5 * scale * compactFactor, 9, 30);
        WeatherIconSymbol.FontSize = Math.Clamp(17 * scale * compactFactor, 10, 32);

        TimeTextBlock.FontWeight = ToVariableWeight(Lerp(620, 760, Math.Clamp((scale - 0.68) / 1.35, 0, 1)));
        DateTextBlock.FontWeight = ToVariableWeight(Lerp(540, 680, Math.Clamp((scale - 0.68) / 1.35, 0, 1)));

        var contentHeight = Math.Max(24, targetHeight - (verticalPadding * 2));
        var contentWidth = Math.Max(48, targetWidth - (horizontalPadding * 2));
        var minimumLeftWidth = Math.Clamp(contentWidth * Lerp(0.56, 0.64, compactness), 52, 360);
        var maxDialByWidth = Math.Max(18, contentWidth - minimumLeftWidth - columnSpacing);
        var dialByHeight = contentHeight * Lerp(0.94, 0.84, compactness);
        var dialSize = Math.Clamp(Math.Min(dialByHeight, maxDialByWidth), 20, 140);
        var leftContentWidth = Math.Max(26, contentWidth - dialSize - columnSpacing);

        LeftStack.MaxWidth = leftContentWidth;
        DateWeatherStack.MaxWidth = leftContentWidth;
        TimeTextBlock.MaxWidth = leftContentWidth;
        DateTextBlock.MaxWidth = Math.Max(18, leftContentWidth - WeatherIconSymbol.FontSize - DateWeatherStack.Spacing);

        AnalogDialBorder.Width = dialSize;
        AnalogDialBorder.Height = dialSize;
        AnalogDialBorder.CornerRadius = new CornerRadius(dialSize / 2d);

        ApplyModeVisualIfNeeded();
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _isAttached = true;
        UpdateClockVisual();
        _clockTimer.Start();
        _weatherRefreshTimer.Start();
        _ = RefreshWeatherAsync(forceRefresh: false);
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _isAttached = false;
        _clockTimer.Stop();
        _weatherRefreshTimer.Stop();
        CancelRefreshRequest();
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        ApplyCellSize(_currentCellSize);
    }

    private void OnClockTimerTick(object? sender, EventArgs e)
    {
        UpdateClockVisual();
    }

    private async void OnWeatherRefreshTick(object? sender, EventArgs e)
    {
        await RefreshWeatherAsync(forceRefresh: false);
    }

    private void OnTimeZoneChanged(object? sender, EventArgs e)
    {
        UpdateClockVisual();
    }

    private async Task RefreshWeatherAsync(bool forceRefresh)
    {
        if (!_isAttached || _isRefreshing)
        {
            return;
        }

        _isRefreshing = true;
        var config = LoadConfig();
        _languageCode = config.LanguageCode;

        if (string.IsNullOrWhiteSpace(config.LocationKey))
        {
            ApplyDefaultWeatherIcon();
            _isRefreshing = false;
            UpdateClockVisual();
            return;
        }

        var cts = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _refreshCts, cts);
        previous?.Cancel();
        previous?.Dispose();

        try
        {
            var query = new WeatherQuery(
                LocationKey: config.LocationKey,
                Latitude: config.Latitude,
                Longitude: config.Longitude,
                ForecastDays: 1,
                Locale: config.Locale,
                ForceRefresh: forceRefresh);

            var result = await _weatherInfoService.GetWeatherAsync(query, cts.Token);
            if (cts.IsCancellationRequested || !_isAttached)
            {
                return;
            }

            if (!result.Success || result.Data is null)
            {
                ApplyDefaultWeatherIcon();
                return;
            }

            ApplyWeatherSnapshot(result.Data);
        }
        catch (OperationCanceledException)
        {
            // Ignore canceled refresh requests.
        }
        catch
        {
            if (!cts.IsCancellationRequested && _isAttached)
            {
                ApplyDefaultWeatherIcon();
            }
        }
        finally
        {
            if (ReferenceEquals(_refreshCts, cts))
            {
                _refreshCts = null;
            }

            cts.Dispose();
            _isRefreshing = false;
        }
    }

    private void ApplyWeatherSnapshot(WeatherSnapshot snapshot)
    {
        var isNight = ResolveIsNight(snapshot);
        _activeWeatherSymbol = ResolveWeatherSymbol(snapshot.Current.WeatherCode, isNight);
        WeatherIconSymbol.Symbol = _activeWeatherSymbol;
        WeatherIconSymbol.Foreground = CreateBrush(ResolveWeatherIconColor(_activeWeatherSymbol, isNight));
    }

    private void ApplyDefaultWeatherIcon()
    {
        var isNight = IsNightNow();
        _activeWeatherSymbol = isNight ? Symbol.WeatherMoon : Symbol.WeatherPartlyCloudyDay;
        WeatherIconSymbol.Symbol = _activeWeatherSymbol;
        WeatherIconSymbol.Foreground = CreateBrush(ResolveWeatherIconColor(_activeWeatherSymbol, isNight));
    }

    private void UpdateClockVisual()
    {
        ApplyModeVisualIfNeeded();

        var now = _timeZoneService?.GetCurrentTime() ?? DateTime.Now;
        TimeTextBlock.Text = now.ToString("HH:mm", CultureInfo.CurrentCulture);
        DateTextBlock.Text = FormatDate(now);

        var hourAngle = (now.Hour % 12 + now.Minute / 60d + now.Second / 3600d) * 30d;
        var minuteAngle = (now.Minute + now.Second / 60d) * 6d;
        var secondAngle = (now.Second + now.Millisecond / 1000d) * 6d;

        SetHandGeometry(_hourHandLine, hourAngle, forwardLength: 23.5, backwardLength: 5.0);
        SetHandGeometry(_minuteHandLine, minuteAngle, forwardLength: 33.5, backwardLength: 6.5);
        SetHandGeometry(_secondHandLine, secondAngle, forwardLength: 39.0, backwardLength: 10.0);
    }

    private void InitializeDialIfNeeded()
    {
        if (_dialInitialized)
        {
            return;
        }

        BuildTicks(isNightMode: false);
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
        var tickColor = isNightMode ? "#CED7EA" : "#1C2333";

        for (var i = 0; i < 12; i++)
        {
            var angle = (i * 30 - 90) * Math.PI / 180d;
            var isMajor = i % 3 == 0;
            var outerRadius = DialCenter - 8;
            var innerRadius = outerRadius - (isMajor ? 13.5 : 9.5);

            var x1 = DialCenter + Math.Cos(angle) * innerRadius;
            var y1 = DialCenter + Math.Sin(angle) * innerRadius;
            var x2 = DialCenter + Math.Cos(angle) * outerRadius;
            var y2 = DialCenter + Math.Sin(angle) * outerRadius;

            TickCanvas.Children.Add(new Line
            {
                StartPoint = new Point(x1, y1),
                EndPoint = new Point(x2, y2),
                Stroke = CreateBrush(tickColor),
                StrokeThickness = isMajor ? 2.8 : 1.9,
                StrokeLineCap = PenLineCap.Round
            });
        }
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
            ? CreateGradientBrush("#2A3346", "#202A3B")
            : CreateGradientBrush("#FFFFFF", "#F6F8FC");
        RootBorder.BorderBrush = CreateBrush(isNightMode ? "#36F2F5FF" : "#14000000");

        AnalogDialBorder.Background = isNightMode
            ? CreateBrush("#1B2434")
            : CreateBrush("#F8FAFF");
        AnalogDialBorder.BorderBrush = CreateBrush(isNightMode ? "#34DDE7FF" : "#12000000");

        TimeTextBlock.Foreground = CreateBrush(isNightMode ? "#F8FBFF" : "#10131A");
        DateTextBlock.Foreground = CreateBrush(isNightMode ? "#BCC8DD" : "#7A7E87");

        _hourHandLine.Stroke = CreateBrush(isNightMode ? "#F1F5FF" : "#232938");
        _minuteHandLine.Stroke = CreateBrush(isNightMode ? "#D6E0F2" : "#2F3749");
        _secondHandLine.Stroke = CreateBrush("#1A74F2");
        CenterDotOuter.Fill = CreateBrush(isNightMode ? "#7BAAE8" : "#4F7CC0");
        CenterDotInner.Fill = CreateBrush("#1A74F2");

        BuildTicks(isNightMode);
        WeatherIconSymbol.Foreground = CreateBrush(ResolveWeatherIconColor(_activeWeatherSymbol, isNightMode));
    }

    private WeatherClockConfig LoadConfig()
    {
        var snapshot = _settingsService.Load();
        var languageCode = _localizationService.NormalizeLanguageCode(snapshot.LanguageCode);
        var locale = string.Equals(languageCode, "zh-CN", StringComparison.OrdinalIgnoreCase)
            ? "zh_cn"
            : "en_us";

        var latitude = NormalizeLatitude(snapshot.WeatherLatitude);
        var longitude = NormalizeLongitude(snapshot.WeatherLongitude);
        var locationKey = snapshot.WeatherLocationKey?.Trim() ?? string.Empty;

        var modeIsCoordinates = string.Equals(
            snapshot.WeatherLocationMode,
            "Coordinates",
            StringComparison.OrdinalIgnoreCase);
        if (modeIsCoordinates && string.IsNullOrWhiteSpace(locationKey))
        {
            locationKey = BuildCoordinateLocationKey(latitude, longitude);
        }

        return new WeatherClockConfig(
            LanguageCode: languageCode,
            Locale: locale,
            LocationKey: locationKey,
            Latitude: latitude,
            Longitude: longitude);
    }

    private string FormatDate(DateTime dateTime)
    {
        var isZh = string.Equals(_languageCode, "zh-CN", StringComparison.OrdinalIgnoreCase);
        if (isZh)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{dateTime.Month}\u6708{dateTime.Day}\u65e5");
        }

        try
        {
            var culture = CultureInfo.GetCultureInfo(_languageCode);
            return dateTime.ToString("MMM d", culture);
        }
        catch
        {
            return dateTime.ToString("MMM d", CultureInfo.InvariantCulture);
        }
    }

    private double ResolveScale()
    {
        var cellScale = Math.Clamp(_currentCellSize / 44d, 0.60, 2.20);
        var heightScale = Bounds.Height > 1 ? Math.Clamp(Bounds.Height / 56d, 0.65, 2.80) : 1;
        var widthScale = Bounds.Width > 1 ? Math.Clamp(Bounds.Width / 180d, 0.65, 2.80) : 1;
        return Math.Clamp(Math.Min(heightScale, widthScale) * 1.02 * cellScale, 0.62, 2.40);
    }

    private bool ResolveIsNight(WeatherSnapshot snapshot)
    {
        if (snapshot.ObservationTime.HasValue)
        {
            var observed = snapshot.ObservationTime.Value;
            try
            {
                if (_timeZoneService is not null)
                {
                    var zoned = TimeZoneInfo.ConvertTime(observed, _timeZoneService.CurrentTimeZone);
                    return zoned.Hour < 6 || zoned.Hour >= 18;
                }
            }
            catch
            {
                // Fall through to local observation.
            }

            return observed.Hour < 6 || observed.Hour >= 18;
        }

        return IsNightNow();
    }

    private bool IsNightNow()
    {
        var now = _timeZoneService?.GetCurrentTime() ?? DateTime.Now;
        return now.Hour < 6 || now.Hour >= 18;
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

    private static Symbol ResolveWeatherSymbol(int? weatherCode, bool isNight)
    {
        return weatherCode switch
        {
            0 => isNight ? Symbol.WeatherMoon : Symbol.WeatherSunny,
            1 or 2 => isNight ? Symbol.WeatherPartlyCloudyNight : Symbol.WeatherPartlyCloudyDay,
            3 or 7 => Symbol.WeatherRainShowersDay,
            8 or 9 => Symbol.WeatherRain,
            4 => Symbol.WeatherThunderstorm,
            13 or 14 or 15 or 16 => Symbol.WeatherSnow,
            18 or 32 => Symbol.WeatherFog,
            _ => isNight ? Symbol.WeatherPartlyCloudyNight : Symbol.WeatherPartlyCloudyDay
        };
    }

    private static string ResolveWeatherIconColor(Symbol symbol, bool isNightMode)
    {
        return symbol switch
        {
            Symbol.WeatherSunny => isNightMode ? "#FFD978" : "#F7B500",
            Symbol.WeatherMoon => "#F6D98F",
            Symbol.WeatherPartlyCloudyDay => "#5A9CFF",
            Symbol.WeatherPartlyCloudyNight => "#8AB6FF",
            Symbol.WeatherRainShowersDay => "#5F96E8",
            Symbol.WeatherRain => "#4B84DA",
            Symbol.WeatherThunderstorm => "#F1C24D",
            Symbol.WeatherSnow => "#8EBFE5",
            _ => isNightMode ? "#A9BDD7" : "#93A2B8"
        };
    }

    private static void SetHandGeometry(Line hand, double angleDeg, double forwardLength, double backwardLength)
    {
        var radians = (angleDeg - 90) * Math.PI / 180d;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);

        hand.StartPoint = new Point(
            DialCenter - (cos * backwardLength),
            DialCenter - (sin * backwardLength));
        hand.EndPoint = new Point(
            DialCenter + (cos * forwardLength),
            DialCenter + (sin * forwardLength));
    }

    private static Line CreateHandLine(string colorHex, double thickness)
    {
        return new Line
        {
            StartPoint = new Point(DialCenter, DialCenter),
            EndPoint = new Point(DialCenter, DialCenter - 32),
            Stroke = CreateBrush(colorHex),
            StrokeThickness = thickness,
            StrokeLineCap = PenLineCap.Round
        };
    }

    private static IBrush CreateBrush(string colorHex)
    {
        return new SolidColorBrush(Color.Parse(colorHex));
    }

    private static IBrush CreateGradientBrush(string fromHex, string toHex)
    {
        return new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops = new GradientStops
            {
                new GradientStop(Color.Parse(fromHex), 0),
                new GradientStop(Color.Parse(toHex), 1)
            }
        };
    }

    private static double Lerp(double from, double to, double t)
    {
        return from + ((to - from) * t);
    }

    private static FontWeight ToVariableWeight(double weight)
    {
        return (FontWeight)(int)Math.Clamp(Math.Round(weight), 1, 1000);
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

    private static string BuildCoordinateLocationKey(double latitude, double longitude)
    {
        return string.Create(CultureInfo.InvariantCulture, $"coord:{latitude:F4},{longitude:F4}");
    }

    private static double NormalizeLatitude(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return 39.9042;
        }

        return Math.Clamp(value, -90, 90);
    }

    private static double NormalizeLongitude(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return 116.4074;
        }

        return Math.Clamp(value, -180, 180);
    }

    private void CancelRefreshRequest()
    {
        var cts = Interlocked.Exchange(ref _refreshCts, null);
        cts?.Cancel();
        cts?.Dispose();
    }
}
