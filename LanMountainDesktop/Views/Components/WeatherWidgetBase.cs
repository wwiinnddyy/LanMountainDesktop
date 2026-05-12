using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using LanMountainDesktop.ComponentSystem;
using LanMountainDesktop.Host.Abstractions;
using LanMountainDesktop.Models;
using LanMountainDesktop.PluginSdk;
using LanMountainDesktop.Services;
using LanMountainDesktop.Services.Settings;

namespace LanMountainDesktop.Views.Components;

public enum WeatherWidgetState
{
    Loading,
    Ready,
    MissingLocation,
    Error,
    Preview
}

public abstract class WeatherWidgetBase : UserControl,
    IDesktopComponentWidget,
    IDesktopPageVisibilityAwareComponentWidget,
    IWeatherInfoAwareComponentWidget,
    IComponentRuntimeContextAware,
    IComponentPlacementContextAware,
    IComponentChromeContextAware
{
    private readonly DispatcherTimer _refreshTimer = new();
    private CancellationTokenSource? _refreshCancellation;
    private bool _isAttached;
    private bool _isOnActivePage = true;
    private bool _isEditMode;
    private bool _hasStarted;
    private bool _isSettingsSubscribed;
    private DesktopComponentRenderMode _renderMode = DesktopComponentRenderMode.Live;

    private ISettingsFacadeService _settingsFacade = HostSettingsFacadeProvider.GetOrCreate();
    private ISettingsService _settingsService = HostSettingsFacadeProvider.GetOrCreate().Settings;
    private IWeatherInfoService _weatherInfoService = CreateDefaultWeatherInfoService();
    private string _componentId = string.Empty;
    private string? _placementId;
    private double _cellSize = 64;

    protected WeatherWidgetBase()
    {
        _refreshTimer.Tick += (_, _) => _ = RefreshWeatherAsync(forceRefresh: true);
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
        SizeChanged += (_, _) => ApplyCellSize(_cellSize);
    }

    protected WeatherWidgetState State { get; private set; } = WeatherWidgetState.Loading;

    protected WeatherSnapshot? Snapshot { get; private set; }

    protected string DisplayLocation { get; private set; } = "Weather";

    protected string StatusText { get; private set; } = "Loading";

    protected MaterialWeatherCondition CurrentCondition { get; private set; } = MaterialWeatherCondition.Clear;

    protected MaterialWeatherPalette CurrentPalette { get; private set; } = MaterialWeatherVisualTheme.ResolvePalette(MaterialWeatherCondition.Clear, false);

    protected string CurrentVisualStyleId { get; private set; } = WeatherVisualStyleId.Default;

    protected bool IsLiveRenderMode => _renderMode == DesktopComponentRenderMode.Live;

    protected double CurrentCellSize => _cellSize;

    protected abstract MaterialWeatherSceneControl SceneControl { get; }

    public virtual void ApplyCellSize(double cellSize)
    {
        _cellSize = Math.Max(24, cellSize);
        ApplyResponsiveLayout(_cellSize);
    }

    public void SetWeatherInfoService(IWeatherInfoService weatherInfoService)
    {
        _weatherInfoService = weatherInfoService ?? CreateDefaultWeatherInfoService();
        StartIfReady();
    }

    public void SetComponentRuntimeContext(DesktopComponentRuntimeContext context)
    {
        UnsubscribeSettings();
        _settingsFacade = context.SettingsFacade;
        _settingsService = context.SettingsService;
        _componentId = context.ComponentId;
        _placementId = context.PlacementId;
        _renderMode = context.RenderMode;
        SubscribeSettings();
        StartIfReady();
    }

    public void SetComponentPlacementContext(string componentId, string? placementId)
    {
        _componentId = string.IsNullOrWhiteSpace(componentId) ? _componentId : componentId.Trim();
        _placementId = placementId;
        ConfigureRefreshTimer();
    }

    public virtual void SetComponentChromeContext(ComponentChromeContext context)
    {
        ApplyCellSize(context.CellSize);
    }

    public void SetDesktopPageContext(bool isOnActivePage, bool isEditMode)
    {
        _isOnActivePage = isOnActivePage;
        _isEditMode = isEditMode;
        UpdateAnimationState();
        if (_isOnActivePage)
        {
            StartIfReady();
        }
    }

    protected abstract void ApplyResponsiveLayout(double cellSize);

    protected abstract void RenderWeather();

    private static IWeatherInfoService CreateDefaultWeatherInfoService()
    {
        return HostSettingsFacadeProvider.GetOrCreate().Weather.GetWeatherInfoService();
    }

    protected virtual WeatherSnapshot CreatePreviewSnapshot()
    {
        var now = DateTimeOffset.Now;
        return new WeatherSnapshot(
            Provider: "Preview",
            LocationKey: "preview",
            LocationName: "Material City",
            Latitude: 0,
            Longitude: 0,
            FetchedAt: now,
            ObservationTime: now,
            Current: new WeatherCurrentCondition(24, 25, 58, 42, 12, 180, 1, true, "Partly cloudy"),
            DailyForecasts:
            [
                new WeatherDailyForecast(DateOnly.FromDateTime(DateTime.Today), 20, 28, 1, "Partly cloudy", 2, "Cloudy", "06:10", "18:40", 20),
                new WeatherDailyForecast(DateOnly.FromDateTime(DateTime.Today.AddDays(1)), 19, 26, 7, "Light rain", 7, "Light rain", "06:11", "18:40", 60),
                new WeatherDailyForecast(DateOnly.FromDateTime(DateTime.Today.AddDays(2)), 18, 25, 2, "Cloudy", 2, "Cloudy", "06:12", "18:39", 30),
                new WeatherDailyForecast(DateOnly.FromDateTime(DateTime.Today.AddDays(3)), 21, 30, 0, "Clear", 1, "Partly cloudy", "06:12", "18:38", 10),
                new WeatherDailyForecast(DateOnly.FromDateTime(DateTime.Today.AddDays(4)), 20, 29, 1, "Partly cloudy", 1, "Partly cloudy", "06:13", "18:37", 10)
            ],
            HourlyForecasts: Enumerable.Range(0, 8)
                .Select(i => new WeatherHourlyForecast(now.AddHours(i), 24 + (i % 4), i == 2 ? 7 : 1, i == 2 ? "Rain" : "Cloudy"))
                .ToArray());
    }

    protected string FormatTemperature(double? value)
    {
        return value.HasValue
            ? string.Format(CultureInfo.InvariantCulture, "{0:0.#}\u00b0", value.Value)
            : "--\u00b0";
    }

    protected string FormatTemperatureWithUnit(double? value)
    {
        return value.HasValue
            ? string.Format(CultureInfo.InvariantCulture, "{0:0.#}\u00b0C", value.Value)
            : "--";
    }

    protected string FormatRange(WeatherSnapshot? snapshot)
    {
        var daily = snapshot?.DailyForecasts.FirstOrDefault();
        return daily is null
            ? "-- / --"
            : $"{FormatTemperature(daily.LowTemperatureC)} / {FormatTemperature(daily.HighTemperatureC)}";
    }

    protected string FormatTime(DateTimeOffset time)
    {
        return time.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture);
    }

    protected string ResolveDayLabel(DateOnly date)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        if (date == today) return "Today";
        if (date == today.AddDays(1)) return "Tomorrow";
        return date.ToString("ddd", CultureInfo.InvariantCulture);
    }

    protected IBrush Brush(Color color, double opacity = 1)
    {
        return new SolidColorBrush(color, opacity);
    }

    protected void ApplyCurrentScene()
    {
        SceneControl.Apply(CurrentVisualStyleId, CurrentCondition, CurrentPalette, IsLiveRenderMode && _isAttached && _isOnActivePage && !_isEditMode);
    }

    protected string ResolveIconKey(int? weatherCode, string? weatherText, bool isDaylight = true)
    {
        return WeatherIconAssetResolver.ResolveIconKey(weatherCode, weatherText, isDaylight);
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _isAttached = true;
        SubscribeSettings();
        StartIfReady();
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _isAttached = false;
        _refreshTimer.Stop();
        _refreshCancellation?.Cancel();
        UnsubscribeSettings();
        UpdateAnimationState();
    }

    private void StartIfReady()
    {
        if (!_isAttached || _hasStarted)
        {
            UpdateAnimationState();
            return;
        }

        _hasStarted = true;
        ConfigureRefreshTimer();

        if (!IsLiveRenderMode)
        {
            ApplySnapshot(CreatePreviewSnapshot(), WeatherWidgetState.Preview, "Material City", "Preview");
            return;
        }

        _ = RefreshWeatherAsync(forceRefresh: false);
    }

    private void ConfigureRefreshTimer()
    {
        var (enabled, interval) = ResolveRefreshSettings();
        _refreshTimer.Interval = TimeSpan.FromMinutes(interval);
        if (_isAttached && IsLiveRenderMode && enabled)
        {
            _refreshTimer.Start();
        }
        else
        {
            _refreshTimer.Stop();
        }
    }

    private async Task RefreshWeatherAsync(bool forceRefresh)
    {
        if (!_isAttached || !IsLiveRenderMode || !_isOnActivePage)
        {
            return;
        }

        var config = ResolveWeatherConfig();
        if (string.IsNullOrWhiteSpace(config.LocationKey))
        {
            ApplySnapshot(null, WeatherWidgetState.MissingLocation, "Weather", "Set location in Settings");
            return;
        }

        ApplySnapshot(Snapshot, Snapshot is null ? WeatherWidgetState.Loading : WeatherWidgetState.Ready, config.LocationName, "Loading");

        _refreshCancellation?.Cancel();
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        _refreshCancellation = cts;

        try
        {
            var language = _settingsFacade.Region.Get().LanguageCode;
            var result = await _weatherInfoService.GetWeatherAsync(
                new WeatherQuery(
                    config.LocationKey,
                    config.Latitude,
                    config.Longitude,
                    ForecastDays: 7,
                    Locale: NormalizeWeatherLocale(language),
                    ForceRefresh: forceRefresh),
                cts.Token);

            if (result.Success && result.Data is not null)
            {
                ApplySnapshot(result.Data, WeatherWidgetState.Ready, ResolveLocationName(result.Data, config.LocationName), "Ready");
                return;
            }

            ApplySnapshot(Snapshot, WeatherWidgetState.Error, config.LocationName, "Weather unavailable");
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            ApplySnapshot(Snapshot, WeatherWidgetState.Error, config.LocationName, "Weather unavailable");
        }
    }

    private void ApplySnapshot(WeatherSnapshot? snapshot, WeatherWidgetState state, string location, string status)
    {
        Snapshot = snapshot;
        State = state;
        DisplayLocation = string.IsNullOrWhiteSpace(location) ? "Weather" : location.Trim();
        StatusText = status;

        var isNight = snapshot?.Current.IsDaylight.HasValue == true
            ? !snapshot.Current.IsDaylight.Value
            : _settingsFacade.Theme.Get().IsNightMode;
        CurrentVisualStyleId = WeatherVisualStyleCatalog.Normalize(_settingsFacade.Weather.Get().IconPackId);
        CurrentCondition = MaterialWeatherVisualTheme.ResolveCondition(snapshot);
        CurrentPalette = MaterialWeatherVisualTheme.ResolvePalette(CurrentVisualStyleId, CurrentCondition, isNight);
        ApplyCurrentScene();
        RenderWeather();
    }

    private void SubscribeSettings()
    {
        if (!_isAttached || _isSettingsSubscribed)
        {
            return;
        }

        _settingsService.Changed += OnSettingsChanged;
        _isSettingsSubscribed = true;
    }

    private void UnsubscribeSettings()
    {
        if (!_isSettingsSubscribed)
        {
            return;
        }

        _settingsService.Changed -= OnSettingsChanged;
        _isSettingsSubscribed = false;
    }

    private void OnSettingsChanged(object? sender, SettingsChangedEvent e)
    {
        if (e.Scope != SettingsScope.App)
        {
            return;
        }

        if (e.ChangedKeys.Count > 0 &&
            !e.ChangedKeys.Contains(nameof(AppSettingsSnapshot.WeatherIconPackId)))
        {
            return;
        }

        CurrentVisualStyleId = WeatherVisualStyleCatalog.Normalize(_settingsFacade.Weather.Get().IconPackId);
        RenderWeather();
    }

    private (bool Enabled, int IntervalMinutes) ResolveRefreshSettings()
    {
        if (string.IsNullOrWhiteSpace(_componentId))
        {
            return (true, 12);
        }

        var snapshot = _settingsService.LoadSnapshot<ComponentSettingsSnapshot>(
            SettingsScope.ComponentInstance,
            _componentId,
            _placementId);

        return (
            snapshot.WeatherAutoRefreshEnabled,
            Math.Clamp(snapshot.WeatherAutoRefreshIntervalMinutes, 5, 360));
    }

    private WeatherConfig ResolveWeatherConfig()
    {
        var app = _settingsFacade.Weather.Get();
        var locationKey = app.LocationKey?.Trim() ?? string.Empty;
        var locationName = app.LocationName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(locationKey) &&
            string.Equals(app.LocationMode, "Coordinates", StringComparison.OrdinalIgnoreCase))
        {
            locationKey = FormattableString.Invariant($"coord:{app.Latitude:F4},{app.Longitude:F4}");
        }

        if (string.IsNullOrWhiteSpace(locationName))
        {
            locationName = string.IsNullOrWhiteSpace(locationKey) ? "Weather" : locationKey;
        }

        return new WeatherConfig(locationKey, locationName, app.Latitude, app.Longitude);
    }

    private static string ResolveLocationName(WeatherSnapshot snapshot, string fallback)
    {
        return string.IsNullOrWhiteSpace(snapshot.LocationName)
            ? fallback
            : snapshot.LocationName!;
    }

    private void UpdateAnimationState()
    {
        ApplyCurrentScene();
    }

    private static string NormalizeWeatherLocale(string? languageCode)
    {
        return string.Equals(languageCode, "en-US", StringComparison.OrdinalIgnoreCase)
            ? "en_us"
            : "zh_cn";
    }

    private readonly record struct WeatherConfig(string LocationKey, string LocationName, double Latitude, double Longitude);
}
