using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Avalonia.Controls;
using LanMountainDesktop.ComponentSystem;
using LanMountainDesktop.PluginSdk;
using LanMountainDesktop.Services;
using LanMountainDesktop.Services.Settings;

namespace LanMountainDesktop.Views.Components;

public sealed record DesktopComponentControlFactoryContext(
    DesktopComponentDefinition Definition,
    double CellSize,
    TimeZoneService TimeZoneService,
    IWeatherInfoService WeatherInfoService,
    IRecommendationInfoService RecommendationInfoService,
    ICalculatorDataService CalculatorDataService,
    ISettingsFacadeService SettingsFacade,
    ISettingsService SettingsService,
    IComponentInstanceSettingsStore ComponentSettingsStore,
    IComponentSettingsAccessor ComponentSettingsAccessor,
    string? PlacementId = null);

public sealed class DesktopComponentRuntimeRegistration
{
    public DesktopComponentRuntimeRegistration(
        string componentId,
        string? displayNameLocalizationKey,
        Func<Control> controlFactory,
        Func<double, double>? cornerRadiusResolver = null)
        : this(componentId, displayNameLocalizationKey, _ => controlFactory(), cornerRadiusResolver)
    {
    }

    public DesktopComponentRuntimeRegistration(
        string componentId,
        string? displayNameLocalizationKey,
        Func<DesktopComponentControlFactoryContext, Control> controlFactory,
        Func<double, double>? cornerRadiusResolver = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(componentId);
        ArgumentNullException.ThrowIfNull(controlFactory);

        ComponentId = componentId.Trim();
        DisplayNameLocalizationKey = string.IsNullOrWhiteSpace(displayNameLocalizationKey)
            ? null
            : displayNameLocalizationKey.Trim();
        ControlFactory = controlFactory;
        CornerRadiusResolver = cornerRadiusResolver;
    }

    public string ComponentId { get; }

    public string? DisplayNameLocalizationKey { get; }

    public Func<DesktopComponentControlFactoryContext, Control> ControlFactory { get; }

    public Func<double, double>? CornerRadiusResolver { get; }
}

public sealed class DesktopComponentRuntimeDescriptor
{
    private static readonly Func<double, double> DefaultCornerRadiusResolver =
        cellSize => Math.Clamp(cellSize * 0.22, 8, 18);

    private readonly Func<DesktopComponentControlFactoryContext, Control> _controlFactory;
    private readonly Func<double, double> _cornerRadiusResolver;

    internal DesktopComponentRuntimeDescriptor(
        DesktopComponentDefinition definition,
        string? displayNameLocalizationKey,
        Func<DesktopComponentControlFactoryContext, Control> controlFactory,
        Func<double, double>? cornerRadiusResolver)
    {
        Definition = definition;
        DisplayNameLocalizationKey = displayNameLocalizationKey;
        _controlFactory = controlFactory;
        _cornerRadiusResolver = cornerRadiusResolver ?? DefaultCornerRadiusResolver;
    }

    public DesktopComponentDefinition Definition { get; }

    public string? DisplayNameLocalizationKey { get; }

