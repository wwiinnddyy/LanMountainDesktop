using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using LanMountainDesktop.ComponentSystem;
using LanMountainDesktop.Host.Abstractions;
using LanMountainDesktop.Models;
using LanMountainDesktop.Services;

namespace LanMountainDesktop.Views.Components;

public partial class WeatherClockWidget : UserControl, IDesktopComponentWidget, ITimeZoneAwareComponentWidget, IWeatherInfoAwareComponentWidget, IComponentPlacementContextAware, IComponentChromeContextAware
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
    private static readonly IReadOnlyList<int> SupportedAutoRefreshIntervalsMinutes = RefreshIntervalCatalog.SupportedIntervalsMinutes;

    private readonly DispatcherTimer _clockTimer = new()
    {
        Interval = TimeSpan.FromSeconds(1)
    };

    private readonly DispatcherTimer _weatherRefreshTimer = new()
    {
        Interval = TimeSpan.FromMinutes(12)
    };

    private LanMountainDesktop.PluginSdk.ISettingsService _settingsService = LanMountainDesktop.Services.Settings.HostSettingsFacadeProvider.GetOrCreate().Settings;
    private IComponentInstanceSettingsStore _componentSettingsStore = HostComponentSettingsStoreProvider.GetOrCreate();
    private readonly LocalizationService _localizationService = new();
    private readonly Line _hourHandLine = CreateHandLine("#232938", 4.0);
    private readonly Line _minuteHandLine = CreateHandLine("#2F3749", 2.8);
    private readonly Line _secondHandLine = CreateHandLine("#1A74F2", 1.9);

    private IWeatherInfoService _weatherInfoService = DefaultWeatherInfoService;
    private TimeZoneService? _timeZoneService;
    private CancellationTokenSource? _refreshCts;
    private double _currentCellSize = 48;
    private ComponentChromeContext? _chromeContext;
    private bool _isAttached;
    private bool _dialInitialized;
    private bool _handsInitialized;
    private bool _isRefreshing;
    private bool _weatherAutoRefreshEnabled = true;
    private bool? _isNightModeApplied;
    private string _languageCode = "zh-CN";
    private HyperOS3WeatherVisualKind _activeVisualKind = HyperOS3WeatherVisualKind.CloudyDay;
    private string _componentId = BuiltInComponentIds.DesktopWeatherClock;
    private string _placementId = string.Empty;

    public WeatherClockWidget()
    {
        InitializeComponent();

        _clockTimer.Tick += OnClockTimerTick;
        _weatherRefreshTimer.Tick += OnWeatherRefreshTick;
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
        SizeChanged += OnSizeChanged;
        ActualThemeVariantChanged += OnActualThemeVariantChanged;

        InitializeDialIfNeeded();
        InitializeHandsIfNeeded();
        ApplyCellSize(_currentCellSize);
        ApplyDefaultWeatherIcon();
        UpdateClockVisual();
        ApplyAutoRefreshSettings();
    }

    public void SetTimeZoneService(TimeZoneService timeZoneService)
    {
        ClearTimeZoneService();
        _timeZoneService = timeZoneService;
        _timeZoneService.TimeZoneChanged += OnTimeZoneChanged;
        UpdateClockVisual();
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

    public void SetWeatherInfoService(IWeatherInfoService weatherInfoService)
    {
        _weatherInfoService = weatherInfoService ?? DefaultWeatherInfoService;
        if (_isAttached)
        {
            _ = RefreshWeatherAsync(forceRefresh: false);
        }
    }

    public void RefreshFromSettings()
    {
        ApplyAutoRefreshSettings();
        if (_isAttached)
        {
            _ = RefreshWeatherAsync(forceRefresh: true);
        }
    }

    public void SetComponentPlacementContext(string componentId, string? placementId)
    {
        _componentId = string.IsNullOrWhiteSpace(componentId)
            ? BuiltInComponentIds.DesktopWeatherClock
            : componentId.Trim();
        _placementId = placementId?.Trim() ?? string.Empty;
        RefreshFromSettings();
    }

    public void SetComponentChromeContext(ComponentChromeContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _chromeContext = context;
        ApplyCellSize(_currentCellSize);
    }

    public void ApplyCellSize(double cellSize)
    {
        _currentCellSize = Math.Max(1, cellSize);
        var metrics = HyperOS3WeatherTheme.ResolveMetrics(HyperOS3WeatherWidgetKind.WeatherClock2x1);
        var scale = ResolveScale();
        var targetHeight = Bounds.Height > 1
            ? Math.Clamp(Bounds.Height, 38, 160)
            : Math.Clamp(_currentCellSize * 0.92, 38, 120);
        var targetWidth = Bounds.Width > 1
            ? Math.Clamp(Bounds.Width, 48, 520)
            : Math.Clamp(_currentCellSize * 2.15, 88, 260);
        var compactness = Math.Clamp((176 - targetWidth) / 86d, 0, 1);
        var ultraCompact = targetWidth < 126 || targetHeight < 46;
        var compactFactor = Lerp(1, ultraCompact ? 0.64 : 0.72, compactness);
        var mainRectangleCornerRadius = ComponentChromeCornerRadiusHelper.ResolveMainRectangleRadius(_chromeContext);

        var horizontalPadding = ComponentChromeCornerRadiusHelper.SafeValue(
            targetHeight * Lerp(0.18, 0.12, compactness),
            5,
            30,
            _chromeContext);
        var verticalPadding = ComponentChromeCornerRadiusHelper.SafeValue(
            targetHeight * Lerp(0.14, 0.10, compactness),
            3,
            20,
            _chromeContext);

        RootBorder.CornerRadius = mainRectangleCornerRadius;
        RootBorder.Padding = new Thickness(horizontalPadding, verticalPadding, horizontalPadding, verticalPadding);

        var columnSpacing = Math.Clamp(targetHeight * Lerp(0.16, 0.08, compactness), 2, 22);
        LeftStack.Spacing = Math.Clamp(targetHeight * Lerp(0.06, 0.04, compactness), 1.5, 10);
        DateWeatherStack.Spacing = Math.Clamp(targetHeight * Lerp(0.10, 0.06, compactness), 3, 14);

        var contentHeight = Math.Max(24, targetHeight - (verticalPadding * 2));
        var contentWidth = Math.Max(48, targetWidth - (horizontalPadding * 2));
        var minimumLeftWidth = Math.Clamp(contentWidth * Lerp(0.56, 0.64, compactness), ultraCompact ? 34 : 52, 360);
        var maxDialByWidth = Math.Max(0, contentWidth - minimumLeftWidth - columnSpacing);
        var dialByHeight = contentHeight * Lerp(0.94, 0.82, compactness);
        var dialMinSize = ultraCompact ? 14 : 20;
        var dialSize = Math.Min(dialByHeight, maxDialByWidth);
        if (dialSize < dialMinSize && maxDialByWidth >= dialMinSize * 0.8)
        {
            dialSize = dialMinSize;
        }

        dialSize = Math.Clamp(dialSize, 0, 140);
        var showDial = dialSize >= 12;
        if (!showDial)
        {
            dialSize = 0;
            columnSpacing = 0;
        }

        var leftContentWidth = Math.Max(0, contentWidth - (showDial ? dialSize + columnSpacing : 0));
        if (showDial && leftContentWidth < 26)
        {
            var fittedDial = Math.Max(12, Math.Min(dialSize, Math.Max(0, contentWidth - columnSpacing - 26)));
            dialSize = fittedDial;
            leftContentWidth = Math.Max(0, contentWidth - dialSize - columnSpacing);
            if (leftContentWidth < 20)
            {
                showDial = false;
                dialSize = 0;
                columnSpacing = 0;
                leftContentWidth = contentWidth;
            }
        }

        ContentGrid.ColumnSpacing = showDial ? columnSpacing : 0;
        if (ContentGrid.ColumnDefinitions.Count >= 2)
        {
            ContentGrid.ColumnDefinitions[0].Width = new GridLength(leftContentWidth, GridUnitType.Pixel);
            ContentGrid.ColumnDefinitions[1].Width = new GridLength(showDial ? dialSize : 0, GridUnitType.Pixel);
        }

        var timeTextWidth = leftContentWidth * 0.92;
        var timeCharCount = 5;
        var maxTimeFontSize = timeTextWidth / (timeCharCount * 0.58);
        var baseTimeFontSize = Math.Clamp(maxTimeFontSize, 12, 48);
        var timeFontSize = Math.Clamp(baseTimeFontSize * scale * compactFactor, 10, 48);
        TimeTextBlock.FontSize = timeFontSize;
        
        var dateFontSize = Math.Clamp(timeFontSize * 0.48, 8, 22);
        DateTextBlock.FontSize = dateFontSize;
        
        var weatherIconSize = Math.Clamp(dateFontSize * 1.1, 10, 24);
        WeatherIconImage.Width = weatherIconSize;
        WeatherIconImage.Height = weatherIconSize;

        TimeTextBlock.FontWeight = ToVariableWeight(Lerp(620, 760, Math.Clamp((scale - 0.68) / 1.35, 0, 1)));
        DateTextBlock.FontWeight = ToVariableWeight(Lerp(540, 680, Math.Clamp((scale - 0.68) / 1.35, 0, 1)));

        LeftStack.Width = leftContentWidth;
        LeftStack.MaxWidth = leftContentWidth;
        DateWeatherStack.MaxWidth = leftContentWidth;
        TimeTextBlock.MaxWidth = timeTextWidth;

        var showDateLine = leftContentWidth >= Math.Max(36, timeFontSize * 1.4) && contentHeight >= 38;
        DateWeatherStack.IsVisible = showDateLine;
        WeatherIconImage.IsVisible = showDateLine && leftContentWidth >= Math.Max(48, dateFontSize * 3.2);

        var dateReservedWidth = WeatherIconImage.IsVisible
            ? weatherIconSize + DateWeatherStack.Spacing
            : 0;
        DateTextBlock.MaxWidth = Math.Max(12, leftContentWidth - dateReservedWidth);

        AnalogDialBorder.IsVisible = showDial;
        AnalogDialBorder.Width = dialSize;
        AnalogDialBorder.Height = dialSize;
        AnalogDialBorder.CornerRadius = new CornerRadius(dialSize / 2d);

        ApplyModeVisualIfNeeded();
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _isAttached = true;
        ApplyAutoRefreshSettings();
        UpdateClockVisual();
        _clockTimer.Start();
        UpdateWeatherRefreshTimerState();
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

    private void OnActualThemeVariantChanged(object? sender, EventArgs e)
    {
        _isNightModeApplied = null;
        ApplyModeVisualIfNeeded();
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
        _activeVisualKind = HyperOS3WeatherTheme.ResolveVisualKind(snapshot.Current.WeatherCode, isNight);
        WeatherIconImage.Source = HyperOS3WeatherAssetLoader.LoadImage(
            HyperOS3WeatherTheme.ResolveIconAsset(_activeVisualKind));
    }

    private void ApplyDefaultWeatherIcon()
    {
        var isNight = IsNightNow();
        _activeVisualKind = isNight ? HyperOS3WeatherVisualKind.ClearNight : HyperOS3WeatherVisualKind.CloudyDay;
        WeatherIconImage.Source = HyperOS3WeatherAssetLoader.LoadImage(
            HyperOS3WeatherTheme.ResolveIconAsset(_activeVisualKind));
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
        var gradientFrom = isNightMode ? "#2A3346" : "#FFFFFF";
        var gradientTo = isNightMode ? "#202A3B" : "#F6F8FC";
        var dialSurface = isNightMode ? "#1B2434" : "#F8FAFF";
        var backgroundSamples = WeatherTypographyAccessibility.BuildBackgroundSamples(
            gradientFrom,
            gradientTo,
            dialSurface,
            isNightMode);

        RootBorder.Background = CreateGradientBrush(gradientFrom, gradientTo);
        RootBorder.BorderBrush = CreateBrush(isNightMode ? "#36F2F5FF" : "#14000000");

        AnalogDialBorder.Background = isNightMode
            ? CreateBrush("#1B2434")
            : CreateBrush("#F8FAFF");
        AnalogDialBorder.BorderBrush = CreateBrush(isNightMode ? "#34DDE7FF" : "#12000000");

        if (isNightMode)
        {
            TimeTextBlock.Foreground = WeatherTypographyAccessibility.CreateReadableBrush(
                "#F8FBFF",
                backgroundSamples,
                WeatherTypographyAccessibility.WcagLargeTextContrast);
            DateTextBlock.Foreground = WeatherTypographyAccessibility.CreateReadableBrush(
                "#BCC8DD",
                backgroundSamples,
                WeatherTypographyAccessibility.WcagNormalTextContrast);
        }
        else
        {
            TimeTextBlock.Foreground = CreateBrush("#10131A");
            DateTextBlock.Foreground = CreateBrush("#7A7E87");
        }

        _hourHandLine.Stroke = CreateBrush(isNightMode ? "#F1F5FF" : "#232938");
        _minuteHandLine.Stroke = CreateBrush(isNightMode ? "#D6E0F2" : "#2F3749");
        _secondHandLine.Stroke = CreateBrush("#1A74F2");
        CenterDotOuter.Fill = CreateBrush(isNightMode ? "#7BAAE8" : "#4F7CC0");
        CenterDotInner.Fill = CreateBrush("#1A74F2");

        BuildTicks(isNightMode);
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
        return HyperOS3WeatherTheme.ResolveIsNightPreferred(
            snapshot,
            _timeZoneService?.CurrentTimeZone,
            _timeZoneService?.GetCurrentTime() ?? DateTime.Now);
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

    private void ApplyAutoRefreshSettings()
    {
        var enabled = true;
        var intervalMinutes = 12;

        try
        {
            var snapshot = _componentSettingsStore.LoadForComponent(_componentId, _placementId);
            enabled = snapshot.WeatherAutoRefreshEnabled;
            intervalMinutes = NormalizeAutoRefreshIntervalMinutes(snapshot.WeatherAutoRefreshIntervalMinutes);
        }
        catch
        {
            // Keep fallback defaults.
        }

        _weatherAutoRefreshEnabled = enabled;
        _weatherRefreshTimer.Interval = TimeSpan.FromMinutes(intervalMinutes);
        UpdateWeatherRefreshTimerState();
    }

    private void UpdateWeatherRefreshTimerState()
    {
        if (_isAttached && _weatherAutoRefreshEnabled)
        {
            if (!_weatherRefreshTimer.IsEnabled)
            {
                _weatherRefreshTimer.Start();
            }

            return;
        }

        _weatherRefreshTimer.Stop();
    }

    private static int NormalizeAutoRefreshIntervalMinutes(int minutes)
    {
        if (minutes <= 0)
        {
            return 12;
        }

        if (SupportedAutoRefreshIntervalsMinutes.Contains(minutes))
        {
            return minutes;
        }

        return SupportedAutoRefreshIntervalsMinutes
            .OrderBy(value => Math.Abs(value - minutes))
            .FirstOrDefault(12);
    }

    private void CancelRefreshRequest()
    {
        var cts = Interlocked.Exchange(ref _refreshCts, null);
        cts?.Cancel();
        cts?.Dispose();
    }
}
