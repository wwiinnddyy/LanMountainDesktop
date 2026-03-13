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
using LanMountainDesktop.ComponentSystem;
using LanMountainDesktop.Models;
using LanMountainDesktop.Services;
using LanMountainDesktop.Theme;

namespace LanMountainDesktop.Views.Components;

public partial class WeatherWidget : UserControl, IDesktopComponentWidget, IDesktopPageVisibilityAwareComponentWidget, ITimeZoneAwareComponentWidget, IWeatherInfoAwareComponentWidget, IComponentPlacementContextAware
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

    private sealed record WeatherWidgetConfig(
        string LanguageCode,
        string Locale,
        string LocationKey,
        string LocationName,
        double Latitude,
        double Longitude);

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

    private readonly AppSettingsService _settingsService = new();
    private IComponentInstanceSettingsStore _componentSettingsStore = new ComponentSettingsService();
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
    private WeatherVisualKind _activeVisualKind = WeatherVisualKind.ClearDay;
    private double _animationPhase;
    private int _activeParticleCount;
    private bool _isAttached;
    private bool _isOnActivePage = true;
    private bool _isRefreshing;
    private bool _autoRefreshEnabled = true;
    private string _componentId = BuiltInComponentIds.DesktopWeather;
    private string _placementId = string.Empty;

    public WeatherWidget()
    {
        InitializeComponent();
        InitializeMotionTransform();

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
            ? BuiltInComponentIds.DesktopWeather
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

    public void ApplyCellSize(double cellSize)
    {
        _currentCellSize = Math.Max(1, cellSize);
        var scale = ResolveScale();
        var metrics = HyperOS3WeatherTheme.ResolveMetrics(HyperOS3WeatherWidgetKind.Realtime2x2);
        var hostWidth = Bounds.Width > 1 ? Bounds.Width : Math.Max(80, _currentCellSize * 2);
        var hostHeight = Bounds.Height > 1 ? Bounds.Height : Math.Max(80, _currentCellSize * 2);
        var cornerRadius = Math.Clamp(_currentCellSize * metrics.CornerRadiusScale, 26, 46);
        var horizontalPadding = Math.Clamp(_currentCellSize * metrics.HorizontalPaddingScale, 10, 24);
        var verticalPadding = Math.Clamp(_currentCellSize * metrics.VerticalPaddingScale, 10, 24);

        RootBorder.CornerRadius = new CornerRadius(cornerRadius);
        BackgroundImageLayer.CornerRadius = new CornerRadius(cornerRadius);
        BackgroundMotionLayer.CornerRadius = new CornerRadius(cornerRadius);
        BackgroundTintLayer.CornerRadius = new CornerRadius(cornerRadius);
        BackgroundLightLayer.CornerRadius = new CornerRadius(cornerRadius);
        BackgroundShadeLayer.CornerRadius = new CornerRadius(cornerRadius);
        ContentPaddingBorder.Padding = new Thickness(
            Math.Clamp(Math.Min(horizontalPadding * scale, hostWidth * 0.12), 3, 24),
            Math.Clamp(Math.Min(verticalPadding * scale, hostHeight * 0.12), 3, 24));
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

    private WeatherWidgetConfig LoadConfig()
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

        return new WeatherWidgetConfig(
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
        SetWeatherIcon(visualKind);
        SetLoadingSkeleton(false);

        TemperatureTextBlock.Text = FormatTemperature(snapshot.Current.TemperatureC);
        var (low, high) = ResolveTemperatureRange(snapshot);
        RangeTextBlock.Text = FormatTemperatureRange(low, high);
        ApplyAdaptiveTypography();
    }

    private void ApplyNotConfiguredState()
    {
        var fallbackKind = ResolveFallbackVisualKind();
        ApplyVisualTheme(fallbackKind);
        SetWeatherIcon(fallbackKind);
        SetLoadingSkeleton(false);
        CityTextBlock.Text = L("weather.widget.location_not_configured", "Weather location is not configured");
        ConditionTextBlock.Text = L("weather.widget.configure_hint", "Open Settings > Weather to configure");
        TemperatureTextBlock.Text = "--°";
        RangeTextBlock.Text = "--°/--°";
        ApplyAdaptiveTypography();
        _latestSnapshot = null;
    }

    private void ApplyLoadingState(string locationName)
    {
        var loadingKind = IsNightNow() ? WeatherVisualKind.CloudyNight : WeatherVisualKind.CloudyDay;
        ApplyVisualTheme(loadingKind);
        SetWeatherIcon(loadingKind);
        SetLoadingSkeleton(true);
        CityTextBlock.Text = ResolvePreciseDisplayLocation(
            locationName,
            _languageCode,
            L("weather.widget.location_unknown", "Unknown location"));
        ConditionTextBlock.Text = L("weather.widget.loading", "Loading...");
        TemperatureTextBlock.Text = "--°";
        RangeTextBlock.Text = "--°/--°";
        ApplyAdaptiveTypography();
    }

    private void ApplyFailedState(string locationName)
    {
        ApplyVisualTheme(WeatherVisualKind.Fog);
        SetWeatherIcon(WeatherVisualKind.Fog);
        SetLoadingSkeleton(false);
        CityTextBlock.Text = ResolvePreciseDisplayLocation(
            locationName,
            _languageCode,
            L("weather.widget.location_unknown", "Unknown location"));
        ConditionTextBlock.Text = L("weather.widget.fetch_failed", "Weather fetch failed");
        TemperatureTextBlock.Text = "--°";
        RangeTextBlock.Text = "--°/--°";
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

        var isNightVisual = kind is WeatherVisualKind.ClearNight or WeatherVisualKind.CloudyNight;
        var backgroundSamples = WeatherTypographyAccessibility.BuildBackgroundSamples(
            palette.GradientFrom,
            palette.GradientTo,
            palette.Tint,
            isNightVisual);
        var primary = WeatherTypographyAccessibility.CreateReadableBrush(
            palette.PrimaryText,
            backgroundSamples,
            WeatherTypographyAccessibility.WcagLargeTextContrast);
        var secondary = WeatherTypographyAccessibility.CreateReadableBrush(
            palette.SecondaryText,
            backgroundSamples,
            WeatherTypographyAccessibility.WcagNormalTextContrast,
            isNightVisual ? (byte)0xEE : (byte)0xE0);
        var tertiary = WeatherTypographyAccessibility.CreateReadableBrush(
            palette.TertiaryText,
            backgroundSamples,
            WeatherTypographyAccessibility.WcagNormalTextContrast,
            isNightVisual ? (byte)0xC4 : (byte)0xAE);
        var particleBrush = ResolveParticleBrush(ToThemeKind(kind), palette.ParticleColor);
        LocationIcon.Foreground = tertiary;
        CityTextBlock.Foreground = tertiary;
        TemperatureTextBlock.Foreground = primary;
        ConditionTextBlock.Foreground = secondary;
        RangeTextBlock.Foreground = secondary;

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
        var width = Bounds.Width > 1 ? Bounds.Width : _currentCellSize * 2;
        var height = Bounds.Height > 1 ? Bounds.Height : _currentCellSize * 2;
        var innerWidth = Math.Max(56, width - ContentPaddingBorder.Padding.Left - ContentPaddingBorder.Padding.Right);
        var innerHeight = Math.Max(56, height - ContentPaddingBorder.Padding.Top - ContentPaddingBorder.Padding.Bottom);
        var fitScale = Math.Clamp(Math.Min(innerWidth / 288d, innerHeight / 288d), 0.30, 3.20);
        var cellScale = Math.Clamp(_currentCellSize / 44d, 0.34, 3.80);
        var visualScale = Math.Clamp((fitScale * 0.72) + (cellScale * 0.28), 0.30, 3.80);
        var emphasis = Math.Clamp((visualScale - 0.82) / 1.90, 0, 1);

        ContentGrid.RowSpacing = Math.Clamp(2.2 * fitScale, 0.5, 9);
        TopRowGrid.ColumnSpacing = Math.Clamp(6.0 * fitScale, 2, 20);

        var availableHeight = Math.Max(40, innerHeight - (ContentGrid.RowSpacing * 2));
        var topZoneHeight = Math.Clamp(availableHeight * 0.60, 22, Math.Max(22, availableHeight - 16));
        var bottomZoneHeight = Math.Max(12, availableHeight - topZoneHeight - 2);

        if (ContentGrid.RowDefinitions.Count >= 3)
        {
            ContentGrid.RowDefinitions[0].Height = new GridLength(topZoneHeight, GridUnitType.Pixel);
            ContentGrid.RowDefinitions[1].Height = new GridLength(1, GridUnitType.Star);
            ContentGrid.RowDefinitions[2].Height = new GridLength(bottomZoneHeight, GridUnitType.Pixel);
        }

        var topScale = Math.Clamp(((topZoneHeight / 170d) * 0.42) + (visualScale * 0.84), 0.24, 4.00);
        var bottomScale = Math.Clamp(((bottomZoneHeight / 84d) * 0.46) + (visualScale * 0.66), 0.24, 3.90);
        var iconGrowth = Math.Clamp((visualScale - 0.88) / 1.70, 0, 1);
        var iconScaleBoost = ResolveHeroIconScaleBoost(_activeVisualKind);
        var iconSize = Math.Clamp(Lerp(96, 124, iconGrowth) * topScale * iconScaleBoost, 18, 360);
        iconSize = Math.Min(iconSize, Math.Max(18, innerWidth * Lerp(0.34, 0.44, iconGrowth)));
        WeatherIconImage.Width = iconSize;
        WeatherIconImage.Height = iconSize;
        WeatherIconImage.Margin = new Thickness(0, Math.Clamp(-4.2 * topScale, -14, 0), 0, 0);

        var temperatureSample = string.IsNullOrWhiteSpace(TemperatureTextBlock.Text)
            ? "00°"
            : TemperatureTextBlock.Text.Trim();
        var temperatureGlyphCount = Math.Clamp(temperatureSample.Length, 3, 6);
        var temperatureMaxWidth = Math.Max(34, innerWidth - iconSize - TopRowGrid.ColumnSpacing - 2);
        var rawTemperatureSize = Math.Clamp(Lerp(94, 118, iconGrowth) * topScale, 22, 340);
        var fitTemperatureSize = temperatureMaxWidth / (temperatureGlyphCount * 0.62);
        TemperatureTextBlock.FontSize = Math.Clamp(Math.Min(rawTemperatureSize, fitTemperatureSize), 10, 340);
        TemperatureTextBlock.FontWeight = ToVariableWeight(Lerp(300, 360, emphasis));
        TemperatureTextBlock.Margin = new Thickness(Math.Clamp(-1.4 * topScale, -6, 0), Math.Clamp(-7.6 * topScale, -16, -1), 0, 0);
        TemperatureTextBlock.MaxWidth = Math.Clamp(temperatureMaxWidth, 34, Math.Max(34, innerWidth * 0.76));

        var bottomStackSpacing = Math.Clamp(1.2 * bottomScale, 0.6, 8);
        BottomInfoStack.Spacing = bottomStackSpacing;
        BottomInfoStack.Margin = new Thickness(0, 0, 0, Math.Clamp(1.4 * fitScale, 0, 6));
        BottomInfoStack.MaxHeight = Math.Max(10, bottomZoneHeight);

        var bottomTextMaxWidth = Math.Min(innerWidth, Math.Max(36, innerWidth * 0.86));
        var conditionStackSpacing = Math.Clamp(1.2 + (2.0 * bottomScale), 0.5, 12);
        ConditionStack.Spacing = conditionStackSpacing;
        ConditionStack.Margin = new Thickness(0);
        var infoFontSize = Math.Clamp(27 * bottomScale, 7, 86);
        const double infoLineHeightFactor = 1.10;
        var estimatedBottomUsedHeight =
            (infoFontSize * infoLineHeightFactor * 3) +
            conditionStackSpacing +
            bottomStackSpacing +
            2;
        if (estimatedBottomUsedHeight > bottomZoneHeight)
        {
            var shrink = Math.Clamp(bottomZoneHeight / estimatedBottomUsedHeight, 0.36, 1.0);
            infoFontSize = Math.Max(6, infoFontSize * shrink);
            conditionStackSpacing = Math.Max(0.3, conditionStackSpacing * shrink);
            bottomStackSpacing = Math.Max(0.3, bottomStackSpacing * shrink);
            ConditionStack.Spacing = conditionStackSpacing;
            BottomInfoStack.Spacing = bottomStackSpacing;
        }

        var infoFontWeight = ToVariableWeight(Lerp(580, 690, emphasis));
        ConditionTextBlock.FontSize = Math.Max(6, infoFontSize * 0.96);
        ConditionTextBlock.FontWeight = infoFontWeight;
        ConditionTextBlock.LineHeight = ConditionTextBlock.FontSize * infoLineHeightFactor;
        ConditionTextBlock.MaxWidth = bottomTextMaxWidth;
        RangeTextBlock.FontSize = Math.Max(6, infoFontSize * 1.03);
        RangeTextBlock.FontWeight = infoFontWeight;
        RangeTextBlock.LineHeight = RangeTextBlock.FontSize * infoLineHeightFactor;
        RangeTextBlock.MaxWidth = bottomTextMaxWidth;

        CityInfoBadge.Padding = new Thickness(0);
        CityInfoBadge.CornerRadius = new CornerRadius(0);
        CityInfoBadge.MaxWidth = bottomTextMaxWidth;
        LocationIcon.FontSize = Math.Clamp(
            12 * bottomScale,
            6,
            34);
        LocationIcon.FontSize = Math.Min(LocationIcon.FontSize, infoFontSize * 0.72);
        CityTextBlock.FontSize = Math.Max(6, infoFontSize * 0.84);
        CityTextBlock.FontWeight = ToVariableWeight(Lerp(500, 620, emphasis));
        CityTextBlock.LineHeight = CityTextBlock.FontSize * infoLineHeightFactor;
        CityTextBlock.MaxWidth = bottomTextMaxWidth;
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
            WeatherVisualKind.ClearNight or WeatherVisualKind.CloudyNight => 1.08,
            _ => 1.0
        };
    }

    private void SetWeatherIcon(WeatherVisualKind kind)
    {
        WeatherIconImage.Source = HyperOS3WeatherAssetLoader.LoadImage(
            HyperOS3WeatherTheme.ResolveHeroIconAsset(ToThemeKind(kind)));
    }

    private void SetLoadingSkeleton(bool isLoading)
    {
        var opacity = isLoading ? 0.58 : 1.0;
        TemperatureTextBlock.Opacity = opacity;
        ConditionTextBlock.Opacity = opacity;
        RangeTextBlock.Opacity = opacity;
        CityTextBlock.Opacity = isLoading ? 0.45 : 0.96;
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

        var width = Math.Max(Bounds.Width, 300);
        var height = Math.Max(Bounds.Height, 300);

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

        var width = Math.Max(Bounds.Width, 300);
        var height = Math.Max(Bounds.Height, 300);

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

    private double ResolveScale()
    {
        var cellScale = Math.Clamp(_currentCellSize / 44d, 0.60, 2.0);
        var heightScale = Bounds.Height > 1 ? Math.Clamp(Bounds.Height / 300d, 0.58, 2.0) : 1;
        var widthScale = Bounds.Width > 1 ? Math.Clamp(Bounds.Width / 300d, 0.58, 2.0) : 1;
        return Math.Clamp(Math.Min(cellScale, Math.Min(heightScale, widthScale) * 1.05), 0.58, 2.0);
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
