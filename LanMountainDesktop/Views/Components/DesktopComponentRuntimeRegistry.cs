using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using LanMountainDesktop.ComponentSystem;
using LanMountainDesktop.Services;

namespace LanMountainDesktop.Views.Components;

public sealed record DesktopComponentRuntimeRegistration(
    string ComponentId,
    string DisplayNameLocalizationKey,
    Func<Control> ControlFactory,
    Func<double, double>? CornerRadiusResolver = null);

public sealed class DesktopComponentRuntimeDescriptor
{
    private static readonly Func<double, double> DefaultCornerRadiusResolver =
        cellSize => Math.Clamp(cellSize * 0.22, 8, 18);

    private readonly Func<Control> _controlFactory;
    private readonly Func<double, double> _cornerRadiusResolver;

    internal DesktopComponentRuntimeDescriptor(
        DesktopComponentDefinition definition,
        string displayNameLocalizationKey,
        Func<Control> controlFactory,
        Func<double, double>? cornerRadiusResolver)
    {
        Definition = definition;
        DisplayNameLocalizationKey = displayNameLocalizationKey;
        _controlFactory = controlFactory;
        _cornerRadiusResolver = cornerRadiusResolver ?? DefaultCornerRadiusResolver;
    }

    public DesktopComponentDefinition Definition { get; }

    public string DisplayNameLocalizationKey { get; }

    public Control CreateControl(double cellSize, TimeZoneService timeZoneService, IWeatherInfoService weatherInfoService)
    {
        var control = _controlFactory();
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

        return control;
    }

    public double ResolveCornerRadius(double cellSize)
    {
        return _cornerRadiusResolver(Math.Max(1, cellSize));
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

    public static DesktopComponentRuntimeRegistry CreateDefault(ComponentRegistry componentRegistry)
    {
        return new DesktopComponentRuntimeRegistry(
            componentRegistry,
            new[]
            {
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
                    BuiltInComponentIds.DesktopStudyNoiseCurve,
                    "component.study_noise_curve",
                    () => new StudyNoiseCurveWidget(),
                    cellSize => Math.Clamp(cellSize * 0.34, 12, 26)),
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
                    BuiltInComponentIds.HolidayCalendar,
                    "component.holiday_calendar",
                    () => new HolidayCalendarWidget(),
                    cellSize => Math.Clamp(cellSize * 0.32, 12, 28))
            });
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
