using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Avalonia.Controls;
using LanMountainDesktop.Appearance;
using LanMountainDesktop.ComponentSystem;
using LanMountainDesktop.Host.Abstractions;
using LanMountainDesktop.PluginSdk;
using LanMountainDesktop.Services;
using LanMountainDesktop.Services.Settings;
using LanMountainDesktop.Settings.Core;

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
        : this(
            componentId,
            displayNameLocalizationKey,
            _ => controlFactory(),
            cornerRadiusResolver is null
                ? null
                : chromeContext => cornerRadiusResolver(chromeContext.CellSize))
    {
    }

    public DesktopComponentRuntimeRegistration(
        string componentId,
        string? displayNameLocalizationKey,
        Func<DesktopComponentControlFactoryContext, Control> controlFactory,
        Func<double, double>? cornerRadiusResolver = null)
        : this(
            componentId,
            displayNameLocalizationKey,
            controlFactory,
            cornerRadiusResolver is null
                ? null
                : chromeContext => cornerRadiusResolver(chromeContext.CellSize))
    {
    }

    public DesktopComponentRuntimeRegistration(
        string componentId,
        string? displayNameLocalizationKey,
        Func<DesktopComponentControlFactoryContext, Control> controlFactory,
        Func<ComponentChromeContext, double>? cornerRadiusResolver = null)
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

    public Func<ComponentChromeContext, double>? CornerRadiusResolver { get; }
}

public sealed class DesktopComponentRuntimeDescriptor
{
    private static readonly Func<ComponentChromeContext, double> DefaultCornerRadiusResolver =
        chromeContext => ComponentChromeCornerRadiusHelper.ResolveMainRectangleRadiusValue(chromeContext);

    private readonly Func<DesktopComponentControlFactoryContext, Control> _controlFactory;
    private readonly Func<ComponentChromeContext, double> _cornerRadiusResolver;

    internal DesktopComponentRuntimeDescriptor(
        DesktopComponentDefinition definition,
        string? displayNameLocalizationKey,
        Func<DesktopComponentControlFactoryContext, Control> controlFactory,
        Func<ComponentChromeContext, double>? cornerRadiusResolver)
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
        var appearanceSnapshot = appearanceTheme.GetCurrent();
        var componentAccessor = settingsService.GetComponentAccessor(Definition.Id, placementId);
        var componentSettingsStore = new ComponentSettingsService(settingsService);
        componentSettingsStore.SetScopedComponentContext(Definition.Id, placementId);
        var chromeContext = new ComponentChromeContext(
            Definition.Id,
            placementId,
            cellSize,
            appearanceSnapshot.CornerRadiusTokens);
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
            chromeContext,
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

