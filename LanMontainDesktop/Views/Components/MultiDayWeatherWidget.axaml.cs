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
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using FluentIcons.Avalonia;
using FluentIcons.Common;
using LanMontainDesktop.Models;
using LanMontainDesktop.Services;

namespace LanMontainDesktop.Views.Components;

public partial class MultiDayWeatherWidget : UserControl, IDesktopComponentWidget, ITimeZoneAwareComponentWidget, IWeatherInfoAwareComponentWidget
{
    private enum WeatherVisualKind
    {
        ClearDay,
        ClearNight,
        CloudyDay,
        CloudyNight,
        RainLight,
        RainHeavy,
        Storm,
        Snow,
        Fog
    }

    private readonly record struct WeatherVisualPalette(
        string GradientFrom,
        string GradientTo,
        string Tint,
        string PrimaryText,
        string SecondaryText,
        string TertiaryText,
        string ParticleColor);

    private readonly record struct WeatherMotionProfile(
        double DriftX,
        double DriftY,
        double ZoomBase,
        double ZoomAmplitude,
        double MotionOpacityBase,
        double MotionOpacityPulse,
        double LightOpacityBase,
        double LightOpacityPulse,
        double ShadeOpacityBase,
        double ShadeOpacityPulse,
        double PhaseStep,
        int ParticleCount,
        double ParticleSpeedMin,
        double ParticleSpeedMax,
        double ParticleLengthMin,
        double ParticleLengthMax,
        double ParticleDriftPerTick);

    private sealed class ParticleState
    {
        public double Speed { get; set; }

        public double Drift { get; set; }
    }

    private sealed record MultiDayWeatherWidgetConfig(
        string LanguageCode,
        string Locale,
        string LocationKey,
        string LocationName,
        double Latitude,
        double Longitude);

    private readonly record struct HourlyForecastItem(
        DateTime Time,
        string TimeLabel,
        Symbol Icon,
        string TemperatureText);

    private static readonly IWeatherInfoService DefaultWeatherInfoService = new XiaomiWeatherService();

    private readonly DispatcherTimer _refreshTimer = new()
    {
        Interval = TimeSpan.FromMinutes(12)
    };