    public Control CreateControl(
        double cellSize,
        TimeZoneService timeZoneService,
        IWeatherInfoService weatherInfoService,
        IRecommendationInfoService recommendationInfoService,
        ICalculatorDataService calculatorDataService,
        ISettingsFacadeService settingsFacade,
        string? placementId = null)
    {
        ArgumentNullException.ThrowIfNull(settingsFacade);

        var settingsService = settingsFacade.Settings;
        var appearanceTheme = HostAppearanceThemeProvider.GetOrCreate();
        var componentAccessor = settingsService.GetComponentAccessor(Definition.Id, placementId);
        var componentSettingsStore = new ComponentSettingsService(settingsService);
        componentSettingsStore.SetScopedComponentContext(Definition.Id, placementId);
        var control = _controlFactory(new DesktopComponentControlFactoryContext(
            Definition,
            cellSize,
            timeZoneService,
            weatherInfoService,
            recommendationInfoService,
            calculatorDataService,
            settingsFacade,
            settingsService,
            componentSettingsStore,
            componentAccessor,
            placementId));
        var runtimeContext = new DesktopComponentRuntimeContext(
            Definition.Id,
            placementId,
            settingsFacade,
            settingsService,
            appearanceTheme,
            componentAccessor,
            componentSettingsStore);

        ApplySettingsDependencies(control, settingsService, componentSettingsStore);

        if (control is IComponentRuntimeContextAware runtimeContextAwareComponent)
        {
            runtimeContextAwareComponent.SetComponentRuntimeContext(runtimeContext);
        }

        if (control is IComponentSettingsContextAware settingsContextAwareComponent)
        {
            settingsContextAwareComponent.SetComponentSettingsContext(new DesktopComponentSettingsContext(
                Definition.Id,
                placementId,
                settingsFacade,
                settingsService,
                appearanceTheme,
                componentAccessor,
                componentSettingsStore));
        }

        if (control is IComponentPlacementContextAware placementAwareComponent)
        {
            placementAwareComponent.SetComponentPlacementContext(Definition.Id, placementId);
        }

        if (control is IDesktopComponentWidget sizedComponent)
        {
            sizedComponent.ApplyCellSize(cellSize);
        }

        if (control is ITimeZoneAwareComponentWidget timeZoneAwareComponent)
        {
            timeZoneAwareComponent.SetTimeZoneService(timeZoneService);
        }

        if (control is IWeatherInfoAwareComponentWidget weatherInfoAwareComponent)
        {
            weatherInfoAwareComponent.SetWeatherInfoService(weatherInfoService);
        }

        if (control is IRecommendationInfoAwareComponentWidget recommendationInfoAwareComponent)
        {
            recommendationInfoAwareComponent.SetRecommendationInfoService(recommendationInfoService);
        }

        if (control is ICalculatorInfoAwareComponentWidget calculatorInfoAwareComponent)
        {
            calculatorInfoAwareComponent.SetCalculatorDataService(calculatorDataService);
        }

        return control;
    }

    public double ResolveCornerRadius(double cellSize)
    {
        return _cornerRadiusResolver(Math.Max(1, cellSize));
    }

    private static void ApplySettingsDependencies(
        object? target,
        ISettingsService settingsService,
        IComponentInstanceSettingsStore componentSettingsStore)
    {
        if (target is null)
        {
            return;
        }

        var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        foreach (var field in target.GetType().GetFields(flags))
        {
            if (field.IsInitOnly)
            {
                continue;
            }

            if (typeof(ISettingsService).IsAssignableFrom(field.FieldType))
            {
                field.SetValue(target, settingsService);
                continue;
            }

            if (typeof(IComponentInstanceSettingsStore).IsAssignableFrom(field.FieldType))
            {
                field.SetValue(target, componentSettingsStore);
            }
        }

        foreach (var property in target.GetType().GetProperties(flags))
        {
            if (!property.CanWrite)
            {
                continue;
            }

            if (typeof(ISettingsService).IsAssignableFrom(property.PropertyType))
            {
                property.SetValue(target, settingsService);
                continue;
            }

            if (typeof(IComponentInstanceSettingsStore).IsAssignableFrom(property.PropertyType))
            {
                property.SetValue(target, componentSettingsStore);
            }
        }
    }
}

public sealed class DesktopComponentRuntimeRegistry
{
    private readonly Dictionary<string, DesktopComponentRuntimeDescriptor> _descriptors;

