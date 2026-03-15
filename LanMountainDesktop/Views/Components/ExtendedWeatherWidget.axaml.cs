using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using LanMountainDesktop.ComponentSystem;
using LanMountainDesktop.Models;
using LanMountainDesktop.PluginSdk;
using LanMountainDesktop.Services;
using LanMountainDesktop.Services.Settings;
using LanMountainDesktop.Theme;

namespace LanMountainDesktop.Views.Components;

public partial class ExtendedWeatherWidget : UserControl, IDesktopComponentWidget, IDesktopPageVisibilityAwareComponentWidget, ITimeZoneAwareComponentWidget, IWeatherInfoAwareComponentWidget, IComponentPlacementContextAware
{
    private static readonly IWeatherInfoService DefaultWeatherInfoService = new XiaomiWeatherService();
    private static readonly IReadOnlyList<int> SupportedAutoRefreshIntervalsMinutes = RefreshIntervalCatalog.SupportedIntervalsMinutes;

    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromMinutes(12) };
    private readonly DispatcherTimer _animationTimer = new() { Interval = FluttermotionToken.WeatherAnimationFrameInterval };
    private readonly ScaleTransform _backgroundMotionScaleTransform = new(1, 1);
    private readonly TranslateTransform _backgroundMotionTranslateTransform = new();
    private ISettingsService _settingsService = HostSettingsFacadeProvider.GetOrCreate().Settings;
    private readonly LocalizationService _localizationService = new();

    private IWeatherInfoService _weatherInfoService = DefaultWeatherInfoService;
    private TimeZoneService? _timeZoneService;
    private CancellationTokenSource? _refreshCts;
    private double _currentCellSize = 48;
    private double _phase;
    private bool _isAttached;
    private bool _isOnActivePage = true;
    private bool _isRefreshing;
    private bool _autoRefreshEnabled = true;
    private string _languageCode = "zh-CN";
    private HyperOS3WeatherVisualKind _activeVisualKind = HyperOS3WeatherVisualKind.ClearDay;
    private string _componentId = BuiltInComponentIds.DesktopExtendedWeather;
    private string _placementId = string.Empty;
    private readonly TextBlock[] _hourlyTempBlocks;
    private readonly TextBlock[] _hourlyTimeBlocks;
    private readonly Image[] _hourlyIconBlocks;
    private readonly TextBlock[] _dailyLabelBlocks;
    private readonly TextBlock[] _dailyHighBlocks;
    private readonly TextBlock[] _dailyLowBlocks;
    private readonly Image[] _dailyIconBlocks;
    private readonly HyperOS3WeatherVisualKind[] _dailyIconKinds;

    public ExtendedWeatherWidget()
    {
        InitializeComponent();
        InitializeMotionTransform();
        _hourlyTempBlocks =
        [
            HourlyTemp0, HourlyTemp1, HourlyTemp2, HourlyTemp3, HourlyTemp4, HourlyTemp5
        ];
        _hourlyTimeBlocks =
        [
            HourlyTime0, HourlyTime1, HourlyTime2, HourlyTime3, HourlyTime4, HourlyTime5
        ];
        _hourlyIconBlocks =
        [
            HourlyIcon0, HourlyIcon1, HourlyIcon2, HourlyIcon3, HourlyIcon4, HourlyIcon5
        ];
        _dailyLabelBlocks =
        [
            DailyLabel0, DailyLabel1, DailyLabel2, DailyLabel3, DailyLabel4
        ];
        _dailyHighBlocks =
        [
            DailyHigh0, DailyHigh1, DailyHigh2, DailyHigh3, DailyHigh4
        ];
        _dailyLowBlocks =
        [
            DailyLow0, DailyLow1, DailyLow2, DailyLow3, DailyLow4
        ];
        _dailyIconBlocks =
        [
            DailyIcon0, DailyIcon1, DailyIcon2, DailyIcon3, DailyIcon4
        ];
        _dailyIconKinds = Enumerable.Repeat(HyperOS3WeatherVisualKind.Unknown, _dailyIconBlocks.Length).ToArray();
        ConfigureTextOverflowGuards();
        _refreshTimer.Tick += OnRefreshTimerTick;
        _animationTimer.Tick += OnAnimationTick;
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
        SizeChanged += OnSizeChanged;
        ApplyCellSize(_currentCellSize);
        ApplyVisualTheme(_activeVisualKind);
        ApplyFallback();
        ApplyAutoRefreshSettings();
    }

    private void ConfigureTextOverflowGuards()
    {
        CityTextBlock.TextWrapping = TextWrapping.NoWrap;
        CityTextBlock.TextTrimming = TextTrimming.CharacterEllipsis;
        CityTextBlock.MaxLines = 1;

        ConditionTextBlock.TextWrapping = TextWrapping.NoWrap;
        ConditionTextBlock.TextTrimming = TextTrimming.CharacterEllipsis;
        ConditionTextBlock.MaxLines = 1;

        RangeTextBlock.TextWrapping = TextWrapping.NoWrap;
        RangeTextBlock.TextTrimming = TextTrimming.CharacterEllipsis;
        RangeTextBlock.MaxLines = 1;

        TemperatureTextBlock.TextWrapping = TextWrapping.NoWrap;
        TemperatureTextBlock.TextTrimming = TextTrimming.None;
        TemperatureTextBlock.MaxLines = 1;
    }

    public void ApplyCellSize(double cellSize)
    {
        _currentCellSize = Math.Max(1, cellSize);
        var metrics = HyperOS3WeatherTheme.ResolveMetrics(HyperOS3WeatherWidgetKind.Extended4x4);
        var width = Bounds.Width > 1 ? Bounds.Width : _currentCellSize * 4;
        var height = Bounds.Height > 1 ? Bounds.Height : _currentCellSize * 4;
        var radius = Math.Clamp(_currentCellSize * metrics.CornerRadiusScale, 28, 54);
        RootBorder.CornerRadius = new CornerRadius(radius);
        BackgroundImageLayer.CornerRadius = new CornerRadius(radius);
        BackgroundMotionLayer.CornerRadius = new CornerRadius(radius);
        BackgroundTintLayer.CornerRadius = new CornerRadius(radius);
        BackgroundLightLayer.CornerRadius = new CornerRadius(radius);
        BackgroundShadeLayer.CornerRadius = new CornerRadius(radius);
        var horizontalPadding = Math.Clamp(Math.Min(width * metrics.HorizontalPaddingScale * 0.30, width * 0.11), 4, 34);
        var verticalPadding = Math.Clamp(Math.Min(height * metrics.VerticalPaddingScale * 0.30, height * 0.11), 4, 34);
        ContentPaddingBorder.Padding = new Thickness(
            horizontalPadding,
            verticalPadding);
        ApplyTypography(width, height);
    }

    public void SetTimeZoneService(TimeZoneService timeZoneService)
    {
        if (_timeZoneService is not null)
        {
            _timeZoneService.TimeZoneChanged -= OnTimeZoneChanged;
        }

        _timeZoneService = timeZoneService;
        _timeZoneService.TimeZoneChanged += OnTimeZoneChanged;
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
        if (_isAttached && _isOnActivePage)
        {
            _ = RefreshWeatherAsync(false);
        }
    }

    public void RefreshFromSettings()
    {
        ApplyAutoRefreshSettings();
        if (_isAttached && _isOnActivePage)
        {
            _ = RefreshWeatherAsync(forceRefresh: true);
        }
    }

    public void SetComponentPlacementContext(string componentId, string? placementId)
    {
        _componentId = string.IsNullOrWhiteSpace(componentId)
            ? BuiltInComponentIds.DesktopExtendedWeather
            : componentId.Trim();
        _placementId = placementId?.Trim() ?? string.Empty;
        RefreshFromSettings();
    }

    public void SetDesktopPageContext(bool isOnActivePage, bool isEditMode)
    {
        _ = isEditMode;
        var wasOnActivePage = _isOnActivePage;
        _isOnActivePage = isOnActivePage;
        UpdateTimerState();

        if (!wasOnActivePage && _isOnActivePage && _isAttached)
        {
            _ = RefreshWeatherAsync(false);
        }
    }

    private void OnTimeZoneChanged(object? sender, EventArgs e)
    {
        if (_isAttached && _isOnActivePage)
        {
            _ = RefreshWeatherAsync(false);
        }
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _isAttached = true;
        ApplyAutoRefreshSettings();
        UpdateTimerState();
        if (_isOnActivePage)
        {
            _ = RefreshWeatherAsync(false);
        }
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _isAttached = false;
        UpdateTimerState();
        CancelRefresh();
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        ApplyCellSize(_currentCellSize);
    }

    private async void OnRefreshTimerTick(object? sender, EventArgs e)
    {
        await RefreshWeatherAsync(false);
    }

    private void OnAnimationTick(object? sender, EventArgs e)
    {
        if (!_isAttached || !_isOnActivePage)
        {
            return;
        }

        _phase += 0.018;
        if (_phase > Math.PI * 2) _phase -= Math.PI * 2;
        var sin = Math.Sin(_phase);
        var cos = Math.Cos(_phase * 0.83);
        SetMotionTransform(sin * 7.0, cos * 5.0, 1.05 + (sin * 0.01));
        BackgroundMotionLayer.Opacity = Math.Clamp(0.27 + (cos * 0.05), 0.10, 0.90);
        BackgroundLightLayer.Opacity = Math.Clamp(0.62 + (sin * 0.06), 0.20, 0.95);
        BackgroundShadeLayer.Opacity = Math.Clamp(0.80 + (cos * 0.03), 0.45, 0.95);
    }

    private async Task RefreshWeatherAsync(bool forceRefresh)
    {
        if (!_isAttached || !_isOnActivePage || _isRefreshing)
        {
            return;
        }

        _isRefreshing = true;
        var app = _settingsService.LoadSnapshot<AppSettingsSnapshot>(SettingsScope.App);
        _languageCode = _localizationService.NormalizeLanguageCode(app.LanguageCode);
        var locale = string.Equals(_languageCode, "zh-CN", StringComparison.OrdinalIgnoreCase) ? "zh_cn" : "en_us";
        var latitude = double.IsFinite(app.WeatherLatitude) ? Math.Clamp(app.WeatherLatitude, -90, 90) : 39.9042;
        var longitude = double.IsFinite(app.WeatherLongitude) ? Math.Clamp(app.WeatherLongitude, -180, 180) : 116.4074;
        var locationKey = (app.WeatherLocationKey ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(locationKey) && string.Equals(app.WeatherLocationMode, "Coordinates", StringComparison.OrdinalIgnoreCase))
        {
            locationKey = string.Create(CultureInfo.InvariantCulture, $"coord:{latitude:F4},{longitude:F4}");
        }

        if (string.IsNullOrWhiteSpace(locationKey))
        {
            ApplyFallback();
            _isRefreshing = false;
            return;
        }

        SetLoadingSkeleton(true);

        var cts = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _refreshCts, cts);
        previous?.Cancel();
        previous?.Dispose();

        try
        {
            var query = new WeatherQuery(locationKey, latitude, longitude, 7, locale, ForceRefresh: forceRefresh);
            var result = await _weatherInfoService.GetWeatherAsync(query, cts.Token);
            if (cts.IsCancellationRequested || !_isAttached)
            {
                return;
            }

            if (!result.Success || result.Data is null)
            {
                ApplyFallback();
                return;
            }

            ApplySnapshot(result.Data, app.WeatherLocationName);
        }
        catch (OperationCanceledException)
        {
            // Ignore canceled requests.
        }
        catch
        {
            ApplyFallback();
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

    private void ApplySnapshot(WeatherSnapshot snapshot, string? fallbackLocationName)
    {
        var isNight = HyperOS3WeatherTheme.ResolveIsNightPreferred(
            snapshot,
            _timeZoneService?.CurrentTimeZone,
            _timeZoneService?.GetCurrentTime() ?? DateTime.Now);
        var currentVisual = XiaomiWeatherVisualResolver.Resolve(
            snapshot.Current.WeatherText,
            snapshot.Current.WeatherCode,
            isNight,
            _languageCode);
        var kind = currentVisual.VisualKind;
        ApplyVisualTheme(kind);
        SetLoadingSkeleton(false);
        WeatherIconImage.Source = HyperOS3WeatherAssetLoader.LoadImage(currentVisual.PrimaryIconAsset);
        CityTextBlock.Text = ResolveLocation(snapshot.LocationName, fallbackLocationName);
        ConditionTextBlock.Text = currentVisual.DisplayText;
        TemperatureTextBlock.Text = FormatTemperature(snapshot.Current.TemperatureC);

        var today = snapshot.DailyForecasts.FirstOrDefault();
        RangeTextBlock.Text = $"{FormatTemperature(today?.HighTemperatureC)}/{FormatTemperature(today?.LowTemperatureC)}";

        var now = _timeZoneService?.GetCurrentTime() ?? DateTime.Now;
        var timelineStart = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0, now.Kind);
        var sunsetSlotIndex = ResolveSunsetSlotIndex(snapshot, timelineStart, _hourlyTempBlocks.Length);
        var localHourly = snapshot.HourlyForecasts
            .Select(item => new { Source = item, Time = ConvertToConfiguredTime(item.Time) })
            .OrderBy(item => item.Time)
            .ToList();

        for (var i = 0; i < _hourlyTempBlocks.Length; i++)
        {
            var target = timelineStart.AddHours(i);
            var item = localHourly
                .OrderBy(entry => Math.Abs((entry.Time - target).TotalMinutes))
                .FirstOrDefault();
            var weatherCode = item?.Source.WeatherCode ?? snapshot.Current.WeatherCode;
            var hourVisual = XiaomiWeatherVisualResolver.Resolve(
                item?.Source.WeatherText,
                weatherCode,
                IsNightHour(target),
                _languageCode);
            var hourKind = hourVisual.VisualKind;
            _hourlyTempBlocks[i].Text = i == sunsetSlotIndex
                ? L("weather.hourly.sunset", "Sunset")
                : FormatTemperature(item?.Source.TemperatureC ?? snapshot.Current.TemperatureC);
            _hourlyTimeBlocks[i].Text = target.ToString("HH:mm", CultureInfo.InvariantCulture);
            _hourlyIconBlocks[i].Source = HyperOS3WeatherAssetLoader.LoadImage(hourVisual.CompactIconAsset);
        }

        var todayDate = DateOnly.FromDateTime(now);
        for (var i = 0; i < _dailyLabelBlocks.Length; i++)
        {
            var date = todayDate.AddDays(i + 1);
            var daily = snapshot.DailyForecasts.FirstOrDefault(entry => entry.Date == date) ?? snapshot.DailyForecasts.FirstOrDefault();
            var weatherCode = daily?.DayWeatherCode ?? daily?.NightWeatherCode ?? snapshot.Current.WeatherCode;
            var dayVisual = XiaomiWeatherVisualResolver.Resolve(
                daily?.DayWeatherText ?? daily?.NightWeatherText,
                weatherCode,
                false,
                _languageCode);
            var dayKind = dayVisual.VisualKind;
            var dayText = dayVisual.DisplayText;
            _dailyLabelBlocks[i].Text = $"{ResolveDayLabel(date, i + 1)}·{dayText}";
            _dailyHighBlocks[i].Text = FormatTemperatureValue(daily?.HighTemperatureC);
            _dailyLowBlocks[i].Text = FormatTemperatureValue(daily?.LowTemperatureC);
            _dailyIconKinds[i] = dayKind;
            _dailyIconBlocks[i].Source = HyperOS3WeatherAssetLoader.LoadImage(dayVisual.CompactIconAsset);
        }
    }

    private void ApplyFallback()
    {
        ApplyVisualTheme(HyperOS3WeatherVisualKind.Unknown);
        SetLoadingSkeleton(false);
        WeatherIconImage.Source = HyperOS3WeatherAssetLoader.LoadImage(HyperOS3WeatherTheme.ResolveHeroIconAsset(HyperOS3WeatherVisualKind.Unknown));
        CityTextBlock.Text = L("weather.widget.location_unknown", "Unknown location");
        ConditionTextBlock.Text = L("weather.widget.loading", "Loading...");
        TemperatureTextBlock.Text = "--°";
        RangeTextBlock.Text = "--°/--°";
        var now = _timeZoneService?.GetCurrentTime() ?? DateTime.Now;
        var timelineStart = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0, now.Kind);
        for (var i = 0; i < _hourlyTempBlocks.Length; i++)
        {
            _hourlyTempBlocks[i].Text = i == 3 ? L("weather.hourly.sunset", "Sunset") : "--°";
            _hourlyTimeBlocks[i].Text = timelineStart.AddHours(i).ToString("HH:mm", CultureInfo.InvariantCulture);
            _hourlyIconBlocks[i].Source = HyperOS3WeatherAssetLoader.LoadImage(HyperOS3WeatherTheme.ResolveMiniIconAsset(HyperOS3WeatherVisualKind.Unknown));
        }

        for (var i = 0; i < _dailyLabelBlocks.Length; i++)
        {
            _dailyLabelBlocks[i].Text = $"{ResolveDayLabel(DateOnly.FromDateTime(DateTime.Now).AddDays(i + 1), i + 1)}·{L("weather.widget.condition_cloudy", "Cloudy")}";
            _dailyHighBlocks[i].Text = "--";
            _dailyLowBlocks[i].Text = "--";
            _dailyIconKinds[i] = HyperOS3WeatherVisualKind.Unknown;
            _dailyIconBlocks[i].Source = HyperOS3WeatherAssetLoader.LoadImage(HyperOS3WeatherTheme.ResolveMiniIconAsset(HyperOS3WeatherVisualKind.Unknown));
        }
    }

    private void ApplyVisualTheme(HyperOS3WeatherVisualKind kind)
    {
        _activeVisualKind = kind;
        var palette = HyperOS3WeatherTheme.ResolvePalette(kind);
        RootBorder.Background = CreateGradientBrush(palette.GradientFrom, palette.GradientTo);

        var background = CreateImageBrush(HyperOS3WeatherTheme.ResolveBackgroundAsset(kind));
        BackgroundImageLayer.Background = background ?? CreateGradientBrush(palette.GradientFrom, palette.GradientTo);
        BackgroundMotionLayer.Background = background ?? CreateGradientBrush(palette.GradientFrom, palette.GradientTo);
        BackgroundTintLayer.Background = CreateSolidBrush(palette.Tint);

        var isNightVisual = kind is HyperOS3WeatherVisualKind.ClearNight or HyperOS3WeatherVisualKind.PartlyCloudyNight or HyperOS3WeatherVisualKind.CloudyNight;
        var backgroundSamples = WeatherTypographyAccessibility.BuildBackgroundSamples(
            palette.GradientFrom,
            palette.GradientTo,
            palette.Tint,
            isNightVisual);
        TemperatureTextBlock.Foreground = WeatherTypographyAccessibility.CreateReadableBrush(
            palette.PrimaryText,
            backgroundSamples,
            WeatherTypographyAccessibility.WcagLargeTextContrast);
        CityTextBlock.Foreground = WeatherTypographyAccessibility.CreateReadableBrush(
            palette.SecondaryText,
            backgroundSamples,
            WeatherTypographyAccessibility.WcagNormalTextContrast,
            isNightVisual ? (byte)0xE6 : (byte)0xD4);
        ConditionTextBlock.Foreground = WeatherTypographyAccessibility.CreateReadableBrush(
            palette.PrimaryText,
            backgroundSamples,
            WeatherTypographyAccessibility.WcagLargeTextContrast,
            isNightVisual ? (byte)0xED : (byte)0xDF);
        RangeTextBlock.Foreground = WeatherTypographyAccessibility.CreateReadableBrush(
            palette.PrimaryText,
            backgroundSamples,
            WeatherTypographyAccessibility.WcagLargeTextContrast,
            isNightVisual ? (byte)0xE2 : (byte)0xCE);
        HourlyPanelBorder.Background = Brushes.Transparent;
        SeparatorLine.Background = CreateSolidBrush(palette.SecondaryText, isNightVisual ? (byte)0x3A : (byte)0x28);

        var hourlyTempBrush = WeatherTypographyAccessibility.CreateReadableBrush(
            palette.PrimaryText,
            backgroundSamples,
            WeatherTypographyAccessibility.WcagNormalTextContrast,
            isNightVisual ? (byte)0xEE : (byte)0xE1);
        var hourlyTimeBrush = WeatherTypographyAccessibility.CreateReadableBrush(
            palette.TertiaryText,
            backgroundSamples,
            WeatherTypographyAccessibility.WcagNormalTextContrast,
            isNightVisual ? (byte)0xC8 : (byte)0xAC);
        var dailyTextBrush = WeatherTypographyAccessibility.CreateReadableBrush(
            palette.PrimaryText,
            backgroundSamples,
            WeatherTypographyAccessibility.WcagNormalTextContrast,
            isNightVisual ? (byte)0xEA : (byte)0xDF);
        var dailyLowBrush = WeatherTypographyAccessibility.CreateReadableBrush(
            palette.TertiaryText,
            backgroundSamples,
            WeatherTypographyAccessibility.WcagNormalTextContrast,
            isNightVisual ? (byte)0xBE : (byte)0xA6);
        for (var i = 0; i < _hourlyTempBlocks.Length; i++)
        {
            _hourlyTempBlocks[i].Foreground = hourlyTempBrush;
            _hourlyTimeBlocks[i].Foreground = hourlyTimeBrush;
        }

        for (var i = 0; i < _dailyLabelBlocks.Length; i++)
        {
            _dailyLabelBlocks[i].Foreground = dailyTextBrush;
            _dailyHighBlocks[i].Foreground = dailyTextBrush;
            _dailyLowBlocks[i].Foreground = dailyLowBrush;
        }
    }

    private void ApplyTypography(double width, double height)
    {
        var innerWidth = Math.Max(140, width - ContentPaddingBorder.Padding.Left - ContentPaddingBorder.Padding.Right);
        var innerHeight = Math.Max(140, height - ContentPaddingBorder.Padding.Top - ContentPaddingBorder.Padding.Bottom);
        var fitScale = Math.Clamp(Math.Min(innerWidth / 592d, innerHeight / 600d), 0.30, 3.20);
        var cellScale = Math.Clamp(_currentCellSize / 44d, 0.34, 3.80);
        var visualScale = Math.Clamp((fitScale * 0.72) + (cellScale * 0.28), 0.30, 3.80);
        var emphasis = Math.Clamp((visualScale - 0.82) / 1.90, 0, 1);

        LayoutRoot.RowSpacing = Math.Clamp(8 * fitScale, 1, 22);
        SummaryGrid.ColumnSpacing = Math.Clamp(16 * fitScale, 4, 38);
        SummaryInfoGrid.RowSpacing = Math.Clamp(2 * fitScale, 0.2, 9);
        BottomInfoStack.Spacing = Math.Clamp(2 * fitScale, 0.3, 14);
        ConditionRangeStack.Spacing = Math.Clamp(9 * fitScale, 1, 24);
        HourlyGrid.ColumnSpacing = Math.Clamp(4 * fitScale, 0.5, 22);

        var summaryHeight = Math.Clamp(innerHeight * 0.22, 34, Math.Max(34, innerHeight * 0.42));
        var hourlyHeight = Math.Clamp(innerHeight * 0.20, 34, Math.Max(34, innerHeight * 0.36));
        var separatorBandHeight = Math.Clamp(innerHeight * 0.03, 4, 40);
        if (LayoutRoot.RowDefinitions.Count >= 4)
        {
            LayoutRoot.RowDefinitions[0].Height = new GridLength(summaryHeight, GridUnitType.Pixel);
            LayoutRoot.RowDefinitions[1].Height = new GridLength(hourlyHeight, GridUnitType.Pixel);
            LayoutRoot.RowDefinitions[2].Height = new GridLength(separatorBandHeight, GridUnitType.Pixel);
            LayoutRoot.RowDefinitions[3].Height = new GridLength(1, GridUnitType.Star);
        }

        var topScale = Math.Clamp(((summaryHeight / 118d) * 0.44) + (visualScale * 0.84), 0.24, 4.00);
        var iconGrowth = Math.Clamp((visualScale - 0.88) / 1.70, 0, 1);
        var iconScaleBoost = ResolveHeroIconScaleBoost(_activeVisualKind);
        var iconSize = Math.Clamp(Lerp(90, 122, iconGrowth) * topScale * iconScaleBoost, 14, 360);
        iconSize = Math.Min(iconSize, Math.Max(14, innerWidth * Lerp(0.18, 0.26, iconGrowth)));
        var temperatureSample = string.IsNullOrWhiteSpace(TemperatureTextBlock.Text)
            ? "00°"
            : TemperatureTextBlock.Text.Trim();
        var temperatureGlyphCount = Math.Clamp(temperatureSample.Length, 3, 6);
        var temperatureMaxWidth = Math.Max(30, innerWidth - iconSize - SummaryGrid.ColumnSpacing - 6);
        var rawTemperatureSize = Math.Clamp(Lerp(72, 102, iconGrowth) * topScale, 14, 340);
        var fitTemperatureSize = temperatureMaxWidth / (temperatureGlyphCount * 0.62);
        TemperatureTextBlock.FontSize = Math.Clamp(Math.Min(rawTemperatureSize, fitTemperatureSize), 10, 340);
        TemperatureTextBlock.FontWeight = ToVariableWeight(Lerp(300, 380, emphasis));
        TemperatureTextBlock.MaxWidth = Math.Clamp(temperatureMaxWidth, 30, Math.Max(300, innerWidth * 0.66));
        TemperatureTextBlock.Margin = new Thickness(0, Math.Clamp(-2.2 * topScale, -12, 0), 0, 0);

        var cityFontSize = Math.Clamp(18.5 * topScale, 7, 86);
        var conditionFontSize = Math.Clamp(20 * topScale, 7, 90);
        var rangeFontSize = Math.Clamp(20 * topScale, 7, 90);
        CityTextBlock.FontSize = cityFontSize;
        ConditionTextBlock.FontSize = conditionFontSize;
        RangeTextBlock.FontSize = rangeFontSize;
        CityTextBlock.FontWeight = ToVariableWeight(Lerp(530, 620, emphasis));
        ConditionTextBlock.FontWeight = ToVariableWeight(Lerp(580, 660, emphasis));
        RangeTextBlock.FontWeight = ToVariableWeight(Lerp(600, 680, emphasis));
        CityTextBlock.LineHeight = cityFontSize * 1.08;
        ConditionTextBlock.LineHeight = conditionFontSize * 1.06;
        RangeTextBlock.LineHeight = rangeFontSize * 1.06;

        WeatherIconImage.Width = iconSize;
        WeatherIconImage.Height = iconSize;
        WeatherIconImage.Margin = new Thickness(0, Math.Clamp(-2.4 * topScale, -12, 0), 0, 0);
        ConditionTextBlock.MaxWidth = Math.Clamp(innerWidth * 0.25, 28, 340);
        RangeTextBlock.MaxWidth = Math.Clamp(innerWidth * 0.31, 34, 380);
        CityTextBlock.MaxWidth = Math.Clamp(innerWidth * 0.36, 34, 420);

        HourlyPanelBorder.Padding = new Thickness(0);
        HourlyPanelBorder.CornerRadius = new CornerRadius(0);
        HourlyPanelBorder.Margin = new Thickness(0, Math.Clamp(4 * fitScale, 0, 18), 0, 0);

        var hourlyCellWidth = Math.Max(12, (innerWidth - (HourlyGrid.ColumnSpacing * 5)) / 6d);
        var hourlyCellScale = Math.Clamp(
            Math.Min((visualScale * 0.44) + ((hourlyHeight / 120d) * 0.62), hourlyCellWidth / 76d),
            0.22,
            3.80);
        var hourlyTempSize = Math.Clamp(19 * hourlyCellScale, 6, 72);
        var hourlyTimeSize = Math.Clamp(14 * hourlyCellScale, 6, 52);
        var hourlyIconSize = Math.Clamp(42 * hourlyCellScale, 9, 140);
        hourlyIconSize = Math.Min(hourlyIconSize, Math.Max(10, hourlyCellWidth * 0.86));
        hourlyIconSize = Math.Min(hourlyIconSize, Math.Max(10, hourlyHeight * 0.56));
        var hourlyStackSpacing = Math.Clamp(2 * hourlyCellScale, 0.2, 10);
        for (var i = 0; i < _hourlyTempBlocks.Length; i++)
        {
            _hourlyTempBlocks[i].FontSize = hourlyTempSize;
            _hourlyTimeBlocks[i].FontSize = hourlyTimeSize;
            _hourlyTempBlocks[i].FontWeight = ToVariableWeight(Lerp(540, 650, emphasis));
            _hourlyTimeBlocks[i].FontWeight = ToVariableWeight(Lerp(450, 560, emphasis));
            _hourlyTempBlocks[i].MaxWidth = Math.Clamp(hourlyCellWidth, 12, 260);
            _hourlyTimeBlocks[i].MaxWidth = Math.Clamp(hourlyCellWidth, 12, 260);
            _hourlyIconBlocks[i].Width = hourlyIconSize;
            _hourlyIconBlocks[i].Height = hourlyIconSize;
            if (_hourlyTempBlocks[i].Parent is StackPanel stack) stack.Spacing = hourlyStackSpacing;
        }

        SeparatorLine.Margin = new Thickness(0, Math.Clamp(separatorBandHeight * 0.45, 1, 16), 0, 0);
        DailyGrid.Margin = new Thickness(0, Math.Clamp(6 * fitScale, 0.5, 24), 0, 0);
        var dailyAreaHeight = Math.Max(50, innerHeight - summaryHeight - hourlyHeight - separatorBandHeight - (LayoutRoot.RowSpacing * 3) - DailyGrid.Margin.Top);
        var dailyRowSpacing = Math.Clamp(dailyAreaHeight * 0.028, 1, 22);
        DailyGrid.RowSpacing = dailyRowSpacing;
        var dailyRowHeight = Math.Max(8, (dailyAreaHeight - (dailyRowSpacing * 4)) / 5d);
        var dailyRowScale = Math.Clamp(((dailyRowHeight / 40d) * 0.62) + (visualScale * 0.44), 0.22, 3.80);

        var dailyLabelSize = Math.Clamp(18.5 * dailyRowScale, 6, 70);
        var dailyTempSize = Math.Clamp(19 * dailyRowScale, 6, 72);
        var dailyIconSize = Math.Clamp(43 * dailyRowScale, 9, 132);
        dailyIconSize = Math.Min(dailyIconSize, Math.Max(10, dailyRowHeight * 0.92));
        dailyIconSize = Math.Min(dailyIconSize, Math.Max(10, innerWidth * 0.14));
        var dailyLabelMaxWidth = Math.Clamp(innerWidth * 0.52, 28, 460);
        var dailyHighWidth = Math.Clamp(innerWidth * 0.14, 14, 140);
        var dailyLowWidth = Math.Clamp(innerWidth * 0.11, 12, 120);
        var dailyHighRightGap = Math.Clamp(innerWidth * 0.018, 1, 28);
        for (var i = 0; i < _dailyLabelBlocks.Length; i++)
        {
            _dailyLabelBlocks[i].FontSize = dailyLabelSize;
            _dailyHighBlocks[i].FontSize = dailyTempSize;
            _dailyLowBlocks[i].FontSize = dailyTempSize;
            _dailyLabelBlocks[i].FontWeight = ToVariableWeight(Lerp(520, 620, emphasis));
            _dailyHighBlocks[i].FontWeight = ToVariableWeight(Lerp(560, 680, emphasis));
            _dailyLowBlocks[i].FontWeight = ToVariableWeight(Lerp(470, 590, emphasis));
            _dailyLabelBlocks[i].MaxWidth = dailyLabelMaxWidth;
            _dailyHighBlocks[i].Width = dailyHighWidth;
            _dailyLowBlocks[i].Width = dailyLowWidth;
            _dailyHighBlocks[i].Margin = new Thickness(0, 0, dailyHighRightGap, 0);
            _dailyLowBlocks[i].Margin = new Thickness(0);
            _dailyHighBlocks[i].HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right;
            _dailyLowBlocks[i].HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right;
            _dailyHighBlocks[i].TextAlignment = TextAlignment.Right;
            _dailyLowBlocks[i].TextAlignment = TextAlignment.Right;
            if (_dailyIconBlocks[i].Parent is Grid dailyRowGrid)
            {
                dailyRowGrid.ColumnSpacing = Math.Clamp(9 * dailyRowScale, 4, 18);
            }

            var dailyKind = i < _dailyIconKinds.Length
                ? _dailyIconKinds[i]
                : HyperOS3WeatherVisualKind.Unknown;
            var dailyIconVisualSize = Math.Clamp(
                dailyIconSize * ResolveDailyMiniIconScaleBoost(dailyKind),
                8,
                148);
            dailyIconVisualSize = Math.Min(dailyIconVisualSize, Math.Max(10, dailyRowHeight * 0.94));
            _dailyIconBlocks[i].Width = dailyIconVisualSize;
            _dailyIconBlocks[i].Height = dailyIconVisualSize;
        }
    }

    private int ResolveSunsetSlotIndex(WeatherSnapshot snapshot, DateTime startTime, int slotCount)
    {
        if (slotCount <= 0)
        {
            return -1;
        }

        var todayForecast = snapshot.DailyForecasts.FirstOrDefault(item => item.Date == DateOnly.FromDateTime(startTime));
        if (todayForecast is null || !TryParseClockTime(todayForecast.SunsetTime, out var sunsetClock))
        {
            return -1;
        }

        var sunsetTime = startTime.Date + sunsetClock;
        var bestIndex = -1;
        var bestDelta = double.MaxValue;
        for (var i = 0; i < slotCount; i++)
        {
            var slotTime = startTime.AddHours(i);
            var deltaMinutes = Math.Abs((slotTime - sunsetTime).TotalMinutes);
            if (deltaMinutes >= bestDelta)
            {
                continue;
            }

            bestDelta = deltaMinutes;
            bestIndex = i;
        }

        return bestDelta <= 60 ? bestIndex : -1;
    }

    private static bool TryParseClockTime(string? text, out TimeSpan value)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            value = default;
            return false;
        }

        var candidate = text.Trim();
        if (TimeSpan.TryParse(candidate, CultureInfo.InvariantCulture, out value))
        {
            return true;
        }

        if (DateTimeOffset.TryParse(candidate, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var dto))
        {
            value = dto.TimeOfDay;
            return true;
        }

        if (DateTime.TryParse(candidate, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var dt))
        {
            value = dt.TimeOfDay;
            return true;
        }

        return false;
    }

    private static bool IsNightHour(DateTime time) => time.Hour < 6 || time.Hour >= 18;

    private string ResolveDayLabel(DateOnly date, int offset)
    {
        if (offset == 1) return L("weather.multiday.tomorrow", "Tomorrow");
        var dt = date.ToDateTime(TimeOnly.MinValue);
        if (string.Equals(_languageCode, "zh-CN", StringComparison.OrdinalIgnoreCase))
        {
            return dt.ToString("ddd", CultureInfo.GetCultureInfo("zh-CN"))
                .Replace("星期", "周", StringComparison.Ordinal);
        }

        return dt.ToString("ddd", CultureInfo.InvariantCulture);
    }

    private string ResolveLocation(string? rawLocation, string? fallbackLocation)
    {
        var input = string.IsNullOrWhiteSpace(rawLocation) ? fallbackLocation : rawLocation;
        return ResolvePreciseDisplayLocation(
            input,
            _languageCode,
            L("weather.widget.location_unknown", "Unknown location"));
    }

    private static string ResolvePreciseDisplayLocation(string? rawName, string languageCode, string fallback)
    {
        if (string.IsNullOrWhiteSpace(rawName))
        {
            return fallback;
        }

        var name = rawName.Trim();
        if (name.Length == 0)
        {
            return fallback;
        }

        var isZh = string.Equals(languageCode, "zh-CN", StringComparison.OrdinalIgnoreCase);
        var candidates = new List<string> { name };

        // Prefer detailed parts inside parenthesis, e.g. "Beijing (Haidian)".
        var parenthesisMatches = Regex.Matches(name, @"\(([^()]+)\)|\uFF08([^\uFF08\uFF09]+)\uFF09");
        foreach (Match match in parenthesisMatches)
        {
            var inner = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
            if (!string.IsNullOrWhiteSpace(inner))
            {
                candidates.Add(inner.Trim());
            }
        }

        var nameWithoutParenthesis = Regex.Replace(name, @"\([^()]*\)|\uFF08[^\uFF08\uFF09]*\uFF09", " ");
        candidates.Add(nameWithoutParenthesis);

        const string splitPattern = @"[\s\|/\\,\uFF0C\u3001\u00B7]+";
        foreach (var piece in Regex.Split(string.Join(" ", candidates), splitPattern))
        {
            var token = piece.Trim();
            if (!string.IsNullOrWhiteSpace(token))
            {
                candidates.Add(token);
            }
        }

        var best = fallback;
        var bestScore = int.MinValue;
        foreach (var candidate in candidates
                     .Select(c => c.Trim())
                     .Where(c => !string.IsNullOrWhiteSpace(c))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var score = ScoreLocationToken(candidate, isZh);
            if (score > bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }

        return string.IsNullOrWhiteSpace(best) ? fallback : best;
    }

    private static int ScoreLocationToken(string token, bool isZh)
    {
        var cleaned = token.Trim();
        if (cleaned.Length == 0)
        {
            return int.MinValue;
        }

        if (Regex.IsMatch(cleaned, @"^[0-9.+-]+$") ||
            cleaned.StartsWith("coord:", StringComparison.OrdinalIgnoreCase))
        {
            return -500;
        }

        var score = Math.Min(cleaned.Length, 32);
        if (isZh)
        {
            // Prefer granular places: street > district > city > province.
            if (cleaned.EndsWith("\u8857\u9053", StringComparison.Ordinal) ||
                cleaned.EndsWith("\u8DEF", StringComparison.Ordinal) ||
                cleaned.EndsWith("\u793E\u533A", StringComparison.Ordinal) ||
                cleaned.EndsWith("\u6751", StringComparison.Ordinal))
            {
                score += 120;
            }
            else if (cleaned.EndsWith("\u9547", StringComparison.Ordinal) ||
                     cleaned.EndsWith("\u4E61", StringComparison.Ordinal) ||
                     cleaned.EndsWith("\u65B0\u533A", StringComparison.Ordinal))
            {
                score += 100;
            }
            else if (cleaned.EndsWith("\u533A", StringComparison.Ordinal) ||
                     cleaned.EndsWith("\u53BF", StringComparison.Ordinal) ||
                     cleaned.EndsWith("\u65D7", StringComparison.Ordinal))
            {
                score += 80;
            }
            else if (cleaned.EndsWith("\u5E02", StringComparison.Ordinal) ||
                     cleaned.EndsWith("\u5DDE", StringComparison.Ordinal) ||
                     cleaned.EndsWith("\u76DF", StringComparison.Ordinal))
            {
                score += 60;
            }
            else if (cleaned.EndsWith("\u7701", StringComparison.Ordinal) ||
                     cleaned.EndsWith("\u81EA\u6CBB\u533A", StringComparison.Ordinal) ||
                     cleaned.EndsWith("\u7279\u522B\u884C\u653F\u533A", StringComparison.Ordinal))
            {
                score += 40;
            }
        }
        else
        {
            var lower = cleaned.ToLowerInvariant();
            if (lower.Contains("street", StringComparison.Ordinal) ||
                lower.Contains("st.", StringComparison.Ordinal) ||
                lower.Contains("road", StringComparison.Ordinal) ||
                lower.Contains("rd.", StringComparison.Ordinal) ||
                lower.Contains("avenue", StringComparison.Ordinal) ||
                lower.Contains("district", StringComparison.Ordinal))
            {
                score += 120;
            }
            else if (lower.Contains("county", StringComparison.Ordinal) ||
                     lower.Contains("borough", StringComparison.Ordinal))
            {
                score += 90;
            }
            else if (lower.Contains("city", StringComparison.Ordinal))
            {
                score += 70;
            }
            else if (lower.Contains("province", StringComparison.Ordinal) ||
                     lower.Contains("state", StringComparison.Ordinal))
            {
                score += 50;
            }
            else if (lower.Contains("country", StringComparison.Ordinal))
            {
                score += 30;
            }
        }

        return score;
    }

    private string ResolveWeatherText(string? weatherText, int? weatherCode, HyperOS3WeatherVisualKind kind)
    {
        _ = kind;
        return XiaomiWeatherVisualResolver.ResolveDisplayText(weatherText, weatherCode, _languageCode);
    }

    private DateTime ConvertToConfiguredTime(DateTimeOffset sourceTime)
    {
        try
        {
            return _timeZoneService is null
                ? sourceTime.ToLocalTime().DateTime
                : TimeZoneInfo.ConvertTime(sourceTime, _timeZoneService.CurrentTimeZone).DateTime;
        }
        catch
        {
            return sourceTime.ToLocalTime().DateTime;
        }
    }

    private static string FormatTemperature(double? value) => !value.HasValue || double.IsNaN(value.Value) || double.IsInfinity(value.Value) ? "--°" : $"{(int)Math.Round(value.Value, MidpointRounding.AwayFromZero)}°";
    private static string FormatTemperatureValue(double? value) => !value.HasValue || double.IsNaN(value.Value) || double.IsInfinity(value.Value) ? "--" : $"{(int)Math.Round(value.Value, MidpointRounding.AwayFromZero)}";

    private static IBrush? CreateImageBrush(string? uriText)
    {
        var source = HyperOS3WeatherAssetLoader.LoadImage(uriText);
        if (source is not IImageBrushSource brushSource)
        {
            return null;
        }

        return new ImageBrush { Source = brushSource, Stretch = Stretch.UniformToFill, AlignmentX = AlignmentX.Center, AlignmentY = AlignmentY.Center };
    }

    private string L(string key, string fallback) => _localizationService.GetString(_languageCode, key, fallback);

    private void InitializeMotionTransform()
    {
        BackgroundMotionLayer.RenderTransform = new TransformGroup
        {
            Children = new Transforms
            {
                _backgroundMotionScaleTransform,
                _backgroundMotionTranslateTransform
            }
        };
        SetMotionTransform(0, 0, 1.05);
    }

    private void SetMotionTransform(double translateX, double translateY, double scale)
    {
        _backgroundMotionScaleTransform.ScaleX = scale;
        _backgroundMotionScaleTransform.ScaleY = scale;
        _backgroundMotionTranslateTransform.X = translateX;
        _backgroundMotionTranslateTransform.Y = translateY;
    }

    private void UpdateTimerState()
    {
        if (_isAttached && _isOnActivePage)
        {
            if (_autoRefreshEnabled && !_refreshTimer.IsEnabled)
            {
                _refreshTimer.Start();
            }
            else if (!_autoRefreshEnabled && _refreshTimer.IsEnabled)
            {
                _refreshTimer.Stop();
            }

            if (!_animationTimer.IsEnabled)
            {
                _animationTimer.Start();
            }

            return;
        }

        _refreshTimer.Stop();
        _animationTimer.Stop();
    }

    private void ApplyAutoRefreshSettings()
    {
        var enabled = true;
        var intervalMinutes = 12;

        try
        {
            var snapshot = _settingsService.LoadSnapshot<ComponentSettingsSnapshot>(
                SettingsScope.ComponentInstance,
                _componentId,
                _placementId);
            enabled = snapshot.WeatherAutoRefreshEnabled;
            intervalMinutes = NormalizeAutoRefreshIntervalMinutes(snapshot.WeatherAutoRefreshIntervalMinutes);
        }
        catch
        {
            // Keep fallback defaults.
        }

        _autoRefreshEnabled = enabled;
        _refreshTimer.Interval = TimeSpan.FromMinutes(intervalMinutes);

        if (_isAttached)
        {
            UpdateTimerState();
        }
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

    private void CancelRefresh()
    {
        var cts = Interlocked.Exchange(ref _refreshCts, null);
        cts?.Cancel();
        cts?.Dispose();
    }

    private static double ResolveScale(double width, double height) => Math.Clamp(Math.Min(Math.Clamp(width / 620d, 0.42, 2.4), Math.Clamp(height / 620d, 0.42, 2.4)), 0.42, 2.4);
    private static double Lerp(double from, double to, double t) => from + ((to - from) * t);
    private static double ResolveHeroIconScaleBoost(HyperOS3WeatherVisualKind kind) =>
        kind switch
        {
            HyperOS3WeatherVisualKind.RainLight or HyperOS3WeatherVisualKind.RainHeavy or HyperOS3WeatherVisualKind.Storm or HyperOS3WeatherVisualKind.Snow => 1.16,
            HyperOS3WeatherVisualKind.ClearNight or HyperOS3WeatherVisualKind.PartlyCloudyNight or HyperOS3WeatherVisualKind.CloudyNight => 1.08,
            HyperOS3WeatherVisualKind.Haze or HyperOS3WeatherVisualKind.Fog => 1.04,
            _ => 1.0
        };

    private static double ResolveDailyMiniIconScaleBoost(HyperOS3WeatherVisualKind kind) =>
        kind switch
        {
            HyperOS3WeatherVisualKind.CloudyDay => 1.30,
            HyperOS3WeatherVisualKind.CloudyNight => 1.28,
            HyperOS3WeatherVisualKind.PartlyCloudyDay => 1.28,
            HyperOS3WeatherVisualKind.PartlyCloudyNight => 1.26,
            HyperOS3WeatherVisualKind.ClearDay => 1.26,
            HyperOS3WeatherVisualKind.ClearNight => 1.24,
            HyperOS3WeatherVisualKind.Haze => 1.20,
            HyperOS3WeatherVisualKind.Fog => 1.18,
            HyperOS3WeatherVisualKind.Sleet => 1.14,
            HyperOS3WeatherVisualKind.RainLight => 1.14,
            HyperOS3WeatherVisualKind.RainHeavy => 1.12,
            HyperOS3WeatherVisualKind.Snow => 1.12,
            HyperOS3WeatherVisualKind.Storm => 1.08,
            _ => 1.18
        };
    private static FontWeight ToVariableWeight(double weight) => (FontWeight)(int)Math.Clamp(Math.Round(weight), 1, 1000);
    private static IBrush CreateSolidBrush(string colorHex) => new SolidColorBrush(Color.Parse(colorHex));
    private static IBrush CreateSolidBrush(string colorHex, byte alpha) { var c = Color.Parse(colorHex); return new SolidColorBrush(Color.FromArgb(alpha, c.R, c.G, c.B)); }
    private static IBrush CreateGradientBrush(string fromHex, string toHex) => new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative), GradientStops = new GradientStops { new(Color.Parse(fromHex), 0), new(Color.Parse(toHex), 1) } };

    private void SetLoadingSkeleton(bool isLoading)
    {
        var opacity = isLoading ? 0.58 : 1.0;
        TemperatureTextBlock.Opacity = opacity;
        ConditionTextBlock.Opacity = opacity;
        RangeTextBlock.Opacity = opacity;
        CityTextBlock.Opacity = isLoading ? 0.50 : 0.96;
        for (var i = 0; i < _hourlyTempBlocks.Length; i++)
        {
            _hourlyTempBlocks[i].Opacity = opacity;
            _hourlyTimeBlocks[i].Opacity = isLoading ? 0.74 : 0.94;
        }
    }
}
