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
using FluentIcons.Common;
using LanMontainDesktop.Models;
using LanMontainDesktop.Services;

namespace LanMontainDesktop.Views.Components;

public partial class WeatherWidget : UserControl, IDesktopComponentWidget, ITimeZoneAwareComponentWidget, IWeatherInfoAwareComponentWidget
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

    private static readonly IReadOnlyDictionary<WeatherVisualKind, string> WeatherBackgroundAssets =
        new Dictionary<WeatherVisualKind, string>
        {
            [WeatherVisualKind.ClearDay] = "avares://LanMontainDesktop/Assets/Weather/clear_day.jpg",
            [WeatherVisualKind.ClearNight] = "avares://LanMontainDesktop/Assets/Weather/clear_night.jpg",
            [WeatherVisualKind.CloudyDay] = "avares://LanMontainDesktop/Assets/Weather/cloudy_day.jpg",
            [WeatherVisualKind.CloudyNight] = "avares://LanMontainDesktop/Assets/Weather/cloudy_night.jpg",
            [WeatherVisualKind.RainLight] = "avares://LanMontainDesktop/Assets/Weather/rain_light.jpg",
            [WeatherVisualKind.RainHeavy] = "avares://LanMontainDesktop/Assets/Weather/rain_heavy.jpg",
            [WeatherVisualKind.Storm] = "avares://LanMontainDesktop/Assets/Weather/storm_dark.jpg",
            [WeatherVisualKind.Snow] = "avares://LanMontainDesktop/Assets/Weather/snow_soft.jpg",
            [WeatherVisualKind.Fog] = "avares://LanMontainDesktop/Assets/Weather/fog_haze.jpg"
        };

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

    public WeatherWidget()
    {
        InitializeComponent();

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

    public void SetTimeZoneService(TimeZoneService timeZoneService)
    {
        if (_timeZoneService is not null)
        {
            _timeZoneService.TimeZoneChanged -= OnTimeZoneChanged;
        }

        _timeZoneService = timeZoneService;
        _timeZoneService.TimeZoneChanged += OnTimeZoneChanged;
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
        var cornerRadius = Math.Clamp(_currentCellSize * 0.45, 24, 44);

        RootBorder.CornerRadius = new CornerRadius(cornerRadius);
        BackgroundImageLayer.CornerRadius = new CornerRadius(cornerRadius);
        BackgroundMotionLayer.CornerRadius = new CornerRadius(cornerRadius);
        BackgroundTintLayer.CornerRadius = new CornerRadius(cornerRadius);
        BackgroundLightLayer.CornerRadius = new CornerRadius(cornerRadius);
        BackgroundShadeLayer.CornerRadius = new CornerRadius(cornerRadius);
        ContentPaddingBorder.Padding = new Thickness(Math.Clamp(18 * scale, 12, 24));
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
                // fall through to local clock
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
        WeatherIconSymbol.Symbol = ResolveWeatherSymbol(visualKind);

        TemperatureTextBlock.Text = FormatTemperature(snapshot.Current.TemperatureC);
        var (low, high) = ResolveTemperatureRange(snapshot);
        RangeTextBlock.Text = FormatTemperatureRange(low, high);
        ApplyAdaptiveTypography();
    }

    private void ApplyNotConfiguredState()
    {
        var fallbackKind = ResolveFallbackVisualKind();
        ApplyVisualTheme(fallbackKind);
        WeatherIconSymbol.Symbol = fallbackKind == WeatherVisualKind.ClearNight
            ? Symbol.WeatherMoon
            : Symbol.WeatherSunny;
        CityTextBlock.Text = L("weather.widget.location_not_configured", "Weather location is not configured");
        ConditionTextBlock.Text = L("weather.widget.configure_hint", "Open Settings > Weather to configure");
        TemperatureTextBlock.Text = "--°";
        RangeTextBlock.Text = L("weather.widget.range_unknown", "-- / --");
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
        CityTextBlock.Text = ResolvePreciseDisplayLocation(
            locationName,
            _languageCode,
            L("weather.widget.location_unknown", "Unknown location"));
        ConditionTextBlock.Text = L("weather.widget.loading", "Loading...");
        TemperatureTextBlock.Text = "--°";
        RangeTextBlock.Text = L("weather.widget.range_unknown", "-- / --");
        ApplyAdaptiveTypography();
    }

    private void ApplyFailedState(string locationName)
    {
        ApplyVisualTheme(WeatherVisualKind.Fog);
        WeatherIconSymbol.Symbol = Symbol.WeatherFog;
        CityTextBlock.Text = ResolvePreciseDisplayLocation(
            locationName,
            _languageCode,
            L("weather.widget.location_unknown", "Unknown location"));
        ConditionTextBlock.Text = L("weather.widget.fetch_failed", "Weather fetch failed");
        TemperatureTextBlock.Text = "--°";
        RangeTextBlock.Text = L("weather.widget.range_unknown", "-- / --");
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
        var secondary = CreateSolidBrush(palette.SecondaryText);
        var particleBrush = CreateSolidBrush(palette.ParticleColor);
        LocationIcon.Foreground = primary;
        CityTextBlock.Foreground = primary;
        TemperatureTextBlock.Foreground = primary;
        WeatherIconSymbol.Foreground = primary;
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

        if (WeatherBackgroundAssets.TryGetValue(kind, out var uriText))
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

    private static WeatherVisualKind ResolveVisualKind(int? weatherCode, bool isNight)
    {
        return weatherCode switch
        {
            0 => isNight ? WeatherVisualKind.ClearNight : WeatherVisualKind.ClearDay,
            1 or 2 => isNight ? WeatherVisualKind.CloudyNight : WeatherVisualKind.CloudyDay,
            3 or 7 => WeatherVisualKind.RainLight,
            8 or 9 => WeatherVisualKind.RainHeavy,
            4 => WeatherVisualKind.Storm,
            13 or 14 or 15 or 16 => WeatherVisualKind.Snow,
            18 or 32 => WeatherVisualKind.Fog,
            _ => isNight ? WeatherVisualKind.CloudyNight : WeatherVisualKind.CloudyDay
        };
    }

    private static WeatherVisualPalette ResolvePalette(WeatherVisualKind kind)
    {
        return kind switch
        {
            WeatherVisualKind.ClearDay => new WeatherVisualPalette(
                GradientFrom: "#4F92E8",
                GradientTo: "#83C5FF",
                Tint: "#234D87",
                PrimaryText: "#FFFFFFFF",
                SecondaryText: "#EEF5FF",
                ParticleColor: "#00FFFFFF"),
            WeatherVisualKind.ClearNight => new WeatherVisualPalette(
                GradientFrom: "#0E2B72",
                GradientTo: "#193A85",
                Tint: "#0A1E52",
                PrimaryText: "#FFFFFFFF",
                SecondaryText: "#CFE0FF",
                ParticleColor: "#00FFFFFF"),
            WeatherVisualKind.CloudyDay => new WeatherVisualPalette(
                GradientFrom: "#4A72B3",
                GradientTo: "#6A8EC2",
                Tint: "#2A487C",
                PrimaryText: "#FFFFFFFF",
                SecondaryText: "#EAF2FF",
                ParticleColor: "#16FFFFFF"),
            WeatherVisualKind.CloudyNight => new WeatherVisualPalette(
                GradientFrom: "#102A6B",
                GradientTo: "#193A80",
                Tint: "#0B1F51",
                PrimaryText: "#FFFFFFFF",
                SecondaryText: "#D5E4FF",
                ParticleColor: "#24FFFFFF"),
            WeatherVisualKind.RainLight => new WeatherVisualPalette(
                GradientFrom: "#32588A",
                GradientTo: "#4D74A8",
                Tint: "#1F3454",
                PrimaryText: "#FFFFFFFF",
                SecondaryText: "#E6F0FF",
                ParticleColor: "#88D7E8FF"),
            WeatherVisualKind.RainHeavy => new WeatherVisualPalette(
                GradientFrom: "#253F66",
                GradientTo: "#36567F",
                Tint: "#17263E",
                PrimaryText: "#FFFFFFFF",
                SecondaryText: "#DCE9FF",
                ParticleColor: "#A2CDE1FF"),
            WeatherVisualKind.Storm => new WeatherVisualPalette(
                GradientFrom: "#293A67",
                GradientTo: "#3A4F78",
                Tint: "#161E35",
                PrimaryText: "#FFFFFFFF",
                SecondaryText: "#DCE4F8",
                ParticleColor: "#A8C2D6F2"),
            WeatherVisualKind.Snow => new WeatherVisualPalette(
                GradientFrom: "#D1E8FF",
                GradientTo: "#A7D0F4",
                Tint: "#607C9D",
                PrimaryText: "#FF10253D",
                SecondaryText: "#FF2B435E",
                ParticleColor: "#CCFFFFFF"),
            _ => new WeatherVisualPalette(
                GradientFrom: "#445B7A",
                GradientTo: "#5B738F",
                Tint: "#2A3E56",
                PrimaryText: "#FFFFFFFF",
                SecondaryText: "#E7EDF6",
                ParticleColor: "#88E4EDF7")
        };
    }

    private static Symbol ResolveWeatherSymbol(WeatherVisualKind kind)
    {
        return kind switch
        {
            WeatherVisualKind.ClearDay => Symbol.WeatherSunny,
            WeatherVisualKind.ClearNight => Symbol.WeatherMoon,
            WeatherVisualKind.CloudyDay => Symbol.WeatherPartlyCloudyDay,
            WeatherVisualKind.CloudyNight => Symbol.WeatherPartlyCloudyNight,
            WeatherVisualKind.RainLight => Symbol.WeatherRainShowersDay,
            WeatherVisualKind.RainHeavy => Symbol.WeatherRain,
            WeatherVisualKind.Storm => Symbol.WeatherThunderstorm,
            WeatherVisualKind.Snow => Symbol.WeatherSnow,
            _ => Symbol.WeatherFog
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

    private static string FormatTemperature(double? value)
    {
        if (!value.HasValue || double.IsNaN(value.Value) || double.IsInfinity(value.Value))
        {
            return "--";
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
        var scale = ResolveScale();
        var densityBoost = scale <= 0.70 ? 0.88 : scale <= 0.88 ? 0.94 : scale >= 1.45 ? 1.06 : 1.0;
        var cityLength = Math.Max(1, CityTextBlock.Text?.Length ?? 2);
        var cityCompression = cityLength >= 10 ? 0.72 : cityLength >= 7 ? 0.83 : cityLength >= 5 ? 0.92 : 1.0;
        var conditionLength = Math.Max(1, ConditionTextBlock.Text?.Length ?? 2);
        var conditionCompression = conditionLength >= 9 ? 0.84 : conditionLength >= 6 ? 0.92 : 1.0;

        ContentGrid.RowSpacing = Math.Clamp(8 * scale, 4, 14);
        TopRowGrid.ColumnSpacing = Math.Clamp(8 * scale, 4, 12);
        BottomInfoStack.Spacing = Math.Clamp(4 * scale, 2, 8);
        BottomInfoStack.Margin = new Thickness(0, 0, 0, Math.Clamp(10 * scale, 4, 16));

        LocationIcon.FontSize = Math.Clamp(20 * scale * densityBoost, 10, 30);
        CityTextBlock.FontSize = Math.Clamp(30 * scale * cityCompression * densityBoost, 12, 42);
        WeatherIconSymbol.FontSize = Math.Clamp(40 * scale * densityBoost, 14, 56);
        TemperatureTextBlock.FontSize = Math.Clamp(108 * scale * densityBoost, 36, 144);
        ConditionTextBlock.FontSize = Math.Clamp(30 * scale * conditionCompression * densityBoost, 11, 44);
        RangeTextBlock.FontSize = Math.Clamp(36 * scale * densityBoost, 12, 50);
        TemperatureTextBlock.Margin = new Thickness(0, Math.Clamp(4 * scale, 1, 8), 0, Math.Clamp(10 * scale, 4, 16));

        CityTextBlock.FontWeight = ToVariableWeight(Lerp(560, 700, Math.Clamp((scale - 0.58) / 1.3, 0, 1)));
        TemperatureTextBlock.FontWeight = ToVariableWeight(Lerp(620, 800, Math.Clamp((scale - 0.58) / 1.2, 0, 1)));
        ConditionTextBlock.FontWeight = ToVariableWeight(Lerp(560, 700, Math.Clamp((scale - 0.58) / 1.2, 0, 1)));
        RangeTextBlock.FontWeight = ToVariableWeight(Lerp(560, 700, Math.Clamp((scale - 0.58) / 1.2, 0, 1)));
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
        return kind switch
        {
            WeatherVisualKind.ClearDay => new WeatherMotionProfile(
                DriftX: 8.0, DriftY: 4.0, ZoomBase: 1.055, ZoomAmplitude: 0.012,
                MotionOpacityBase: 0.22, MotionOpacityPulse: 0.05,
                LightOpacityBase: 0.68, LightOpacityPulse: 0.08,
                ShadeOpacityBase: 0.72, ShadeOpacityPulse: 0.03,
                PhaseStep: 0.015, ParticleCount: 0,
                ParticleSpeedMin: 0, ParticleSpeedMax: 0,
                ParticleLengthMin: 0, ParticleLengthMax: 0, ParticleDriftPerTick: 0),
            WeatherVisualKind.ClearNight => new WeatherMotionProfile(
                DriftX: 10.0, DriftY: 6.0, ZoomBase: 1.060, ZoomAmplitude: 0.014,
                MotionOpacityBase: 0.28, MotionOpacityPulse: 0.06,
                LightOpacityBase: 0.58, LightOpacityPulse: 0.07,
                ShadeOpacityBase: 0.82, ShadeOpacityPulse: 0.04,
                PhaseStep: 0.018, ParticleCount: 0,
                ParticleSpeedMin: 0, ParticleSpeedMax: 0,
                ParticleLengthMin: 0, ParticleLengthMax: 0, ParticleDriftPerTick: 0),
            WeatherVisualKind.CloudyDay => new WeatherMotionProfile(
                DriftX: 12.0, DriftY: 7.0, ZoomBase: 1.060, ZoomAmplitude: 0.013,
                MotionOpacityBase: 0.32, MotionOpacityPulse: 0.06,
                LightOpacityBase: 0.62, LightOpacityPulse: 0.07,
                ShadeOpacityBase: 0.80, ShadeOpacityPulse: 0.03,
                PhaseStep: 0.020, ParticleCount: 6,
                ParticleSpeedMin: 0.30, ParticleSpeedMax: 0.70,
                ParticleLengthMin: 14, ParticleLengthMax: 28, ParticleDriftPerTick: 0.10),
            WeatherVisualKind.CloudyNight => new WeatherMotionProfile(
                DriftX: 14.0, DriftY: 8.0, ZoomBase: 1.065, ZoomAmplitude: 0.013,
                MotionOpacityBase: 0.34, MotionOpacityPulse: 0.07,
                LightOpacityBase: 0.54, LightOpacityPulse: 0.06,
                ShadeOpacityBase: 0.85, ShadeOpacityPulse: 0.03,
                PhaseStep: 0.021, ParticleCount: 8,
                ParticleSpeedMin: 0.35, ParticleSpeedMax: 0.80,
                ParticleLengthMin: 16, ParticleLengthMax: 30, ParticleDriftPerTick: 0.12),
            WeatherVisualKind.RainLight => new WeatherMotionProfile(
                DriftX: 6.0, DriftY: 10.0, ZoomBase: 1.050, ZoomAmplitude: 0.010,
                MotionOpacityBase: 0.30, MotionOpacityPulse: 0.08,
                LightOpacityBase: 0.50, LightOpacityPulse: 0.04,
                ShadeOpacityBase: 0.84, ShadeOpacityPulse: 0.04,
                PhaseStep: 0.030, ParticleCount: 18,
                ParticleSpeedMin: 1.80, ParticleSpeedMax: 3.20,
                ParticleLengthMin: 14, ParticleLengthMax: 26, ParticleDriftPerTick: 0.70),
            WeatherVisualKind.RainHeavy => new WeatherMotionProfile(
                DriftX: 5.0, DriftY: 11.0, ZoomBase: 1.045, ZoomAmplitude: 0.010,
                MotionOpacityBase: 0.34, MotionOpacityPulse: 0.10,
                LightOpacityBase: 0.42, LightOpacityPulse: 0.03,
                ShadeOpacityBase: 0.88, ShadeOpacityPulse: 0.05,
                PhaseStep: 0.036, ParticleCount: 30,
                ParticleSpeedMin: 2.80, ParticleSpeedMax: 4.80,
                ParticleLengthMin: 18, ParticleLengthMax: 34, ParticleDriftPerTick: 0.92),
            WeatherVisualKind.Storm => new WeatherMotionProfile(
                DriftX: 4.0, DriftY: 12.0, ZoomBase: 1.042, ZoomAmplitude: 0.012,
                MotionOpacityBase: 0.38, MotionOpacityPulse: 0.12,
                LightOpacityBase: 0.36, LightOpacityPulse: 0.02,
                ShadeOpacityBase: 0.91, ShadeOpacityPulse: 0.04,
                PhaseStep: 0.042, ParticleCount: 34,
                ParticleSpeedMin: 3.60, ParticleSpeedMax: 5.80,
                ParticleLengthMin: 20, ParticleLengthMax: 36, ParticleDriftPerTick: 1.08),
            WeatherVisualKind.Snow => new WeatherMotionProfile(
                DriftX: 9.0, DriftY: 7.0, ZoomBase: 1.055, ZoomAmplitude: 0.012,
                MotionOpacityBase: 0.28, MotionOpacityPulse: 0.06,
                LightOpacityBase: 0.74, LightOpacityPulse: 0.08,
                ShadeOpacityBase: 0.68, ShadeOpacityPulse: 0.03,
                PhaseStep: 0.020, ParticleCount: 24,
                ParticleSpeedMin: 0.60, ParticleSpeedMax: 1.60,
                ParticleLengthMin: 3.0, ParticleLengthMax: 8.5, ParticleDriftPerTick: 0.24),
            _ => new WeatherMotionProfile(
                DriftX: 7.0, DriftY: 5.0, ZoomBase: 1.050, ZoomAmplitude: 0.011,
                MotionOpacityBase: 0.30, MotionOpacityPulse: 0.05,
                LightOpacityBase: 0.58, LightOpacityPulse: 0.05,
                ShadeOpacityBase: 0.86, ShadeOpacityPulse: 0.03,
                PhaseStep: 0.018, ParticleCount: 10,
                ParticleSpeedMin: 0.25, ParticleSpeedMax: 0.70,
                ParticleLengthMin: 16, ParticleLengthMax: 34, ParticleDriftPerTick: 0.12)
        };
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