    public DesktopComponentRuntimeRegistry(
        ComponentRegistry componentRegistry,
        IEnumerable<DesktopComponentRuntimeRegistration> registrations)
    {
        var registrationMap = registrations
            .Where(r => !string.IsNullOrWhiteSpace(r.ComponentId) && r.ControlFactory is not null)
            .GroupBy(r => r.ComponentId.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);

        _descriptors = componentRegistry
            .GetAll()
            .Where(definition => registrationMap.ContainsKey(definition.Id))
            .ToDictionary(
                definition => definition.Id,
                definition =>
                {
                    var registration = registrationMap[definition.Id];
                    return new DesktopComponentRuntimeDescriptor(
                        definition,
                        registration.DisplayNameLocalizationKey,
                        registration.ControlFactory,
                        registration.CornerRadiusResolver);
                },
                StringComparer.OrdinalIgnoreCase);
    }

    public static IReadOnlyList<DesktopComponentRuntimeRegistration> GetDefaultRegistrations()
    {
        return
        [
                new DesktopComponentRuntimeRegistration(
                    BuiltInComponentIds.Date,
                    "component.date",
                    () => new DateWidget(),
                    _ => 16),
                new DesktopComponentRuntimeRegistration(
                    BuiltInComponentIds.MonthCalendar,
                    "component.month_calendar",
                    () => new MonthCalendarWidget(),
                    cellSize => Math.Clamp(cellSize * 0.26, 10, 22)),
                new DesktopComponentRuntimeRegistration(
                    BuiltInComponentIds.LunarCalendar,
                    "component.lunar_calendar",
                    () => new LunarCalendarWidget(),
                    cellSize => Math.Clamp(cellSize * 0.30, 12, 26)),
                new DesktopComponentRuntimeRegistration(
                    BuiltInComponentIds.DesktopClock,
                    "component.desktop_clock",
                    () => new AnalogClockWidget(),
                    cellSize => Math.Clamp(cellSize * 0.30, 12, 28)),
                new DesktopComponentRuntimeRegistration(
                    BuiltInComponentIds.DesktopWeatherClock,
                    "component.weather_clock",
                    () => new WeatherClockWidget(),
                    cellSize => Math.Clamp(cellSize * 0.34, 14, 30)),
                new DesktopComponentRuntimeRegistration(
                    BuiltInComponentIds.DesktopWorldClock,
                    "component.world_clock",
                    () => new WorldClockWidget(),
                    cellSize => Math.Clamp(cellSize * 0.30, 10, 24)),
                new DesktopComponentRuntimeRegistration(
                    BuiltInComponentIds.DesktopTimer,
                    "component.desktop_timer",
                    () => new TimerWidget(),
                    cellSize => Math.Clamp(cellSize * 0.30, 12, 28)),
                new DesktopComponentRuntimeRegistration(
                    BuiltInComponentIds.DesktopWeather,
                    "component.desktop_weather",
                    () => new WeatherWidget(),
                    cellSize => Math.Clamp(cellSize * 0.45, 24, 44)),
                new DesktopComponentRuntimeRegistration(
                    BuiltInComponentIds.DesktopHourlyWeather,
                    "component.hourly_weather",
                    () => new HourlyWeatherWidget(),
                    cellSize => Math.Clamp(cellSize * 0.45, 24, 44)),
                new DesktopComponentRuntimeRegistration(
                    BuiltInComponentIds.DesktopMultiDayWeather,
                    "component.multiday_weather",
                    () => new MultiDayWeatherWidget(),
                    cellSize => Math.Clamp(cellSize * 0.45, 24, 44)),
                new DesktopComponentRuntimeRegistration(
                    BuiltInComponentIds.DesktopExtendedWeather,
                    "component.extended_weather",
                    () => new ExtendedWeatherWidget(),
                    cellSize => Math.Clamp(cellSize * 0.45, 24, 44)),
                new DesktopComponentRuntimeRegistration(
                    BuiltInComponentIds.DesktopClassSchedule,
                    "component.class_schedule",
                    () => new ClassScheduleWidget(),
                    cellSize => Math.Clamp(cellSize * 0.45, 24, 44)),
                new DesktopComponentRuntimeRegistration(
                    BuiltInComponentIds.DesktopMusicControl,
                    "component.music_control",
                    () => new MusicControlWidget(),
                    cellSize => Math.Clamp(cellSize * 0.34, 14, 30)),
                new DesktopComponentRuntimeRegistration(
                    BuiltInComponentIds.DesktopAudioRecorder,
                    "component.audio_recorder",
                    () => new RecordingWidget(),
                    cellSize => Math.Clamp(cellSize * 0.36, 16, 34)),
                new DesktopComponentRuntimeRegistration(
                    BuiltInComponentIds.DesktopStudyEnvironment,
                    "component.study_environment",
                    () => new StudyEnvironmentWidget(),
                    cellSize => Math.Clamp(cellSize * 0.36, 12, 26)),
                new DesktopComponentRuntimeRegistration(
                    BuiltInComponentIds.DesktopStudySessionControl,
                    "component.study_session_control",
                    () => new StudySessionControlWidget(),
                    cellSize => Math.Clamp(cellSize * 0.36, 10, 24)),
                new DesktopComponentRuntimeRegistration(
                    BuiltInComponentIds.DesktopStudySessionHistory,
                    "component.study_session_history",
                    () => new StudySessionHistoryWidget(),
                    cellSize => Math.Clamp(cellSize * 0.34, 10, 24)),
                new DesktopComponentRuntimeRegistration(
                    BuiltInComponentIds.DesktopStudyNoiseCurve,
                    "component.study_noise_curve",
                    () => new StudyNoiseCurveWidget(),
                    cellSize => Math.Clamp(cellSize * 0.34, 12, 26)),
                new DesktopComponentRuntimeRegistration(
                    BuiltInComponentIds.DesktopStudyNoiseDistribution,
                    "component.study_noise_distribution",
                    () => new StudyNoiseDistributionWidget(),
                    cellSize => Math.Clamp(cellSize * 0.34, 12, 26)),
                new DesktopComponentRuntimeRegistration(
                    BuiltInComponentIds.DesktopStudyScoreOverview,
                    "component.study_score_overview",
                    () => new StudyScoreOverviewWidget(),
                    cellSize => Math.Clamp(cellSize * 0.34, 12, 28)),
                new DesktopComponentRuntimeRegistration(
                    BuiltInComponentIds.DesktopStudyDeductionReasons,
                    "component.study_deduction_reasons",
                    () => new StudyDeductionReasonsWidget(),
                    cellSize => Math.Clamp(cellSize * 0.34, 10, 24)),
                new DesktopComponentRuntimeRegistration(
                    BuiltInComponentIds.DesktopStudyInterruptDensity,
                    "component.study_interrupt_density",
                    () => new StudyInterruptDensityWidget(),
                    cellSize => Math.Clamp(cellSize * 0.34, 10, 24)),
                new DesktopComponentRuntimeRegistration(
                    BuiltInComponentIds.DesktopDailyPoetry,
                    "component.daily_poetry",
                    () => new DailyPoetryWidget(),
                    cellSize => Math.Clamp(cellSize * 0.34, 14, 30)),
                new DesktopComponentRuntimeRegistration(
                    BuiltInComponentIds.DesktopDailyArtwork,
                    "component.daily_artwork",
                    () => new DailyArtworkWidget(),
                    cellSize => Math.Clamp(cellSize * 0.34, 14, 30)),
                new DesktopComponentRuntimeRegistration(
                    BuiltInComponentIds.DesktopDailyWord,
                    "component.daily_word",
                    () => new DailyWordWidget(),
                    cellSize => Math.Clamp(cellSize * 0.34, 14, 30)),
                new DesktopComponentRuntimeRegistration(
                    BuiltInComponentIds.DesktopDailyWord2x2,
                    "component.daily_word_2x2",
                    () => new DailyWord2x2Widget(),
                    cellSize => Math.Clamp(cellSize * 0.34, 12, 26)),
                new DesktopComponentRuntimeRegistration(
                    BuiltInComponentIds.DesktopCnrDailyNews,
                    "component.cnr_daily_news",
                    () => new CnrDailyNewsWidget(),
                    cellSize => Math.Clamp(cellSize * 0.34, 14, 30)),
                new DesktopComponentRuntimeRegistration(
                    BuiltInComponentIds.DesktopIfengNews,
                    "component.ifeng_news",
                    () => new IfengNewsWidget(),
                    cellSize => Math.Clamp(cellSize * 0.30, 12, 24)),
                new DesktopComponentRuntimeRegistration(
                    BuiltInComponentIds.DesktopBilibiliHotSearch,
                    "component.bilibili_hot_search",
                    () => new BilibiliHotSearchWidget(),
                    cellSize => Math.Clamp(cellSize * 0.34, 14, 30)),
                new DesktopComponentRuntimeRegistration(
                    BuiltInComponentIds.DesktopBaiduHotSearch,
                    "component.baidu_hot_search",
                    () => new BaiduHotSearchWidget(),
                    cellSize => Math.Clamp(cellSize * 0.34, 14, 30)),
                new DesktopComponentRuntimeRegistration(
                    BuiltInComponentIds.DesktopStcn24Forum,
                    "component.stcn24_forum",
                    () => new Stcn24ForumWidget(),
                    cellSize => Math.Clamp(cellSize * 0.28, 12, 24)),
                new DesktopComponentRuntimeRegistration(
                    BuiltInComponentIds.DesktopExchangeRateCalculator,
                    "component.exchange_rate_converter",
                    () => new ExchangeRateCalculatorWidget(),
                    cellSize => Math.Clamp(cellSize * 0.28, 12, 26)),
                new DesktopComponentRuntimeRegistration(
                    BuiltInComponentIds.DesktopWhiteboard,
                    "component.whiteboard",
                    () => new WhiteboardWidget(),
                    cellSize => Math.Clamp(cellSize * 0.24, 10, 24)),
                new DesktopComponentRuntimeRegistration(
                    BuiltInComponentIds.DesktopBlackboardLandscape,
                    "component.blackboard_landscape",
                    () => new WhiteboardWidget(baseWidthCells: 4),
                    cellSize => Math.Clamp(cellSize * 0.24, 10, 24)),
                new DesktopComponentRuntimeRegistration(
                    BuiltInComponentIds.DesktopBrowser,
                    "component.browser",
                    () => new BrowserWidget(),
                    cellSize => Math.Clamp(cellSize * 0.24, 10, 24)),
                new DesktopComponentRuntimeRegistration(
                    BuiltInComponentIds.DesktopOfficeRecentDocuments,
                    "component.office_recent_documents",
                    () => new OfficeRecentDocumentsWidget(),
                    cellSize => Math.Clamp(cellSize * 0.50, 10, 24)),
                new DesktopComponentRuntimeRegistration(
                    BuiltInComponentIds.HolidayCalendar,
                    "component.holiday_calendar",
                    () => new HolidayCalendarWidget(),
                    cellSize => Math.Clamp(cellSize * 0.32, 12, 28))
        ];
    }

    public static DesktopComponentRuntimeRegistry CreateDefault(
        ComponentRegistry componentRegistry,
        ISettingsFacadeService settingsFacade)
    {
        _ = settingsFacade;
        return new DesktopComponentRuntimeRegistry(componentRegistry, GetDefaultRegistrations());
    }

    public bool TryGetDescriptor(string componentId, out DesktopComponentRuntimeDescriptor descriptor)
    {
        return _descriptors.TryGetValue(componentId, out descriptor!);
    }

    public IReadOnlyList<DesktopComponentRuntimeDescriptor> GetDesktopComponents()
    {
        return _descriptors.Values
            .Where(descriptor => descriptor.Definition.AllowDesktopPlacement)
            .OrderBy(descriptor => descriptor.Definition.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(descriptor => descriptor.Definition.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