        if (control is IComponentChromeContextAware chromeContextAwareComponent)
        {
            chromeContextAwareComponent.SetComponentChromeContext(chromeContext);
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

    public double ResolveCornerRadius(ComponentChromeContext chromeContext)
    {
        ArgumentNullException.ThrowIfNull(chromeContext);

        var resolved = _cornerRadiusResolver(chromeContext with { CellSize = Math.Max(1, chromeContext.CellSize) });
        return double.IsFinite(resolved) ? Math.Max(0d, resolved) : DefaultCornerRadiusResolver(chromeContext);
    }

    public double ResolveCornerRadius(double cellSize)
    {
        return ResolveCornerRadius(new ComponentChromeContext(
            Definition.Id,
            null,
            Math.Max(1, cellSize),
            AppearanceCornerRadiusTokenFactory.Create(GlobalAppearanceSettings.DefaultCornerRadiusStyle)));
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
                    () => new DateWidget()),
                new DesktopComponentRuntimeRegistration(
                    BuiltInComponentIds.MonthCalendar,
                    "component.month_calendar",
                    () => new MonthCalendarWidget()),
                new DesktopComponentRuntimeRegistration(
                    BuiltInComponentIds.LunarCalendar,
                    "component.lunar_calendar",
                    () => new LunarCalendarWidget()),
                new DesktopComponentRuntimeRegistration(
                    BuiltInComponentIds.DesktopClock,
                    "component.desktop_clock",
                    () => new AnalogClockWidget()),
                new DesktopComponentRuntimeRegistration(
                    BuiltInComponentIds.DesktopWeatherClock,
                    "component.weather_clock",
                    () => new WeatherClockWidget()),
                new DesktopComponentRuntimeRegistration(
                    BuiltInComponentIds.DesktopWorldClock,
                    "component.world_clock",
                    () => new WorldClockWidget()),
                new DesktopComponentRuntimeRegistration(
                    BuiltInComponentIds.DesktopTimer,
                    "component.desktop_timer",
                    () => new TimerWidget()),
                new DesktopComponentRuntimeRegistration(
                    BuiltInComponentIds.DesktopWeather,
                    "component.desktop_weather",
                    () => new WeatherWidget()),
                new DesktopComponentRuntimeRegistration(
                    BuiltInComponentIds.DesktopHourlyWeather,
                    "component.hourly_weather",
                    () => new HourlyWeatherWidget()),
                new DesktopComponentRuntimeRegistration(
                    BuiltInComponentIds.DesktopMultiDayWeather,
                    "component.multiday_weather",
                    () => new MultiDayWeatherWidget()),
                new DesktopComponentRuntimeRegistration(
                    BuiltInComponentIds.DesktopExtendedWeather,
                    "component.extended_weather",
                    () => new ExtendedWeatherWidget()),
                new DesktopComponentRuntimeRegistration(
                    BuiltInComponentIds.DesktopClassSchedule,
                    "component.class_schedule",
                    () => new ClassScheduleWidget()),
                new DesktopComponentRuntimeRegistration(
                    BuiltInComponentIds.DesktopMusicControl,
                    "component.music_control",
                    () => new MusicControlWidget()),
                new DesktopComponentRuntimeRegistration(
                    BuiltInComponentIds.DesktopAudioRecorder,
                    "component.audio_recorder",
                    () => new RecordingWidget()),
                new DesktopComponentRuntimeRegistration(
                    BuiltInComponentIds.DesktopStudyEnvironment,
                    "component.study_environment",
                    () => new StudyEnvironmentWidget()),
                new DesktopComponentRuntimeRegistration(
                    BuiltInComponentIds.DesktopStudySessionControl,
                    "component.study_session_control",
                    () => new StudySessionControlWidget()),
                new DesktopComponentRuntimeRegistration(
                    BuiltInComponentIds.DesktopStudySessionHistory,
                    "component.study_session_history",
                    () => new StudySessionHistoryWidget()),
                new DesktopComponentRuntimeRegistration(
                    BuiltInComponentIds.DesktopStudyNoiseCurve,
                    "component.study_noise_curve",
                    () => new StudyNoiseCurveWidget()),
                new DesktopComponentRuntimeRegistration(
                    BuiltInComponentIds.DesktopStudyNoiseDistribution,
                    "component.study_noise_distribution",
                    () => new StudyNoiseDistributionWidget()),
                new DesktopComponentRuntimeRegistration(
                    BuiltInComponentIds.DesktopStudyScoreOverview,
                    "component.study_score_overview",
                    () => new StudyScoreOverviewWidget()),
                new DesktopComponentRuntimeRegistration(
                    BuiltInComponentIds.DesktopStudyDeductionReasons,
                    "component.study_deduction_reasons",
                    () => new StudyDeductionReasonsWidget()),
                new DesktopComponentRuntimeRegistration(
                    BuiltInComponentIds.DesktopStudyInterruptDensity,
                    "component.study_interrupt_density",
                    () => new StudyInterruptDensityWidget()),
                new DesktopComponentRuntimeRegistration(
                    BuiltInComponentIds.DesktopDailyPoetry,
                    "component.daily_poetry",
                    () => new DailyPoetryWidget()),
                new DesktopComponentRuntimeRegistration(
                    BuiltInComponentIds.DesktopDailyArtwork,
                    "component.daily_artwork",
                    () => new DailyArtworkWidget()),
                new DesktopComponentRuntimeRegistration(
                    BuiltInComponentIds.DesktopDailyWord,
                    "component.daily_word",
                    () => new DailyWordWidget()),
                new DesktopComponentRuntimeRegistration(
                    BuiltInComponentIds.DesktopDailyWord2x2,
                    "component.daily_word_2x2",
                    () => new DailyWord2x2Widget()),
                new DesktopComponentRuntimeRegistration(
                    BuiltInComponentIds.DesktopCnrDailyNews,
                    "component.cnr_daily_news",
                    () => new CnrDailyNewsWidget()),
                new DesktopComponentRuntimeRegistration(
                    BuiltInComponentIds.DesktopIfengNews,
                    "component.ifeng_news",
                    () => new IfengNewsWidget()),
                new DesktopComponentRuntimeRegistration(
                    BuiltInComponentIds.DesktopJuyaNews,
                    "component.juya_news",
                    () => new JuyaNewsWidget()),
                new DesktopComponentRuntimeRegistration(
                    BuiltInComponentIds.DesktopBilibiliHotSearch,
                    "component.bilibili_hot_search",
                    () => new BilibiliHotSearchWidget()),
                new DesktopComponentRuntimeRegistration(
                    BuiltInComponentIds.DesktopBaiduHotSearch,
                    "component.baidu_hot_search",
                    () => new BaiduHotSearchWidget()),
                new DesktopComponentRuntimeRegistration(
                    BuiltInComponentIds.DesktopStcn24Forum,
                    "component.stcn24_forum",
                    () => new Stcn24ForumWidget()),
                new DesktopComponentRuntimeRegistration(
                    BuiltInComponentIds.DesktopExchangeRateCalculator,
                    "component.exchange_rate_converter",
                    () => new ExchangeRateCalculatorWidget()),
                new DesktopComponentRuntimeRegistration(
                    BuiltInComponentIds.DesktopWhiteboard,
                    "component.whiteboard",
                    () => new WhiteboardWidget()),
                new DesktopComponentRuntimeRegistration(
                    BuiltInComponentIds.DesktopBlackboardLandscape,
                    "component.blackboard_landscape",
                    () => new WhiteboardWidget(baseWidthCells: 4)),
                new DesktopComponentRuntimeRegistration(
                    BuiltInComponentIds.DesktopStickyNote,
                    "component.sticky_note",
                    () => new StickyNoteWidget()),
                new DesktopComponentRuntimeRegistration(
                    BuiltInComponentIds.DesktopBrowser,
                    "component.browser",
                    () => new BrowserWidget()),
                new DesktopComponentRuntimeRegistration(
                    BuiltInComponentIds.DesktopOfficeRecentDocuments,
                    "component.office_recent_documents",
                    () => new OfficeRecentDocumentsWidget()),
                new DesktopComponentRuntimeRegistration(
                    BuiltInComponentIds.DesktopRemovableStorage,
                    "component.removable_storage",
                    () => new RemovableStorageWidget()),
                new DesktopComponentRuntimeRegistration(
                    BuiltInComponentIds.HolidayCalendar,
                    "component.holiday_calendar",
                    () => new HolidayCalendarWidget()),
                new DesktopComponentRuntimeRegistration(
                    BuiltInComponentIds.DesktopZhiJiaoHub,
                    "component.zhijiao_hub",
                    () => new ZhiJiaoHubWidget()),
                new DesktopComponentRuntimeRegistration(
                    BuiltInComponentIds.DesktopFileManager,
                    "component.file_manager",
                    () => new FileManagerWidget()),
                new DesktopComponentRuntimeRegistration(
                    BuiltInComponentIds.DesktopNotificationBox,
                    "component.notification_box",
                    () => new NotificationBoxWidget()),
                new DesktopComponentRuntimeRegistration(
                    BuiltInComponentIds.DesktopShortcut,
                    "component.shortcut",
                    () => new ShortcutWidget())
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