    private readonly DispatcherTimer _backgroundAnimationTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(48)
    };

    private readonly AppSettingsService _settingsService = new();
    private readonly LocalizationService _localizationService = new();
    private readonly Dictionary<WeatherVisualKind, IBrush> _backgroundBrushCache = new();
    private readonly Dictionary<HyperOS3WeatherVisualKind, IBrush> _particleBrushCache = new();
    private readonly List<Border> _particleVisuals = new();
    private readonly List<ParticleState> _particleStates = new();
    private readonly Random _particleRandom = new();

    private IWeatherInfoService _weatherInfoService = DefaultWeatherInfoService;
    private TimeZoneService? _timeZoneService;
    private CancellationTokenSource? _refreshCts;
    private WeatherSnapshot? _latestSnapshot;
    private string _languageCode = "zh-CN";
    private double _currentCellSize = 48;
    private WeatherVisualKind _activeVisualKind = WeatherVisualKind.ClearDay;
    private double _animationPhase;
    private int _activeParticleCount;
    private bool _isAttached;
    private bool _isRefreshing;
    private readonly TextBlock[] _hourlyTimeBlocks;
    private readonly SymbolIcon[] _hourlyIconBlocks;
    private readonly TextBlock[] _hourlyTempBlocks;

    public MultiDayWeatherWidget()
    {
        InitializeComponent();
        _hourlyTimeBlocks =
        [
            HourlyTime0, HourlyTime1, HourlyTime2, HourlyTime3, HourlyTime4
        ];
        _hourlyIconBlocks =
        [
            HourlyIcon0, HourlyIcon1, HourlyIcon2, HourlyIcon3, HourlyIcon4
        ];
        _hourlyTempBlocks =
        [
            HourlyTemp0, HourlyTemp1, HourlyTemp2, HourlyTemp3, HourlyTemp4
        ];
        ConfigureTextOverflowGuards();

        _refreshTimer.Tick += OnRefreshTimerTick;
        _backgroundAnimationTimer.Tick += OnBackgroundAnimationTick;
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
        SizeChanged += OnSizeChanged;

        InitializeParticleVisuals();
        ApplyVisualTheme(WeatherVisualKind.ClearDay);
        ApplyNotConfiguredState();
        ApplyCellSize(_currentCellSize);
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
        TemperatureTextBlock.TextTrimming = TextTrimming.CharacterEllipsis;
        TemperatureTextBlock.MaxLines = 1;

        foreach (var timeBlock in _hourlyTimeBlocks)
        {
            timeBlock.TextWrapping = TextWrapping.NoWrap;
            timeBlock.TextTrimming = TextTrimming.CharacterEllipsis;
            timeBlock.MaxLines = 1;
            timeBlock.TextAlignment = TextAlignment.Center;
        }

        foreach (var tempBlock in _hourlyTempBlocks)
        {
            tempBlock.TextWrapping = TextWrapping.NoWrap;
            tempBlock.TextTrimming = TextTrimming.CharacterEllipsis;
            tempBlock.MaxLines = 1;
            tempBlock.TextAlignment = TextAlignment.Center;
        }
    }

    public void SetTimeZoneService(TimeZoneService timeZoneService)
    {
        ClearTimeZoneService();
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
        if (_isAttached)
        {
            _ = RefreshWeatherAsync(forceRefresh: false);
        }
    }

    public void ApplyCellSize(double cellSize)
    {
        _currentCellSize = Math.Max(1, cellSize);
        var metrics = HyperOS3WeatherTheme.ResolveMetrics(HyperOS3WeatherWidgetKind.MultiDay4x2);
        var scale = ResolveScale();
        var hostWidth = Bounds.Width > 1 ? Bounds.Width : Math.Max(140, _currentCellSize * 4);
        var hostHeight = Bounds.Height > 1 ? Bounds.Height : Math.Max(78, _currentCellSize * 2);
        var cornerRadius = Math.Clamp(_currentCellSize * metrics.CornerRadiusScale, 24, 44);

        RootBorder.CornerRadius = new CornerRadius(cornerRadius);
        BackgroundImageLayer.CornerRadius = new CornerRadius(cornerRadius);
        BackgroundMotionLayer.CornerRadius = new CornerRadius(cornerRadius);
        BackgroundTintLayer.CornerRadius = new CornerRadius(cornerRadius);
        BackgroundLightLayer.CornerRadius = new CornerRadius(cornerRadius);
        BackgroundShadeLayer.CornerRadius = new CornerRadius(cornerRadius);
        ContentPaddingBorder.Padding = new Thickness(
            Math.Clamp(Math.Min((_currentCellSize * metrics.HorizontalPaddingScale) * scale, hostWidth * 0.028), 3, 18),
            Math.Clamp(Math.Min((_currentCellSize * metrics.VerticalPaddingScale) * scale, hostHeight * 0.060), 2, 14));
        ApplyAdaptiveTypography();
        ResetParticles();
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _isAttached = true;
        _refreshTimer.Start();
        _backgroundAnimationTimer.Start();
        _ = RefreshWeatherAsync(forceRefresh: false);
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _isAttached = false;
        _refreshTimer.Stop();
        _backgroundAnimationTimer.Stop();
        CancelRefreshRequest();
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        ApplyCellSize(_currentCellSize);
        ResetParticles();
    }

    private async void OnRefreshTimerTick(object? sender, EventArgs e)
    {
        await RefreshWeatherAsync(forceRefresh: false);
    }

    private void OnBackgroundAnimationTick(object? sender, EventArgs e)
    {
        if (!_isAttached)
        {
            return;
        }

        var motion = ResolveMotionProfile(_activeVisualKind);
        _animationPhase += motion.PhaseStep;
        if (_animationPhase > Math.PI * 2)
        {
            _animationPhase -= Math.PI * 2;
        }

        var sin = Math.Sin(_animationPhase);
        var cos = Math.Cos(_animationPhase * 0.83);
        var zoom = motion.ZoomBase + (sin * motion.ZoomAmplitude);

        SetMotionTransform(sin * motion.DriftX, cos * motion.DriftY, zoom);

        BackgroundMotionLayer.Opacity = Math.Clamp(
            motion.MotionOpacityBase + (cos * motion.MotionOpacityPulse),
            0.08,
            0.92);
        BackgroundLightLayer.Opacity = Math.Clamp(
            motion.LightOpacityBase + (sin * motion.LightOpacityPulse),
            0.10,
            0.95);
        BackgroundShadeLayer.Opacity = Math.Clamp(
            motion.ShadeOpacityBase + (cos * motion.ShadeOpacityPulse),
            0.42,
            0.95);

        AdvanceParticles(motion);
    }

    private void OnTimeZoneChanged(object? sender, EventArgs e)
    {
        if (_isAttached)
        {
            _ = RefreshWeatherAsync(forceRefresh: false);
        }
    }

    private WeatherVisualKind ResolveFallbackVisualKind()
    {
        return IsNightNow() ? WeatherVisualKind.ClearNight : WeatherVisualKind.ClearDay;
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
            ApplyNotConfiguredState();
            _isRefreshing = false;
            return;
        }

        if (_latestSnapshot is null)
        {
            ApplyLoadingState(config.LocationName);
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
                ForecastDays: 3,
                Locale: config.Locale,
                ForceRefresh: forceRefresh);

            var result = await _weatherInfoService.GetWeatherAsync(query, cts.Token);
            if (cts.IsCancellationRequested || !_isAttached)
            {
                return;
            }

            if (!result.Success || result.Data is null)
            {
                ApplyFailedState(config.LocationName);
                return;
            }

            _latestSnapshot = result.Data;
            ApplySnapshot(result.Data, config.LocationName);
        }
        catch (OperationCanceledException)
        {
            // Ignore canceled refresh requests.
        }
        catch
        {
            if (!cts.IsCancellationRequested && _isAttached)
            {
                ApplyFailedState(config.LocationName);
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

    private MultiDayWeatherWidgetConfig LoadConfig()
    {
        var snapshot = _settingsService.Load();
        var languageCode = _localizationService.NormalizeLanguageCode(snapshot.LanguageCode);
        var locale = string.Equals(languageCode, "zh-CN", StringComparison.OrdinalIgnoreCase)
            ? "zh_cn"
            : "en_us";

        var latitude = NormalizeLatitude(snapshot.WeatherLatitude);
        var longitude = NormalizeLongitude(snapshot.WeatherLongitude);
        var modeIsCoordinates = string.Equals(snapshot.WeatherLocationMode, "Coordinates", StringComparison.OrdinalIgnoreCase);
        var locationKey = snapshot.WeatherLocationKey?.Trim() ?? string.Empty;
        var locationName = snapshot.WeatherLocationName?.Trim() ?? string.Empty;

        if (modeIsCoordinates)
        {
            if (string.IsNullOrWhiteSpace(locationKey))
            {
                locationKey = BuildCoordinateLocationKey(latitude, longitude);
            }

            if (string.IsNullOrWhiteSpace(locationName))
            {
                locationName = BuildCoordinateLocationName(latitude, longitude, languageCode);
            }
        }
        else if (string.IsNullOrWhiteSpace(locationName))
        {
            locationName = locationKey;
        }

        return new MultiDayWeatherWidgetConfig(
            languageCode,
            locale,
            locationKey,
            locationName,
            latitude,
            longitude);
    }

    private void ApplySnapshot(WeatherSnapshot snapshot, string fallbackLocationName)
    {
        var isNight = ResolveIsNight(snapshot);
        var visualKind = ResolveVisualKind(snapshot.Current.WeatherCode, isNight);
        ApplyVisualTheme(visualKind);

        var rawLocation = string.IsNullOrWhiteSpace(snapshot.LocationName)
            ? fallbackLocationName
            : snapshot.LocationName;
        CityTextBlock.Text = ResolvePreciseDisplayLocation(rawLocation, _languageCode, L("weather.widget.location_unknown", "Unknown location"));

        ConditionTextBlock.Text = ResolveWeatherConditionText(snapshot.Current.WeatherText, visualKind);
        WeatherIconSymbol.Symbol = ResolveWeatherSymbol(visualKind);
        WeatherIconSymbol.Foreground = CreateSolidBrush(
            ResolveWeatherIconAccent(
                WeatherIconSymbol.Symbol,
                visualKind is WeatherVisualKind.ClearNight or WeatherVisualKind.CloudyNight));

        TemperatureTextBlock.Text = FormatTemperature(snapshot.Current.TemperatureC);
        RangeTextBlock.Text = FormatAirQualityText(snapshot.Current.AirQualityIndex);
        ApplyHourlyForecastItems(BuildHourlyForecastItems(snapshot));
        ApplyAdaptiveTypography();
    }

    private void ApplyNotConfiguredState()
    {
        var fallbackKind = ResolveFallbackVisualKind();
        ApplyVisualTheme(fallbackKind);
        WeatherIconSymbol.Symbol = fallbackKind == WeatherVisualKind.ClearNight
            ? Symbol.WeatherMoon
            : Symbol.WeatherSunny;
        WeatherIconSymbol.Foreground = CreateSolidBrush(
            ResolveWeatherIconAccent(
                WeatherIconSymbol.Symbol,
                fallbackKind is WeatherVisualKind.ClearNight or WeatherVisualKind.CloudyNight));
        CityTextBlock.Text = L("weather.widget.location_not_configured", "Weather location is not configured");
        ConditionTextBlock.Text = L("weather.widget.condition_unknown", "Unknown");
        TemperatureTextBlock.Text = "--°";
        RangeTextBlock.Text = L("weather.multiday.aqi_unknown", "Air --");
        ApplyHourlyForecastItems(BuildPlaceholderHourlyForecastItems(fallbackKind));
        ApplyAdaptiveTypography();
        _latestSnapshot = null;
    }

    private void ApplyLoadingState(string locationName)
    {
        var loadingKind = IsNightNow() ? WeatherVisualKind.CloudyNight : WeatherVisualKind.CloudyDay;
        ApplyVisualTheme(loadingKind);
        WeatherIconSymbol.Symbol = loadingKind == WeatherVisualKind.CloudyNight
            ? Symbol.WeatherPartlyCloudyNight
            : Symbol.WeatherPartlyCloudyDay;
        WeatherIconSymbol.Foreground = CreateSolidBrush(
            ResolveWeatherIconAccent(
                WeatherIconSymbol.Symbol,
                loadingKind is WeatherVisualKind.ClearNight or WeatherVisualKind.CloudyNight));
        CityTextBlock.Text = ResolvePreciseDisplayLocation(
            locationName,
            _languageCode,
            L("weather.widget.location_unknown", "Unknown location"));
        ConditionTextBlock.Text = L("weather.widget.loading", "Loading...");
        TemperatureTextBlock.Text = "--°";
        RangeTextBlock.Text = L("weather.multiday.aqi_unknown", "Air --");
        ApplyHourlyForecastItems(BuildPlaceholderHourlyForecastItems(loadingKind));
        ApplyAdaptiveTypography();
    }

    private void ApplyFailedState(string locationName)
    {
        ApplyVisualTheme(WeatherVisualKind.Fog);
        WeatherIconSymbol.Symbol = Symbol.WeatherFog;
        WeatherIconSymbol.Foreground = CreateSolidBrush(ResolveWeatherIconAccent(WeatherIconSymbol.Symbol, false));
        CityTextBlock.Text = ResolvePreciseDisplayLocation(
            locationName,
            _languageCode,
            L("weather.widget.location_unknown", "Unknown location"));
        ConditionTextBlock.Text = L("weather.widget.fetch_failed", "Weather fetch failed");
        TemperatureTextBlock.Text = "--°";
        RangeTextBlock.Text = L("weather.multiday.aqi_unknown", "Air --");
        ApplyHourlyForecastItems(BuildPlaceholderHourlyForecastItems(WeatherVisualKind.Fog));
        ApplyAdaptiveTypography();
        _latestSnapshot = null;
    }

    private void ApplyVisualTheme(WeatherVisualKind kind)
    {
        _activeVisualKind = kind;
        var palette = ResolvePalette(kind);
        RootBorder.Background = CreateGradientBrush(palette.GradientFrom, palette.GradientTo);
        BackgroundImageLayer.Background = ResolveWeatherBackgroundBrush(kind, palette);
        BackgroundMotionLayer.Background = ResolveWeatherBackgroundBrush(kind, palette);
        BackgroundTintLayer.Background = CreateSolidBrush(palette.Tint);

        var primary = CreateSolidBrush(palette.PrimaryText);
        var particleBrush = ResolveParticleBrush(ToThemeKind(kind), palette.ParticleColor);
        var isNightVisual = kind is WeatherVisualKind.ClearNight or WeatherVisualKind.CloudyNight;
        var conditionSecondary = CreateSolidBrush(palette.SecondaryText, isNightVisual ? (byte)0xF0 : (byte)0xE6);
        var airQualitySecondary = CreateSolidBrush(palette.SecondaryText, isNightVisual ? (byte)0xDE : (byte)0xCC);
        var forecastTimeBrush = CreateSolidBrush(palette.TertiaryText, isNightVisual ? (byte)0xEA : (byte)0xB6);
        var forecastTempBrush = CreateSolidBrush(palette.PrimaryText, isNightVisual ? (byte)0xF8 : (byte)0xE4);
        HourlyPanelBorder.Background = CreateSolidBrush(isNightVisual ? "#24FFFFFF" : "#1EFFFFFF");
        LocationIcon.Foreground = primary;
        CityTextBlock.Foreground = primary;
        TemperatureTextBlock.Foreground = primary;
        WeatherIconSymbol.Foreground = CreateSolidBrush(ResolveWeatherIconAccent(WeatherIconSymbol.Symbol, isNightVisual));
        ConditionTextBlock.Foreground = conditionSecondary;
        RangeTextBlock.Foreground = airQualitySecondary;
        for (var i = 0; i < _hourlyTimeBlocks.Length; i++)
        {
            _hourlyTimeBlocks[i].Foreground = forecastTimeBrush;
            _hourlyTempBlocks[i].Foreground = forecastTempBrush;
            _hourlyIconBlocks[i].Foreground = CreateSolidBrush(ResolveWeatherIconAccent(_hourlyIconBlocks[i].Symbol, isNightVisual));
        }

        foreach (var particle in _particleVisuals)
        {
            particle.Background = particleBrush;
        }

        ResetAnimationState();
        ResetParticles();
    }

    private IBrush ResolveWeatherBackgroundBrush(WeatherVisualKind kind, WeatherVisualPalette palette)
    {
        if (_backgroundBrushCache.TryGetValue(kind, out var cached))
        {
            return cached;
        }

        var uriText = HyperOS3WeatherTheme.ResolveBackgroundAsset(ToThemeKind(kind));
        if (!string.IsNullOrWhiteSpace(uriText))
        {
            try
            {
                var uri = new Uri(uriText, UriKind.Absolute);
                using var stream = AssetLoader.Open(uri);
                var bitmap = new Bitmap(stream);
                var imageBrush = new ImageBrush
                {
                    Source = bitmap,
                    Stretch = Stretch.UniformToFill,
                    AlignmentX = AlignmentX.Center,
                    AlignmentY = AlignmentY.Center
                };
                _backgroundBrushCache[kind] = imageBrush;
                return imageBrush;
            }
            catch
            {
                // Fall through to gradient background when the image cannot be loaded.
            }
        }

        var gradientBrush = CreateGradientBrush(palette.GradientFrom, palette.GradientTo);
        _backgroundBrushCache[kind] = gradientBrush;
        return gradientBrush;
    }

    private IBrush ResolveParticleBrush(HyperOS3WeatherVisualKind kind, string fallbackColor)
    {
        if (_particleBrushCache.TryGetValue(kind, out var cached))
        {
            return cached;
        }

        var uriText = HyperOS3WeatherTheme.ResolveParticleAsset(kind);
        if (!string.IsNullOrWhiteSpace(uriText))
        {
            try
            {
                var uri = new Uri(uriText, UriKind.Absolute);
                using var stream = AssetLoader.Open(uri);
                var bitmap = new Bitmap(stream);
                var imageBrush = new ImageBrush
                {
                    Source = bitmap,
                    Stretch = Stretch.UniformToFill,
                    AlignmentX = AlignmentX.Center,
                    AlignmentY = AlignmentY.Center
                };
                _particleBrushCache[kind] = imageBrush;
                return imageBrush;
            }
            catch
            {
                // Fall through to solid particle color when the image cannot be loaded.
            }
        }

        var solidBrush = CreateSolidBrush(fallbackColor);
        _particleBrushCache[kind] = solidBrush;
        return solidBrush;
    }

    private static WeatherVisualKind ResolveVisualKind(int? weatherCode, bool isNight)
    {
        return HyperOS3WeatherTheme.ResolveVisualKind(weatherCode, isNight) switch
        {
            HyperOS3WeatherVisualKind.ClearDay => WeatherVisualKind.ClearDay,
            HyperOS3WeatherVisualKind.ClearNight => WeatherVisualKind.ClearNight,
            HyperOS3WeatherVisualKind.CloudyDay => WeatherVisualKind.CloudyDay,
            HyperOS3WeatherVisualKind.CloudyNight => WeatherVisualKind.CloudyNight,
            HyperOS3WeatherVisualKind.RainLight => WeatherVisualKind.RainLight,
            HyperOS3WeatherVisualKind.RainHeavy => WeatherVisualKind.RainHeavy,
            HyperOS3WeatherVisualKind.Storm => WeatherVisualKind.Storm,
            HyperOS3WeatherVisualKind.Snow => WeatherVisualKind.Snow,
            _ => WeatherVisualKind.Fog
        };
    }

    private static WeatherVisualPalette ResolvePalette(WeatherVisualKind kind)
    {
        var palette = HyperOS3WeatherTheme.ResolvePalette(ToThemeKind(kind));
        return new WeatherVisualPalette(
            palette.GradientFrom,
            palette.GradientTo,
            palette.Tint,
            palette.PrimaryText,
            palette.SecondaryText,
            palette.TertiaryText,
            palette.ParticleColor);
    }

    private static Symbol ResolveWeatherSymbol(WeatherVisualKind kind)
    {
        return HyperOS3WeatherTheme.ResolveWeatherSymbol(ToThemeKind(kind));
    }

    private static HyperOS3WeatherVisualKind ToThemeKind(WeatherVisualKind kind)
    {
        return kind switch
        {
            WeatherVisualKind.ClearDay => HyperOS3WeatherVisualKind.ClearDay,
            WeatherVisualKind.ClearNight => HyperOS3WeatherVisualKind.ClearNight,
            WeatherVisualKind.CloudyDay => HyperOS3WeatherVisualKind.CloudyDay,
            WeatherVisualKind.CloudyNight => HyperOS3WeatherVisualKind.CloudyNight,
            WeatherVisualKind.RainLight => HyperOS3WeatherVisualKind.RainLight,
            WeatherVisualKind.RainHeavy => HyperOS3WeatherVisualKind.RainHeavy,
            WeatherVisualKind.Storm => HyperOS3WeatherVisualKind.Storm,
            WeatherVisualKind.Snow => HyperOS3WeatherVisualKind.Snow,
            _ => HyperOS3WeatherVisualKind.Fog
        };
    }

    private string ResolveWeatherConditionText(string? weatherText, WeatherVisualKind kind)
    {
        if (!string.IsNullOrWhiteSpace(weatherText))
        {
            return weatherText;
        }

        return kind switch
        {
            WeatherVisualKind.ClearDay or WeatherVisualKind.ClearNight => L("weather.widget.condition_clear", "Clear"),
            WeatherVisualKind.CloudyDay or WeatherVisualKind.CloudyNight => L("weather.widget.condition_cloudy", "Cloudy"),
            WeatherVisualKind.RainLight or WeatherVisualKind.RainHeavy => L("weather.widget.condition_rain", "Rain"),
            WeatherVisualKind.Storm => L("weather.widget.condition_storm", "Thunderstorm"),
            WeatherVisualKind.Snow => L("weather.widget.condition_snow", "Snow"),
            WeatherVisualKind.Fog => L("weather.widget.condition_fog", "Fog"),
            _ => L("weather.widget.condition_unknown", "Unknown")
        };
    }

    private static (double? Low, double? High) ResolveTemperatureRange(WeatherSnapshot snapshot)
    {
        var first = snapshot.DailyForecasts.FirstOrDefault();
        var low = first?.LowTemperatureC;
        var high = first?.HighTemperatureC;

        if (!low.HasValue && !high.HasValue && snapshot.Current.TemperatureC.HasValue)
        {
            var baseline = snapshot.Current.TemperatureC.Value;
            low = Math.Floor(baseline - 2);
            high = Math.Ceiling(baseline + 2);
        }

        return (low, high);
    }

    private string FormatTemperatureRange(double? low, double? high)
    {
        if (!low.HasValue && !high.HasValue)
        {
            return L("weather.widget.range_unknown", "-- / --");
        }

        var lowText = FormatTemperature(low);
        var highText = FormatTemperature(high);
        return string.Format(
            GetUiCulture(),
            L("weather.widget.range_format", "{0} / {1}"),
            lowText,
            highText);
    }

    private string FormatAirQualityText(int? airQualityIndex)
    {
        if (!airQualityIndex.HasValue || airQualityIndex.Value <= 0)
        {
            return L("weather.multiday.aqi_unknown", "Air --");
        }

        return string.Format(
            GetUiCulture(),
            L("weather.multiday.aqi_format", "Air Quality {0}"),
            airQualityIndex.Value);
    }

    private static string FormatTemperature(double? value)
    {
        if (!value.HasValue || double.IsNaN(value.Value) || double.IsInfinity(value.Value))
        {
            return "--°";
        }

        var rounded = (int)Math.Round(value.Value, MidpointRounding.AwayFromZero);
        return string.Create(CultureInfo.InvariantCulture, $"{rounded}°");
    }

    private IReadOnlyList<HourlyForecastItem> BuildHourlyForecastItems(WeatherSnapshot snapshot)
    {
        const int itemCount = 5;
        var today = DateOnly.FromDateTime(_timeZoneService?.GetCurrentTime() ?? DateTime.Now);
        var firstFallback = snapshot.DailyForecasts.FirstOrDefault();
        var items = new List<HourlyForecastItem>(itemCount);

        for (var i = 0; i < itemCount; i++)
        {
            var date = today.AddDays(i);
            var daily = ResolveDailyForecastForDate(snapshot, date) ?? firstFallback;
            var weatherCode = daily?.DayWeatherCode ??
                              daily?.NightWeatherCode ??
                              snapshot.Current.WeatherCode;
            var visualKind = ResolveVisualKind(weatherCode, isNight: false);
            var low = daily?.LowTemperatureC;
            var high = daily?.HighTemperatureC;
            var rangeText = string.Format(
                CultureInfo.InvariantCulture,
                "{0} / {1}",
                FormatTemperature(low),
                FormatTemperature(high));

            items.Add(new HourlyForecastItem(
                date.ToDateTime(TimeOnly.MinValue),
                ResolveForecastDayLabel(date, i),
                ResolveWeatherSymbol(visualKind),
                rangeText));
        }

        return items;
    }

    private IReadOnlyList<HourlyForecastItem> BuildPlaceholderHourlyForecastItems(WeatherVisualKind visualKind)
    {
        const int itemCount = 5;
        var items = new List<HourlyForecastItem>(itemCount);
        var start = DateOnly.FromDateTime(_timeZoneService?.GetCurrentTime() ?? DateTime.Now);
        var symbol = ResolveWeatherSymbol(visualKind);
        for (var i = 0; i < itemCount; i++)
        {
            var date = start.AddDays(i);
            items.Add(new HourlyForecastItem(
                date.ToDateTime(TimeOnly.MinValue),
                ResolveForecastDayLabel(date, i),
                symbol,
                "--° / --°"));
        }

        return items;
    }

    private void ApplyHourlyForecastItems(IReadOnlyList<HourlyForecastItem> items)
    {
        var isNightVisual = _activeVisualKind is WeatherVisualKind.ClearNight or WeatherVisualKind.CloudyNight;
        var compactRangeText = ResolveScale() <= 0.78;
        for (var i = 0; i < _hourlyTimeBlocks.Length; i++)
        {
            if (i >= items.Count)
            {
                _hourlyTimeBlocks[i].Text = "--";
                _hourlyTempBlocks[i].Text = compactRangeText ? "--°/--°" : "--° / --°";
                _hourlyIconBlocks[i].Symbol = ResolveWeatherSymbol(_activeVisualKind);
                continue;
            }

            var item = items[i];
            _hourlyTimeBlocks[i].Text = item.TimeLabel;
            _hourlyIconBlocks[i].Symbol = item.Icon;
            _hourlyIconBlocks[i].Foreground = CreateSolidBrush(ResolveWeatherIconAccent(item.Icon, isNightVisual));
            _hourlyTempBlocks[i].Text = compactRangeText
                ? CompactRangeLabel(item.TemperatureText)
                : item.TemperatureText;
        }
    }

    private static string CompactRangeLabel(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "--°/--°";
        }

        return text
            .Replace(" / ", "/", StringComparison.Ordinal)
            .Replace(" /", "/", StringComparison.Ordinal)
            .Replace("/ ", "/", StringComparison.Ordinal);
    }

    private static WeatherDailyForecast? ResolveDailyForecastForDate(WeatherSnapshot snapshot, DateOnly date)
    {
        foreach (var forecast in snapshot.DailyForecasts)
        {
            if (forecast.Date == date)
            {
                return forecast;
            }
        }

        return null;
    }

    private string ResolveForecastDayLabel(DateOnly date, int offset)
    {
        if (offset == 0)
        {
            return L("weather.multiday.today", "Today");
        }

        if (offset == 1)
        {
            return L("weather.multiday.tomorrow", "Tomorrow");
        }

        var dateTime = date.ToDateTime(TimeOnly.MinValue);
        var isZh = string.Equals(_languageCode, "zh-CN", StringComparison.OrdinalIgnoreCase);
        if (isZh)
        {
            var weekday = dateTime.ToString("ddd", CultureInfo.GetCultureInfo("zh-CN"));
            return weekday
                .Replace("星期", "周", StringComparison.Ordinal)
                .Replace("周周", "周", StringComparison.Ordinal);
        }

        try
        {
            return dateTime.ToString("ddd", CultureInfo.GetCultureInfo(_languageCode));
        }
        catch
        {
            return dateTime.ToString("ddd", CultureInfo.InvariantCulture);
        }
    }

    private static string ResolveWeatherIconAccent(Symbol symbol, bool isNightVisual)
    {
        var kind = isNightVisual ? HyperOS3WeatherVisualKind.ClearNight : HyperOS3WeatherVisualKind.ClearDay;
        return HyperOS3WeatherTheme.ResolveIconAccent(kind, symbol);
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

    private void ApplyAdaptiveTypography()
    {
        var (layoutWidth, layoutHeight) = ResolveLayoutViewport();
        var metrics = HyperOS3WeatherTheme.ResolveMetrics(HyperOS3WeatherWidgetKind.MultiDay4x2);
        var scale = ResolveScale(layoutWidth, layoutHeight);
        var densityBoost = scale <= 0.55 ? 0.80 : scale <= 0.72 ? 0.88 : scale <= 0.92 ? 0.95 : scale >= 1.45 ? 1.06 : 1.0;
        var compactness = Math.Clamp((0.88 - scale) / 0.50, 0, 1);
        var cityLength = Math.Max(1, CityTextBlock.Text?.Length ?? 2);
        var cityCompression = cityLength >= 12 ? 0.68 : cityLength >= 9 ? 0.80 : cityLength >= 6 ? 0.90 : 1.0;
        var conditionLength = Math.Max(1, ConditionTextBlock.Text?.Length ?? 2);
        var conditionCompression = conditionLength >= 12 ? 0.72 : conditionLength >= 8 ? 0.85 : conditionLength >= 6 ? 0.92 : 1.0;

        ContentGrid.RowSpacing = Math.Clamp(Math.Max(metrics.MainGap * scale, layoutHeight * Lerp(0.030, 0.018, compactness)), 2, 14);
        TopRowGrid.ColumnSpacing = Math.Clamp(Math.Max(metrics.MainGap * scale, layoutWidth * 0.014), 3, 14);
        BottomInfoStack.Spacing = Math.Clamp(Math.Max(metrics.SectionGap * scale, layoutHeight * 0.016), 2, 10);
        BottomInfoStack.Margin = new Thickness(0, 0, 0, Math.Clamp(layoutHeight * 0.018, 0, 10));
        ConditionIconStack.Spacing = Math.Clamp(layoutWidth * 0.009, 3, 12);
        RangeTextBlock.Margin = new Thickness(0, 0, 0, Math.Clamp(layoutHeight * 0.020, 0, 12));

        HourlyPanelBorder.Padding = new Thickness(
            Math.Clamp(layoutWidth * 0.018, 4, 16),
            Math.Clamp(layoutHeight * 0.020, 3, 12));
        HourlyPanelBorder.CornerRadius = new CornerRadius(Math.Clamp(Math.Min(layoutWidth, layoutHeight) * 0.065, 8, 22));
        HourlyGrid.ColumnSpacing = Math.Clamp(layoutWidth * 0.010, 1.5, 12);

        var topBandHeight = Math.Max(18, layoutHeight * 0.22);
        var middleBandHeight = Math.Max(24, layoutHeight * 0.30);
        var bottomBandHeight = Math.Max(22, layoutHeight - topBandHeight - middleBandHeight - (ContentGrid.RowSpacing * 2));

        LocationIcon.FontSize = Math.Min(Math.Clamp((metrics.IconFont * 0.6) * scale * densityBoost, 9, 30), topBandHeight * 0.58);
        CityTextBlock.FontSize = Math.Min(Math.Clamp((metrics.PrimaryTextFont * 1.42) * scale * cityCompression * densityBoost, 12, 46), topBandHeight * 0.76);
        WeatherIconSymbol.FontSize = Math.Min(Math.Clamp((metrics.IconFont * 1.02) * scale * densityBoost, 12, 56), topBandHeight * 0.95);
        TemperatureTextBlock.FontSize = Math.Min(Math.Clamp((metrics.PrimaryTemperatureFont * 1.40) * scale * densityBoost, 26, 138), middleBandHeight * 0.92);
        ConditionTextBlock.FontSize = Math.Min(Math.Clamp((metrics.PrimaryTextFont * 1.10) * scale * conditionCompression * densityBoost, 9, 40), topBandHeight * 0.70);
        RangeTextBlock.FontSize = Math.Min(Math.Clamp((metrics.SecondaryTextFont * 1.42) * scale * densityBoost, 9, 42), middleBandHeight * 0.50);
        TemperatureTextBlock.Margin = new Thickness(0, Math.Clamp(layoutHeight * 0.008, 0, 6), 0, Math.Clamp(layoutHeight * 0.012, 0, 8));

        var weightProgress = Math.Clamp((scale - 0.34) / 1.18, 0, 1);
        CityTextBlock.FontWeight = ToVariableWeight(Lerp(540, 680, weightProgress));
        TemperatureTextBlock.FontWeight = ToVariableWeight(Lerp(600, 760, weightProgress));
        ConditionTextBlock.FontWeight = ToVariableWeight(Lerp(490, 620, weightProgress));
        RangeTextBlock.FontWeight = ToVariableWeight(Lerp(480, 600, weightProgress));

        var topRightMaxWidth = Math.Clamp(layoutWidth * Lerp(0.36, 0.31, compactness), 112, 230);
        ConditionIconStack.MaxWidth = topRightMaxWidth;
        ConditionTextBlock.MaxWidth = Math.Max(
            42,
            topRightMaxWidth - WeatherIconSymbol.FontSize - ConditionIconStack.Spacing - 4);
        RangeTextBlock.MaxWidth = Math.Clamp(layoutWidth * Lerp(0.36, 0.32, compactness), 112, 250);
        var leftTopBudget = Math.Max(
            132,
            layoutWidth - Math.Max(topRightMaxWidth, RangeTextBlock.MaxWidth) - Math.Clamp(66 * scale, 26, 96));
        TemperatureTextBlock.MaxWidth = leftTopBudget;
        CityTextBlock.MaxWidth = Math.Max(110, layoutWidth - Math.Clamp(90 * scale, 30, 128));

        var forecastColumnCount = Math.Max(1, _hourlyTimeBlocks.Length);
        var forecastInnerWidth = Math.Max(
            80,
            layoutWidth - HourlyPanelBorder.Padding.Left - HourlyPanelBorder.Padding.Right - (HourlyGrid.ColumnSpacing * (forecastColumnCount - 1)));
        var forecastCellWidth = Math.Max(32, forecastInnerWidth / forecastColumnCount);
        var forecastStackSpacing = Math.Clamp(bottomBandHeight * 0.065, 1, 5);
        var forecastInnerHeight = Math.Max(
            20,
            bottomBandHeight - HourlyPanelBorder.Padding.Top - HourlyPanelBorder.Padding.Bottom);
        var forecastLineHeight = Math.Max(6, (forecastInnerHeight - (forecastStackSpacing * 2)) / 3d);
        var hourlyTimeMaxByWidth = Math.Clamp(forecastCellWidth / Lerp(4.3, 3.7, 1 - compactness), 7, 24);
        var hourlyTempMaxByWidth = Math.Clamp(forecastCellWidth / Lerp(5.8, 5.0, 1 - compactness), 7, 24);
        var hourlyIconMaxByWidth = Math.Clamp(forecastCellWidth * Lerp(0.30, 0.36, 1 - compactness), 7, 30);
        var hourlyTimeMaxByHeight = Math.Clamp(forecastLineHeight * 0.95, 7, 22);
        var hourlyTempMaxByHeight = Math.Clamp(forecastLineHeight * 0.95, 7, 22);
        var hourlyIconMaxByHeight = Math.Clamp(forecastLineHeight * 1.05, 8, 28);

        var hourlyTimeSize = Math.Min(
            Math.Clamp((metrics.CaptionFont * 1.15) * scale * densityBoost, 8, 30),
            Math.Min(hourlyTimeMaxByWidth, hourlyTimeMaxByHeight));
        var hourlyIconSize = Math.Min(
            Math.Clamp((metrics.IconFont * 0.64) * scale * densityBoost, 8, 34),
            Math.Min(hourlyIconMaxByWidth, hourlyIconMaxByHeight));
        var hourlyTempSize = Math.Min(
            Math.Clamp((metrics.SecondaryTextFont * 1.24) * scale * densityBoost, 8, 32),
            Math.Min(hourlyTempMaxByWidth, hourlyTempMaxByHeight));
        for (var i = 0; i < _hourlyTimeBlocks.Length; i++)
        {
            _hourlyTimeBlocks[i].FontSize = hourlyTimeSize;
            _hourlyTempBlocks[i].FontSize = hourlyTempSize;
            _hourlyIconBlocks[i].FontSize = hourlyIconSize;
            _hourlyTimeBlocks[i].MaxWidth = forecastCellWidth;
            _hourlyTempBlocks[i].MaxWidth = forecastCellWidth;
            _hourlyTimeBlocks[i].FontWeight = ToVariableWeight(Lerp(470, 600, weightProgress));
            _hourlyTempBlocks[i].FontWeight = ToVariableWeight(Lerp(490, 620, weightProgress));
            if (_hourlyTimeBlocks[i].Parent is StackPanel forecastStack)
            {
                forecastStack.Spacing = forecastStackSpacing;
            }
        }
    }

    private static double Lerp(double from, double to, double t)
    {
        return from + ((to - from) * t);
    }

    private static FontWeight ToVariableWeight(double weight)
    {
        return (FontWeight)(int)Math.Clamp(Math.Round(weight), 1, 1000);
    }

    private WeatherMotionProfile ResolveMotionProfile(WeatherVisualKind kind)
    {
        var motion = HyperOS3WeatherTheme.ResolveMotion(ToThemeKind(kind));
        return new WeatherMotionProfile(
            motion.DriftX,
            motion.DriftY,
            motion.ZoomBase,
            motion.ZoomAmplitude,
            motion.MotionOpacityBase,
            motion.MotionOpacityPulse,
            motion.LightOpacityBase,
            motion.LightOpacityPulse,
            motion.ShadeOpacityBase,
            motion.ShadeOpacityPulse,
            motion.PhaseStep,
            motion.ParticleCount,
            motion.ParticleSpeedMin,
            motion.ParticleSpeedMax,
            motion.ParticleLengthMin,
            motion.ParticleLengthMax,
            motion.ParticleDriftPerTick);
    }

    private void ResetAnimationState()
    {
        var motion = ResolveMotionProfile(_activeVisualKind);
        _animationPhase = 0;
        SetMotionTransform(0, 0, motion.ZoomBase);
        BackgroundMotionLayer.Opacity = motion.MotionOpacityBase;
        BackgroundLightLayer.Opacity = motion.LightOpacityBase;
        BackgroundShadeLayer.Opacity = motion.ShadeOpacityBase;
    }

    private void SetMotionTransform(double translateX, double translateY, double scale)
    {
        var group = new TransformGroup
        {
            Children = new Transforms
            {
                new ScaleTransform(scale, scale),
                new TranslateTransform(translateX, translateY)
            }
        };
        BackgroundMotionLayer.RenderTransform = group;
    }

    private void InitializeParticleVisuals()
    {
        if (_particleVisuals.Count > 0)
        {
            return;
        }

        const int maxParticles = 40;
        for (var i = 0; i < maxParticles; i++)
        {
            var particle = new Border
            {
                IsVisible = false,
                Width = 2,
                Height = 14,
                CornerRadius = new CornerRadius(1),
                Opacity = 0.0
            };
            _particleVisuals.Add(particle);
            _particleStates.Add(new ParticleState());
            ParticleLayer.Children.Add(particle);
        }
    }

    private void ResetParticles()
    {
        if (_particleVisuals.Count == 0)
        {
            return;
        }

        var motion = ResolveMotionProfile(_activeVisualKind);
        _activeParticleCount = Math.Clamp(motion.ParticleCount, 0, _particleVisuals.Count);

        var (width, height) = ResolveParticleViewport();

        for (var i = 0; i < _particleVisuals.Count; i++)
        {
            var particle = _particleVisuals[i];
            if (i >= _activeParticleCount)
            {
                particle.IsVisible = false;
                continue;
            }

            particle.IsVisible = true;
            RespawnParticle(i, width, height, motion, initialPlacement: true);
        }
    }

    private void AdvanceParticles(WeatherMotionProfile motion)
    {
        if (_activeParticleCount <= 0)
        {
            return;
        }

        var (width, height) = ResolveParticleViewport();

        for (var i = 0; i < _activeParticleCount; i++)
        {
            var particle = _particleVisuals[i];
            var state = _particleStates[i];

            var x = Canvas.GetLeft(particle);
            var y = Canvas.GetTop(particle);
            if (double.IsNaN(x))
            {
                x = 0;
            }

            if (double.IsNaN(y))
            {
                y = -20;
            }

            var sway = _activeVisualKind == WeatherVisualKind.Snow
                ? Math.Sin(_animationPhase + (i * 0.45)) * 0.55
                : _activeVisualKind == WeatherVisualKind.Fog
                    ? Math.Sin((_animationPhase * 0.7) + (i * 0.31)) * 0.18
                    : 0;

            x += state.Drift + sway;
            y += state.Speed;

            Canvas.SetLeft(particle, x);
            Canvas.SetTop(particle, y);

            if (y > height + 48 || x > width + 56 || x < -72)
            {
                RespawnParticle(i, width, height, motion, initialPlacement: false);
            }
        }
    }

    private void RespawnParticle(int index, double width, double height, WeatherMotionProfile motion, bool initialPlacement)
    {
        var particle = _particleVisuals[index];
        var state = _particleStates[index];

        state.Speed = NextRange(motion.ParticleSpeedMin, motion.ParticleSpeedMax);
        var driftVariance = Math.Abs(motion.ParticleDriftPerTick) * 0.35;
        state.Drift = motion.ParticleDriftPerTick + NextRange(-driftVariance, driftVariance);

        var length = NextRange(motion.ParticleLengthMin, motion.ParticleLengthMax);
        var thickness = _activeVisualKind switch
        {
            WeatherVisualKind.Snow => NextRange(2.2, 4.3),
            WeatherVisualKind.Fog => NextRange(10.0, 22.0),
            _ => NextRange(1.0, 2.2)
        };
        var opacity = _activeVisualKind switch
        {
            WeatherVisualKind.Storm => NextRange(0.26, 0.52),
            WeatherVisualKind.RainHeavy => NextRange(0.24, 0.46),
            WeatherVisualKind.RainLight => NextRange(0.18, 0.34),
            WeatherVisualKind.Snow => NextRange(0.40, 0.72),
            WeatherVisualKind.Fog => NextRange(0.08, 0.20),
            _ => NextRange(0.10, 0.24)
        };

        particle.Width = thickness;
        particle.Height = length;
        particle.Opacity = opacity;
        particle.CornerRadius = new CornerRadius(Math.Max(1, thickness * 0.5));
        particle.RenderTransform = new RotateTransform(_activeVisualKind switch
        {
            WeatherVisualKind.Storm => -24,
            WeatherVisualKind.RainHeavy => -20,
            WeatherVisualKind.RainLight => -14,
            WeatherVisualKind.Snow => -6,
            _ => 0
        });

        var x = initialPlacement
            ? NextRange(-40, width + 20)
            : NextRange(-24, width + 20);
        var y = initialPlacement
            ? NextRange(-height, height)
            : -length - NextRange(8, 120);

        Canvas.SetLeft(particle, x);
        Canvas.SetTop(particle, y);
    }

    private double NextRange(double min, double max)
    {
        if (max <= min)
        {
            return min;
        }

        return min + (_particleRandom.NextDouble() * (max - min));
    }

    private string L(string key, string fallback)
    {
        return _localizationService.GetString(_languageCode, key, fallback);
    }

    private CultureInfo GetUiCulture()
    {
        try
        {
            return CultureInfo.GetCultureInfo(_languageCode);
        }
        catch
        {
            return CultureInfo.InvariantCulture;
        }
    }

    private static string BuildCoordinateLocationKey(double latitude, double longitude)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"coord:{latitude:F4},{longitude:F4}");
    }

    private static string BuildCoordinateLocationName(double latitude, double longitude, string languageCode)
    {
        var template = string.Equals(languageCode, "zh-CN", StringComparison.OrdinalIgnoreCase)
            ? "坐标 {0:F2}, {1:F2}"
            : "Coordinate {0:F2}, {1:F2}";
        return string.Format(CultureInfo.InvariantCulture, template, latitude, longitude);
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

    private (double Width, double Height) ResolveLayoutViewport()
    {
        var width = LayoutRoot.Bounds.Width;
        var height = LayoutRoot.Bounds.Height;
        if (width > 1 && height > 1)
        {
            return (width, height);
        }

        var fallbackWidth = Bounds.Width > 1
            ? Bounds.Width - ContentPaddingBorder.Padding.Left - ContentPaddingBorder.Padding.Right
            : _currentCellSize * 4;
        var fallbackHeight = Bounds.Height > 1
            ? Bounds.Height - ContentPaddingBorder.Padding.Top - ContentPaddingBorder.Padding.Bottom
            : _currentCellSize * 2;

        return (Math.Max(100, fallbackWidth), Math.Max(56, fallbackHeight));
    }

    private (double Width, double Height) ResolveParticleViewport()
    {
        var width = Bounds.Width > 1 ? Bounds.Width : LayoutRoot.Bounds.Width;
        var height = Bounds.Height > 1 ? Bounds.Height : LayoutRoot.Bounds.Height;
        return (Math.Max(80, width), Math.Max(56, height));
    }

    private double ResolveScale()
    {
        var (layoutWidth, layoutHeight) = ResolveLayoutViewport();
        return ResolveScale(layoutWidth, layoutHeight);
    }

    private double ResolveScale(double layoutWidth, double layoutHeight)
    {
        var cellScale = Math.Clamp(_currentCellSize / 44d, 0.34, 2.30);
        var heightScale = Math.Clamp(layoutHeight / 320d, 0.34, 2.30);
        var widthScale = Math.Clamp(layoutWidth / 620d, 0.34, 2.30);
        return Math.Clamp(Math.Min(cellScale, Math.Min(heightScale, widthScale) * 1.05), 0.34, 2.30);
    }

    private static IBrush CreateSolidBrush(string colorHex)
    {
        return new SolidColorBrush(Color.Parse(colorHex));
    }

    private static IBrush CreateSolidBrush(string colorHex, byte alpha)
    {
        var color = Color.Parse(colorHex);
        return new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
    }

    private static IBrush CreateGradientBrush(string fromColorHex, string toColorHex)
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

