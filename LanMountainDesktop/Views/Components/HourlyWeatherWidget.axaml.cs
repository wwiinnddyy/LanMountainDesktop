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
using LanMountainDesktop.DesktopComponents.Runtime;
using LanMountainDesktop.ComponentSystem;
using LanMountainDesktop.Host.Abstractions;
using LanMountainDesktop.Models;
using LanMountainDesktop.Services;
using LanMountainDesktop.Theme;

namespace LanMountainDesktop.Views.Components;

public partial class HourlyWeatherWidget : UserControl, IDesktopComponentWidget, IDesktopPageVisibilityAwareComponentWidget, ITimeZoneAwareComponentWidget, IWeatherInfoAwareComponentWidget, IComponentPlacementContextAware, IComponentChromeContextAware
{
    private enum WeatherVisualKind
    {
        Unknown,
        ClearDay,
        ClearNight,
        PartlyCloudyDay,
        PartlyCloudyNight,
        CloudyDay,
        CloudyNight,
        Haze,
        Sleet,
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

    private sealed record HourlyWeatherWidgetConfig(
        string LanguageCode,
        string Locale,
        string LocationKey,
        string LocationName,
        double Latitude,
        double Longitude);

    private readonly record struct HourlyForecastItem(
        DateTime Time,
        string TimeLabel,
        HyperOS3WeatherVisualKind IconKind,
        string TemperatureText);

    private static readonly IWeatherInfoService DefaultWeatherInfoService = new XiaomiWeatherService();
    private static readonly IReadOnlyList<int> SupportedAutoRefreshIntervalsMinutes = RefreshIntervalCatalog.SupportedIntervalsMinutes;

    private readonly DispatcherTimer _refreshTimer = new()
    {
        Interval = TimeSpan.FromMinutes(12)
    };

    private readonly DispatcherTimer _backgroundAnimationTimer = new()
    {
        Interval = FluttermotionToken.WeatherAnimationFrameInterval
    };

    private LanMountainDesktop.PluginSdk.ISettingsService _settingsService = LanMountainDesktop.Services.Settings.HostSettingsFacadeProvider.GetOrCreate().Settings;
    private IComponentInstanceSettingsStore _componentSettingsStore = HostComponentSettingsStoreProvider.GetOrCreate();
    private readonly LocalizationService _localizationService = new();
    private readonly Dictionary<WeatherVisualKind, IBrush> _backgroundBrushCache = new();
    private readonly Dictionary<HyperOS3WeatherVisualKind, IBrush> _particleBrushCache = new();
    private readonly List<Border> _particleVisuals = new();
    private readonly List<ParticleState> _particleStates = new();
    private readonly Random _particleRandom = new();
    private readonly ScaleTransform _backgroundMotionScaleTransform = new(1, 1);
    private readonly TranslateTransform _backgroundMotionTranslateTransform = new();

    private IWeatherInfoService _weatherInfoService = DefaultWeatherInfoService;
    private TimeZoneService? _timeZoneService;
    private CancellationTokenSource? _refreshCts;
    private WeatherSnapshot? _latestSnapshot;
    private string _languageCode = "zh-CN";
    private double _currentCellSize = 48;
    private ComponentChromeContext? _chromeContext;
    private WeatherVisualKind _activeVisualKind = WeatherVisualKind.ClearDay;
    private double _animationPhase;
    private int _activeParticleCount;
    private bool _isAttached;
    private bool _isOnActivePage = true;
    private bool _isRefreshing;
    private bool _autoRefreshEnabled = true;
    private string _componentId = BuiltInComponentIds.DesktopHourlyWeather;
    private string _placementId = string.Empty;
    private readonly TextBlock[] _hourlyTimeBlocks;
    private readonly Image[] _hourlyIconBlocks;
    private readonly TextBlock[] _hourlyTempBlocks;

    public HourlyWeatherWidget()
    {
        InitializeComponent();
        InitializeMotionTransform();
        _hourlyTimeBlocks =
        [
            HourlyTime0, HourlyTime1, HourlyTime2, HourlyTime3, HourlyTime4, HourlyTime5
        ];
        _hourlyIconBlocks =
        [
            HourlyIcon0, HourlyIcon1, HourlyIcon2, HourlyIcon3, HourlyIcon4, HourlyIcon5
        ];
        _hourlyTempBlocks =
        [
            HourlyTemp0, HourlyTemp1, HourlyTemp2, HourlyTemp3, HourlyTemp4, HourlyTemp5
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

        foreach (var timeBlock in _hourlyTimeBlocks)
        {
            timeBlock.TextWrapping = TextWrapping.NoWrap;
            timeBlock.TextTrimming = TextTrimming.None;
            timeBlock.MaxLines = 1;
            timeBlock.TextAlignment = TextAlignment.Center;
        }

        foreach (var tempBlock in _hourlyTempBlocks)
        {
            tempBlock.TextWrapping = TextWrapping.NoWrap;
            tempBlock.TextTrimming = TextTrimming.None;
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
        if (_isAttached && _isOnActivePage)
        {
            _ = RefreshWeatherAsync(forceRefresh: false);
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
            ? BuiltInComponentIds.DesktopHourlyWeather
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
            _ = RefreshWeatherAsync(forceRefresh: false);
        }
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
        var metrics = HyperOS3WeatherTheme.ResolveMetrics(HyperOS3WeatherWidgetKind.Hourly4x2);
        var scale = ResolveScale();
        var hostWidth = Bounds.Width > 1 ? Bounds.Width : Math.Max(140, _currentCellSize * 4);
        var hostHeight = Bounds.Height > 1 ? Bounds.Height : Math.Max(78, _currentCellSize * 2);
        var cornerRadius = ComponentChromeCornerRadiusHelper.Scale(
            _currentCellSize * metrics.CornerRadiusScale,
            24,
            46,
            _chromeContext);

        ComponentChromeCornerRadiusHelper.Apply(
            cornerRadius,
            RootBorder,
            BackgroundImageLayer,
            BackgroundMotionLayer,
            BackgroundTintLayer,
            BackgroundLightLayer,
            BackgroundShadeLayer);
        ContentPaddingBorder.Padding = new Thickness(
            ComponentChromeCornerRadiusHelper.SafeValue(Math.Min((_currentCellSize * metrics.HorizontalPaddingScale) * scale, hostWidth * 0.034), 4, 22, _chromeContext),
            ComponentChromeCornerRadiusHelper.SafeValue(Math.Min((_currentCellSize * metrics.VerticalPaddingScale) * scale, hostHeight * 0.068), 3, 18, _chromeContext));
        ApplyAdaptiveTypography();
        ResetParticles();
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _isAttached = true;
        ApplyAutoRefreshSettings();
        UpdateTimerState();
        if (_isOnActivePage)
        {
            _ = RefreshWeatherAsync(forceRefresh: false);
        }
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _isAttached = false;
        UpdateTimerState();
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
        if (!_isAttached || !_isOnActivePage)
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
        if (!_isAttached || !_isOnActivePage || _isRefreshing)
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

    private HourlyWeatherWidgetConfig LoadConfig()
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

        return new HourlyWeatherWidgetConfig(
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
        var visual = XiaomiWeatherVisualResolver.Resolve(
            snapshot.Current.WeatherText,
            snapshot.Current.WeatherCode,
            isNight,
            _languageCode);
        var visualKind = ResolveVisualKind(visual.VisualKind);
        ApplyVisualTheme(visualKind);

        var rawLocation = string.IsNullOrWhiteSpace(snapshot.LocationName)
            ? fallbackLocationName
            : snapshot.LocationName;
        CityTextBlock.Text = ResolvePreciseDisplayLocation(rawLocation, _languageCode, L("weather.widget.location_unknown", "Unknown location"));

        ConditionTextBlock.Text = visual.DisplayText;
        SetMainWeatherIcon(visual.PrimaryIconAsset, visualKind);
        SetLoadingSkeleton(false);

        TemperatureTextBlock.Text = FormatTemperature(snapshot.Current.TemperatureC);
        var (low, high) = ResolveTemperatureRange(snapshot);
        RangeTextBlock.Text = FormatTemperatureRange(low, high);
        ApplyHourlyForecastItems(BuildHourlyForecastItems(snapshot));
        ApplyAdaptiveTypography();
    }

    private void ApplyNotConfiguredState()
    {
        var fallbackKind = ResolveFallbackVisualKind();
        ApplyVisualTheme(fallbackKind);
        SetMainWeatherIcon(null, fallbackKind);
        SetLoadingSkeleton(false);
        CityTextBlock.Text = L("weather.widget.location_not_configured", "Weather location is not configured");
        ConditionTextBlock.Text = L("weather.widget.configure_hint", "Open Settings > Weather to configure");
        TemperatureTextBlock.Text = "--°";
        RangeTextBlock.Text = L("weather.widget.range_unknown", "-- / --");
        ApplyHourlyForecastItems(BuildPlaceholderHourlyForecastItems(fallbackKind));
        ApplyAdaptiveTypography();
        _latestSnapshot = null;
    }

    private void ApplyLoadingState(string locationName)
    {
        var loadingKind = IsNightNow() ? WeatherVisualKind.CloudyNight : WeatherVisualKind.CloudyDay;
        ApplyVisualTheme(loadingKind);
        SetMainWeatherIcon(null, loadingKind);
        SetLoadingSkeleton(true);
        CityTextBlock.Text = ResolvePreciseDisplayLocation(
            locationName,
            _languageCode,
            L("weather.widget.location_unknown", "Unknown location"));
        ConditionTextBlock.Text = L("weather.widget.loading", "Loading...");
        TemperatureTextBlock.Text = "--°";
        RangeTextBlock.Text = L("weather.widget.range_unknown", "-- / --");
        ApplyHourlyForecastItems(BuildPlaceholderHourlyForecastItems(loadingKind));
        ApplyAdaptiveTypography();
    }

    private void ApplyFailedState(string locationName)
    {
        ApplyVisualTheme(WeatherVisualKind.Unknown);
        SetMainWeatherIcon(HyperOS3WeatherTheme.ResolveHeroIconAsset(HyperOS3WeatherVisualKind.Unknown), WeatherVisualKind.Unknown);
        SetLoadingSkeleton(false);
        CityTextBlock.Text = ResolvePreciseDisplayLocation(
            locationName,
            _languageCode,
            L("weather.widget.location_unknown", "Unknown location"));
        ConditionTextBlock.Text = L("weather.widget.fetch_failed", "Weather fetch failed");
        TemperatureTextBlock.Text = "--°";
        RangeTextBlock.Text = L("weather.widget.range_unknown", "-- / --");
        ApplyHourlyForecastItems(BuildPlaceholderHourlyForecastItems(WeatherVisualKind.Unknown));
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

        var particleBrush = ResolveParticleBrush(ToThemeKind(kind), palette.ParticleColor);
        var isNightVisual = kind is WeatherVisualKind.ClearNight or WeatherVisualKind.PartlyCloudyNight or WeatherVisualKind.CloudyNight;
        var backgroundSamples = WeatherTypographyAccessibility.BuildBackgroundSamples(
            palette.GradientFrom,
            palette.GradientTo,
            palette.Tint,
            isNightVisual);
        var primary = WeatherTypographyAccessibility.CreateReadableBrush(
            palette.PrimaryText,
            backgroundSamples,
            WeatherTypographyAccessibility.WcagLargeTextContrast);
        var cityBrush = WeatherTypographyAccessibility.CreateReadableBrush(
            palette.SecondaryText,
            backgroundSamples,
            WeatherTypographyAccessibility.WcagNormalTextContrast,
            isNightVisual ? (byte)0xE6 : (byte)0xD4);
        var conditionSecondary = WeatherTypographyAccessibility.CreateReadableBrush(
            palette.PrimaryText,
            backgroundSamples,
            WeatherTypographyAccessibility.WcagLargeTextContrast,
            isNightVisual ? (byte)0xED : (byte)0xDF);
        var rangeSecondary = WeatherTypographyAccessibility.CreateReadableBrush(
            palette.PrimaryText,
            backgroundSamples,
            WeatherTypographyAccessibility.WcagLargeTextContrast,
            isNightVisual ? (byte)0xE2 : (byte)0xCE);
        var forecastTimeBrush = WeatherTypographyAccessibility.CreateReadableBrush(
            palette.TertiaryText,
            backgroundSamples,
            WeatherTypographyAccessibility.WcagNormalTextContrast,
            isNightVisual ? (byte)0xC8 : (byte)0xAC);
        var forecastTempBrush = WeatherTypographyAccessibility.CreateReadableBrush(
            palette.PrimaryText,
            backgroundSamples,
            WeatherTypographyAccessibility.WcagNormalTextContrast,
            isNightVisual ? (byte)0xEE : (byte)0xE1);
        HourlyPanelBorder.Background = Brushes.Transparent;
        LocationIcon.Foreground = cityBrush;
        CityTextBlock.Foreground = cityBrush;
        TemperatureTextBlock.Foreground = primary;
        ConditionTextBlock.Foreground = conditionSecondary;
        RangeTextBlock.Foreground = rangeSecondary;
        for (var i = 0; i < _hourlyTimeBlocks.Length; i++)
        {
            _hourlyTimeBlocks[i].Foreground = forecastTimeBrush;
            _hourlyTempBlocks[i].Foreground = forecastTempBrush;
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
        return ResolveVisualKind(HyperOS3WeatherTheme.ResolveVisualKind(weatherCode, isNight));
    }

    private static WeatherVisualKind ResolveVisualKind(HyperOS3WeatherVisualKind kind)
    {
        return kind switch
        {
            HyperOS3WeatherVisualKind.Unknown => WeatherVisualKind.Unknown,
            HyperOS3WeatherVisualKind.ClearDay => WeatherVisualKind.ClearDay,
            HyperOS3WeatherVisualKind.ClearNight => WeatherVisualKind.ClearNight,
            HyperOS3WeatherVisualKind.PartlyCloudyDay => WeatherVisualKind.PartlyCloudyDay,
            HyperOS3WeatherVisualKind.PartlyCloudyNight => WeatherVisualKind.PartlyCloudyNight,
            HyperOS3WeatherVisualKind.CloudyDay => WeatherVisualKind.CloudyDay,
            HyperOS3WeatherVisualKind.CloudyNight => WeatherVisualKind.CloudyNight,
            HyperOS3WeatherVisualKind.Haze => WeatherVisualKind.Haze,
            HyperOS3WeatherVisualKind.Sleet => WeatherVisualKind.Sleet,
            HyperOS3WeatherVisualKind.RainLight => WeatherVisualKind.RainLight,
            HyperOS3WeatherVisualKind.RainHeavy => WeatherVisualKind.RainHeavy,
            HyperOS3WeatherVisualKind.Storm => WeatherVisualKind.Storm,
            HyperOS3WeatherVisualKind.Snow => WeatherVisualKind.Snow,
            HyperOS3WeatherVisualKind.Fog => WeatherVisualKind.Fog,
            _ => WeatherVisualKind.Unknown
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

    private static HyperOS3WeatherVisualKind ToThemeKind(WeatherVisualKind kind)
    {
        return kind switch
        {
            WeatherVisualKind.Unknown => HyperOS3WeatherVisualKind.Unknown,
            WeatherVisualKind.ClearDay => HyperOS3WeatherVisualKind.ClearDay,
            WeatherVisualKind.ClearNight => HyperOS3WeatherVisualKind.ClearNight,
            WeatherVisualKind.PartlyCloudyDay => HyperOS3WeatherVisualKind.PartlyCloudyDay,
            WeatherVisualKind.PartlyCloudyNight => HyperOS3WeatherVisualKind.PartlyCloudyNight,
            WeatherVisualKind.CloudyDay => HyperOS3WeatherVisualKind.CloudyDay,
            WeatherVisualKind.CloudyNight => HyperOS3WeatherVisualKind.CloudyNight,
            WeatherVisualKind.Haze => HyperOS3WeatherVisualKind.Haze,
            WeatherVisualKind.Sleet => HyperOS3WeatherVisualKind.Sleet,
            WeatherVisualKind.RainLight => HyperOS3WeatherVisualKind.RainLight,
            WeatherVisualKind.RainHeavy => HyperOS3WeatherVisualKind.RainHeavy,
            WeatherVisualKind.Storm => HyperOS3WeatherVisualKind.Storm,
            WeatherVisualKind.Snow => HyperOS3WeatherVisualKind.Snow,
            WeatherVisualKind.Fog => HyperOS3WeatherVisualKind.Fog,
            _ => HyperOS3WeatherVisualKind.Unknown
        };
    }

    private string ResolveWeatherConditionText(string? weatherText, int? weatherCode, WeatherVisualKind kind)
    {
        _ = kind;
        return XiaomiWeatherVisualResolver.ResolveDisplayText(weatherText, weatherCode, _languageCode);
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
            return L("weather.widget.range_unknown", "--/--");
        }

        var lowText = FormatTemperature(low);
        var highText = FormatTemperature(high);
        return string.Format(
            GetUiCulture(),
            L("weather.widget.range_format", "{0}/{1}"),
            lowText,
            highText);
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
        const int itemCount = 6;
        var now = _timeZoneService?.GetCurrentTime() ?? DateTime.Now;
        var timelineStart = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0, now.Kind);
        var fallbackDaily = ResolveDailyForecastForDate(snapshot, DateOnly.FromDateTime(now))
            ?? snapshot.DailyForecasts.FirstOrDefault();
        var (low, high) = ResolveTemperatureRange(snapshot);
        var sunsetSlotIndex = ResolveSunsetSlotIndex(snapshot, timelineStart, itemCount);

        var hourlyCandidates = snapshot.HourlyForecasts
            .Select(hourly => (Hourly: hourly, Time: ConvertToConfiguredTime(hourly.Time).DateTime))
            .Where(item => item.Time >= now.AddMinutes(-70))
            .OrderBy(item => item.Time)
            .Take(72)
            .ToList();

        var items = new List<HourlyForecastItem>(itemCount);
        for (var i = 0; i < itemCount; i++)
        {
            var targetTime = timelineStart.AddHours(i);
            var displayLabel = targetTime.ToString("HH:mm", CultureInfo.InvariantCulture);

            var candidate = TryFindNearestHourlyCandidate(hourlyCandidates, targetTime);
            var weatherCode = candidate?.Hourly.WeatherCode ??
                              ResolveFallbackWeatherCode(targetTime, snapshot, fallbackDaily);
            var iconKind = ToThemeKind(ResolveVisualKind(weatherCode, IsNightHour(targetTime)));

            var estimatedTemp = candidate?.Hourly.TemperatureC ??
                                EstimateHourlyTemperature(
                                    targetTime,
                                    i,
                                    snapshot.Current.TemperatureC,
                                    low,
                                    high);
            var temperatureLabel = i == sunsetSlotIndex
                ? L("weather.hourly.sunset", "Sunset")
                : FormatTemperature(estimatedTemp);

            items.Add(new HourlyForecastItem(
                targetTime,
                displayLabel,
                iconKind,
                temperatureLabel));
        }

        return items;
    }

    private IReadOnlyList<HourlyForecastItem> BuildPlaceholderHourlyForecastItems(WeatherVisualKind visualKind)
    {
        const int itemCount = 6;
        var items = new List<HourlyForecastItem>(itemCount);
        var now = _timeZoneService?.GetCurrentTime() ?? DateTime.Now;
        var timelineStart = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0, now.Kind);
        var iconKind = ToThemeKind(visualKind);
        for (var i = 0; i < itemCount; i++)
        {
            var targetTime = timelineStart.AddHours(i);
            items.Add(new HourlyForecastItem(
                targetTime,
                targetTime.ToString("HH:mm", CultureInfo.InvariantCulture),
                iconKind,
                i == 3 ? L("weather.hourly.sunset", "Sunset") : "--°"));
        }

        return items;
    }

    private void ApplyHourlyForecastItems(IReadOnlyList<HourlyForecastItem> items)
    {
        var fallbackIcon = HyperOS3WeatherAssetLoader.LoadImage(
            HyperOS3WeatherTheme.ResolveMiniIconAsset(ToThemeKind(_activeVisualKind)));
        for (var i = 0; i < _hourlyTimeBlocks.Length; i++)
        {
            if (i >= items.Count)
            {
                _hourlyTimeBlocks[i].Text = "--";
                _hourlyTempBlocks[i].Text = "--°";
                _hourlyIconBlocks[i].Source = fallbackIcon;
                continue;
            }

            var item = items[i];
            _hourlyTimeBlocks[i].Text = item.TimeLabel;
            _hourlyIconBlocks[i].Source = HyperOS3WeatherAssetLoader.LoadImage(
                HyperOS3WeatherTheme.ResolveMiniIconAsset(item.IconKind));
            _hourlyTempBlocks[i].Text = item.TemperatureText;
        }
    }

    private static (WeatherHourlyForecast Hourly, DateTime Time)? TryFindNearestHourlyCandidate(
        IReadOnlyList<(WeatherHourlyForecast Hourly, DateTime Time)> candidates,
        DateTime targetTime)
    {
        if (candidates.Count == 0)
        {
            return null;
        }

        var bestDelta = double.MaxValue;
        (WeatherHourlyForecast Hourly, DateTime Time)? best = null;
        for (var i = 0; i < candidates.Count; i++)
        {
            var candidate = candidates[i];
            var delta = Math.Abs((candidate.Time - targetTime).TotalMinutes);
            if (delta >= bestDelta)
            {
                continue;
            }

            bestDelta = delta;
            best = candidate;
        }

        return bestDelta <= 70 ? best : null;
    }

    private int? ResolveFallbackWeatherCode(
        DateTime targetTime,
        WeatherSnapshot snapshot,
        WeatherDailyForecast? fallbackDaily)
    {
        var daily = ResolveDailyForecastForDate(snapshot, DateOnly.FromDateTime(targetTime)) ?? fallbackDaily;
        if (daily is null)
        {
            return snapshot.Current.WeatherCode;
        }

        return IsNightHour(targetTime)
            ? daily.NightWeatherCode ?? daily.DayWeatherCode ?? snapshot.Current.WeatherCode
            : daily.DayWeatherCode ?? daily.NightWeatherCode ?? snapshot.Current.WeatherCode;
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

    private int ResolveSunsetSlotIndex(WeatherSnapshot snapshot, DateTime startTime, int slotCount)
    {
        if (slotCount <= 0)
        {
            return -1;
        }

        var todayForecast = ResolveDailyForecastForDate(snapshot, DateOnly.FromDateTime(startTime));
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

    private static bool IsNightHour(DateTime time)
    {
        return time.Hour < 6 || time.Hour >= 18;
    }

    private DateTimeOffset ConvertToConfiguredTime(DateTimeOffset time)
    {
        if (_timeZoneService is null)
        {
            return time.ToLocalTime();
        }

        try
        {
            return TimeZoneInfo.ConvertTime(time, _timeZoneService.CurrentTimeZone);
        }
        catch
        {
            return time.ToLocalTime();
        }
    }

    private static double? EstimateHourlyTemperature(
        DateTime targetTime,
        int hourOffset,
        double? currentTemperature,
        double? low,
        double? high)
    {
        if (hourOffset == 0 && currentTemperature.HasValue)
        {
            return currentTemperature.Value;
        }

        if (!low.HasValue && !high.HasValue)
        {
            return currentTemperature;
        }

        if (!low.HasValue && high.HasValue)
        {
            low = high.Value - 4;
        }
        else if (!high.HasValue && low.HasValue)
        {
            high = low.Value + 4;
        }

        if (!low.HasValue || !high.HasValue)
        {
            return currentTemperature;
        }

        var hour = targetTime.Hour + targetTime.Minute / 60d;
        var normalized = (Math.Cos((hour - 15d) / 12d * Math.PI) + 1d) * 0.5d;
        var estimated = low.Value + ((high.Value - low.Value) * normalized);
        if (currentTemperature.HasValue && hourOffset <= 2)
        {
            estimated = (estimated * 0.60) + (currentTemperature.Value * 0.40);
        }

        return estimated;
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
        var innerWidth = Math.Max(120, layoutWidth);
        var innerHeight = Math.Max(56, layoutHeight);
        var fitScale = Math.Clamp(Math.Min(innerWidth / 592d, innerHeight / 284d), 0.30, 3.20);
        var cellScale = Math.Clamp(_currentCellSize / 44d, 0.34, 3.60);
        var visualScale = Math.Clamp((fitScale * 0.72) + (cellScale * 0.28), 0.30, 3.60);
        var emphasis = Math.Clamp((visualScale - 0.82) / 1.90, 0, 1);

        ContentGrid.RowSpacing = Math.Clamp(8 * fitScale, 1, 20);
        TopRowGrid.ColumnSpacing = Math.Clamp(11 * fitScale, 3, 30);
        BottomInfoStack.Margin = new Thickness(0, 0, 0, Math.Clamp(1.2 * fitScale, 0, 7));

        var contentHeight = Math.Max(36, innerHeight - ContentGrid.RowSpacing);
        var topZoneHeight = Math.Clamp(contentHeight * 0.47, 24, Math.Max(24, contentHeight - 12));
        var bottomZoneHeight = Math.Max(10, contentHeight - topZoneHeight);
        if (ContentGrid.RowDefinitions.Count >= 2)
        {
            ContentGrid.RowDefinitions[0].Height = new GridLength(topZoneHeight, GridUnitType.Pixel);
            ContentGrid.RowDefinitions[1].Height = new GridLength(1, GridUnitType.Star);
        }

        var topScale = Math.Clamp(((topZoneHeight / 116d) * 0.42) + (visualScale * 0.86), 0.24, 3.90);
        var bottomScale = Math.Clamp(((bottomZoneHeight / 156d) * 0.44) + (visualScale * 0.72), 0.24, 3.80);
        var iconGrowth = Math.Clamp((visualScale - 0.88) / 1.70, 0, 1);
        var iconScaleBoost = ResolveHeroIconScaleBoost(_activeVisualKind);
        var iconSize = Math.Clamp(Lerp(88, 116, iconGrowth) * topScale * iconScaleBoost, 14, 360);
        iconSize = Math.Min(iconSize, Math.Max(14, innerWidth * Lerp(0.22, 0.32, iconGrowth)));
        var temperatureSample = string.IsNullOrWhiteSpace(TemperatureTextBlock.Text)
            ? "00°"
            : TemperatureTextBlock.Text.Trim();
        var temperatureMaxWidth = Math.Max(28, innerWidth - iconSize - TopRowGrid.ColumnSpacing - 4);
        var rawTemperatureSize = Math.Clamp(Lerp(64, 92, iconGrowth) * topScale, 12, 320);
        var temperatureLayout = ComponentTypographyLayoutService.FitAdaptiveTextLayout(
            TemperatureTextBlock.Text,
            temperatureMaxWidth,
            Math.Max(18, topZoneHeight * 0.84),
            1,
            1,
            Math.Max(9, rawTemperatureSize * 0.42),
            rawTemperatureSize,
            [ToVariableWeight(Lerp(300, 360, emphasis))],
            1.02);
        TemperatureTextBlock.FontSize = temperatureLayout.FontSize;
        TemperatureTextBlock.FontWeight = temperatureLayout.Weight;
        TemperatureTextBlock.Margin = new Thickness(0, Math.Clamp(-2.0 * topScale, -10, 0), 0, 0);
        TemperatureTextBlock.MaxWidth = Math.Clamp(temperatureMaxWidth, 28, Math.Max(280, innerWidth * 0.68));

        var cityBadge = ComponentTypographyLayoutService.ResolveBadgeBox(
            innerWidth * 0.37,
            Math.Max(16, topZoneHeight * 0.34),
            preferredSizeScale: 0.28d,
            minSize: 10,
            maxSize: 24,
            insetScale: 0.18d);
        CityInfoBadge.Padding = cityBadge.Padding;
        CityInfoBadge.CornerRadius = new CornerRadius(cityBadge.Size / 2d);
        CityInfoBadge.MaxWidth = Math.Clamp(innerWidth * 0.37, 34, 460);
        LocationIcon.FontSize = Math.Clamp(13 * topScale, 6, 52);
        var cityLayout = ComponentTypographyLayoutService.FitAdaptiveTextLayout(
            CityTextBlock.Text,
            Math.Max(24, CityInfoBadge.MaxWidth - cityBadge.Padding.Left - cityBadge.Padding.Right),
            Math.Max(12, topZoneHeight * 0.36),
            1,
            1,
            6,
            Math.Max(6, 18.5 * topScale),
            [ToVariableWeight(Lerp(530, 620, emphasis))],
            1.08);
        CityTextBlock.FontSize = cityLayout.FontSize;
        CityTextBlock.FontWeight = cityLayout.Weight;
        CityTextBlock.LineHeight = cityLayout.LineHeight;
        CityTextBlock.MaxWidth = CityInfoBadge.MaxWidth;

        var conditionBadge = ComponentTypographyLayoutService.ResolveBadgeBox(
            innerWidth * 0.24,
            Math.Max(16, bottomZoneHeight * 0.34),
            preferredSizeScale: 0.26d,
            minSize: 10,
            maxSize: 24,
            insetScale: 0.18d);
        ConditionInfoBadge.Padding = conditionBadge.Padding;
        ConditionInfoBadge.CornerRadius = new CornerRadius(conditionBadge.Size / 2d);
        ConditionInfoBadge.MaxWidth = Math.Clamp(innerWidth * 0.24, 26, 320);
        ConditionRangeStack.Spacing = Math.Clamp(8.5 * topScale, 1, 24);
        var conditionLayout = ComponentTypographyLayoutService.FitAdaptiveTextLayout(
            ConditionTextBlock.Text,
            Math.Max(24, ConditionInfoBadge.MaxWidth - conditionBadge.Padding.Left - conditionBadge.Padding.Right),
            Math.Max(12, bottomZoneHeight * 0.30),
            1,
            1,
            7,
            Math.Max(6, 19 * topScale),
            [ToVariableWeight(Lerp(580, 660, emphasis))],
            1.10);
        var rangeLayout = ComponentTypographyLayoutService.FitAdaptiveTextLayout(
            RangeTextBlock.Text,
            Math.Max(24, ConditionInfoBadge.MaxWidth - conditionBadge.Padding.Left - conditionBadge.Padding.Right),
            Math.Max(12, bottomZoneHeight * 0.30),
            1,
            1,
            7,
            Math.Max(6, 21 * topScale),
            [ToVariableWeight(Lerp(600, 680, emphasis))],
            1.10);
        ConditionTextBlock.FontSize = conditionLayout.FontSize;
        ConditionTextBlock.FontWeight = conditionLayout.Weight;
        ConditionTextBlock.LineHeight = conditionLayout.LineHeight;
        RangeTextBlock.FontSize = rangeLayout.FontSize;
        RangeTextBlock.FontWeight = rangeLayout.Weight;
        RangeTextBlock.LineHeight = rangeLayout.LineHeight;
        ConditionTextBlock.MaxWidth = ConditionInfoBadge.MaxWidth;
        RangeTextBlock.MaxWidth = ConditionInfoBadge.MaxWidth;
        BottomInfoStack.Spacing = Math.Clamp(2.0 * topScale, 0.4, 14);

        WeatherIconImage.Width = iconSize;
        WeatherIconImage.Height = iconSize;
        WeatherIconImage.Margin = new Thickness(0, Math.Clamp(-2.2 * topScale, -10, 0), 0, 0);

        HourlyPanelBorder.Padding = new Thickness(0);
        HourlyPanelBorder.Margin = new Thickness(0, Math.Clamp(6 * fitScale, 1, 24), 0, 0);
        HourlyPanelBorder.CornerRadius = new CornerRadius(0);
        HourlyGrid.ColumnSpacing = Math.Clamp(4 * fitScale, 0.5, 24);

        var hourlyColumnCount = Math.Max(1, _hourlyTimeBlocks.Length);
        var hourlyInnerWidth = Math.Max(
            32,
            innerWidth - (HourlyGrid.ColumnSpacing * (hourlyColumnCount - 1)));
        var hourlyCellWidth = Math.Max(12, hourlyInnerWidth / hourlyColumnCount);
        var hourlyCellScale = Math.Clamp(
            Math.Min((bottomScale * 0.66) + (visualScale * 0.44), hourlyCellWidth / 74d),
            0.22,
            3.60);
        var stackSpacing = Math.Clamp(2 * hourlyCellScale, 0.2, 10);
        var hourlyTempSize = Math.Clamp(19.5 * hourlyCellScale, 6, 72);
        var hourlyTimeSize = Math.Clamp(14.5 * hourlyCellScale, 6, 50);
        var hourlyIconSize = Math.Clamp(42 * hourlyCellScale, 9, 136);
        hourlyIconSize = Math.Min(hourlyIconSize, Math.Max(10, hourlyCellWidth * 0.86));
        hourlyIconSize = Math.Min(hourlyIconSize, Math.Max(10, bottomZoneHeight * 0.52));

        for (var i = 0; i < _hourlyTimeBlocks.Length; i++)
        {
            var hourlyTempLayout = ComponentTypographyLayoutService.FitAdaptiveTextLayout(
                _hourlyTempBlocks[i].Text,
                Math.Clamp(hourlyCellWidth, 12, 240),
                Math.Max(10, hourlyCellScale * 28),
                1,
                1,
                6,
                hourlyTempSize,
                [ToVariableWeight(Lerp(580, 690, emphasis))],
                1.02);
            var hourlyTimeLayout = ComponentTypographyLayoutService.FitAdaptiveTextLayout(
                _hourlyTimeBlocks[i].Text,
                Math.Clamp(hourlyCellWidth, 12, 240),
                Math.Max(10, hourlyCellScale * 24),
                1,
                1,
                6,
                hourlyTimeSize,
                [ToVariableWeight(Lerp(500, 600, emphasis))],
                1.02);
            _hourlyTempBlocks[i].FontSize = hourlyTempLayout.FontSize;
            _hourlyTimeBlocks[i].FontSize = hourlyTimeLayout.FontSize;
            _hourlyIconBlocks[i].Width = hourlyIconSize;
            _hourlyIconBlocks[i].Height = hourlyIconSize;
            _hourlyTimeBlocks[i].MaxWidth = Math.Clamp(hourlyCellWidth, 12, 240);
            _hourlyTempBlocks[i].MaxWidth = Math.Clamp(hourlyCellWidth, 12, 240);
            _hourlyTimeBlocks[i].FontWeight = hourlyTimeLayout.Weight;
            _hourlyTempBlocks[i].FontWeight = hourlyTempLayout.Weight;
            if (_hourlyTimeBlocks[i].Parent is StackPanel hourlyStack)
            {
                hourlyStack.Spacing = stackSpacing;
            }
        }
    }

    private static double Lerp(double from, double to, double t)
    {
        return from + ((to - from) * t);
    }

    private static double ResolveHeroIconScaleBoost(WeatherVisualKind kind)
    {
        return kind switch
        {
            WeatherVisualKind.RainLight or WeatherVisualKind.RainHeavy or WeatherVisualKind.Storm or WeatherVisualKind.Snow => 1.16,
            WeatherVisualKind.ClearNight or WeatherVisualKind.PartlyCloudyNight or WeatherVisualKind.CloudyNight => 1.08,
            WeatherVisualKind.Haze or WeatherVisualKind.Fog => 1.04,
            _ => 1.0
        };
    }

    private void SetMainWeatherIcon(string? assetUri, WeatherVisualKind fallbackKind)
    {
        WeatherIconImage.Source = HyperOS3WeatherAssetLoader.LoadImage(
            assetUri ?? HyperOS3WeatherTheme.ResolveHeroIconAsset(ToThemeKind(fallbackKind)));
    }

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
        _backgroundMotionScaleTransform.ScaleX = scale;
        _backgroundMotionScaleTransform.ScaleY = scale;
        _backgroundMotionTranslateTransform.X = translateX;
        _backgroundMotionTranslateTransform.Y = translateY;
    }

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

            if (!_backgroundAnimationTimer.IsEnabled)
            {
                _backgroundAnimationTimer.Start();
            }

            return;
        }

        _refreshTimer.Stop();
        _backgroundAnimationTimer.Stop();
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
                : _activeVisualKind is WeatherVisualKind.Fog or WeatherVisualKind.Haze
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
            WeatherVisualKind.Fog or WeatherVisualKind.Haze => NextRange(10.0, 22.0),
            _ => NextRange(1.0, 2.2)
        };
        var opacity = _activeVisualKind switch
        {
            WeatherVisualKind.Storm => NextRange(0.26, 0.52),
            WeatherVisualKind.RainHeavy => NextRange(0.24, 0.46),
            WeatherVisualKind.RainLight or WeatherVisualKind.Sleet => NextRange(0.18, 0.34),
            WeatherVisualKind.Snow => NextRange(0.40, 0.72),
            WeatherVisualKind.Fog or WeatherVisualKind.Haze => NextRange(0.08, 0.20),
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
            WeatherVisualKind.RainLight or WeatherVisualKind.Sleet => -14,
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
