using System;
using System.Collections.Generic;
using System.Linq;
using LanMountainDesktop.ComponentSystem.Extensions;

namespace LanMountainDesktop.ComponentSystem;

public sealed class ComponentRegistry
{
    private readonly Dictionary<string, DesktopComponentDefinition> _definitions;

    public ComponentRegistry(IEnumerable<DesktopComponentDefinition> definitions)
    {
        _definitions = definitions
            .Where(d => !string.IsNullOrWhiteSpace(d.Id))
            .GroupBy(d => d.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);
    }

    public static ComponentRegistry CreateDefault()
    {
        var builtIn = new[]
        {
            new DesktopComponentDefinition(
                BuiltInComponentIds.Clock,
                "Clock",
                "Clock",
                "Status",
                MinWidthCells: 3,
                MinHeightCells: 1,
                AllowStatusBarPlacement: true,
                AllowDesktopPlacement: false),
            new DesktopComponentDefinition(
                BuiltInComponentIds.DesktopClock,
                "Clock",
                "Clock",
                "Clock",
                MinWidthCells: 2,
                MinHeightCells: 2,
                AllowStatusBarPlacement: false,
                AllowDesktopPlacement: true),
            new DesktopComponentDefinition(
                BuiltInComponentIds.DesktopWeatherClock,
                "Weather Clock",
                "Clock",
                "Clock",
                MinWidthCells: 2,
                MinHeightCells: 1,
                AllowStatusBarPlacement: false,
                AllowDesktopPlacement: true),
            new DesktopComponentDefinition(
                BuiltInComponentIds.DesktopWorldClock,
                "World Clock",
                "Clock",
                "Clock",
                MinWidthCells: 4,
                MinHeightCells: 2,
                AllowStatusBarPlacement: false,
                AllowDesktopPlacement: true),
            new DesktopComponentDefinition(
                BuiltInComponentIds.DesktopTimer,
                "Timer",
                "Timer",
                "Clock",
                MinWidthCells: 2,
                MinHeightCells: 2,
                AllowStatusBarPlacement: false,
                AllowDesktopPlacement: true),
            new DesktopComponentDefinition(
                BuiltInComponentIds.DesktopWeather,
                "Weather",
                "WeatherSunny",
                "Weather",
                MinWidthCells: 2,
                MinHeightCells: 2,
                AllowStatusBarPlacement: false,
                AllowDesktopPlacement: true),
            new DesktopComponentDefinition(
                BuiltInComponentIds.DesktopHourlyWeather,
                "Hourly Weather",
                "WeatherSunny",
                "Weather",
                MinWidthCells: 4,
                MinHeightCells: 2,
                AllowStatusBarPlacement: false,
                AllowDesktopPlacement: true),
            new DesktopComponentDefinition(
                BuiltInComponentIds.DesktopMultiDayWeather,
                "Multi-day Weather",
                "WeatherSunny",
                "Weather",
                MinWidthCells: 4,
                MinHeightCells: 2,
                AllowStatusBarPlacement: false,
                AllowDesktopPlacement: true),
            new DesktopComponentDefinition(
                BuiltInComponentIds.DesktopExtendedWeather,
                "Extended Weather",
                "WeatherSunny",
                "Weather",
                MinWidthCells: 4,
                MinHeightCells: 4,
                AllowStatusBarPlacement: false,
                AllowDesktopPlacement: true),
            new DesktopComponentDefinition(
                BuiltInComponentIds.DesktopClassSchedule,
                "Class Schedule",
                "CalendarDate",
                "Date",
                MinWidthCells: 2,
                MinHeightCells: 4,
                AllowStatusBarPlacement: false,
                AllowDesktopPlacement: true,
                ResizeMode: DesktopComponentResizeMode.Free),
            new DesktopComponentDefinition(
                BuiltInComponentIds.DesktopMusicControl,
                "Music Control",
                "Play",
                "Media",
                MinWidthCells: 4,
                MinHeightCells: 2,
                AllowStatusBarPlacement: false,
                AllowDesktopPlacement: true),
            new DesktopComponentDefinition(
                BuiltInComponentIds.DesktopAudioRecorder,
                "Recorder",
                "MicOn",
                "Media",
                MinWidthCells: 2,
                MinHeightCells: 2,
                AllowStatusBarPlacement: false,
                AllowDesktopPlacement: true),
            new DesktopComponentDefinition(
                BuiltInComponentIds.DesktopStudyEnvironment,
                "Study Environment",
                "MicOn",
                "Study",
                MinWidthCells: 2,
                MinHeightCells: 1,
                AllowStatusBarPlacement: false,
                AllowDesktopPlacement: true,
                ResizeMode: DesktopComponentResizeMode.Free),
            new DesktopComponentDefinition(
                BuiltInComponentIds.DesktopStudySessionControl,
                "Study Session",
                "Play",
                "Study",
                MinWidthCells: 2,
                MinHeightCells: 1,
                AllowStatusBarPlacement: false,
                AllowDesktopPlacement: true),
            new DesktopComponentDefinition(
                BuiltInComponentIds.DesktopStudySessionHistory,
                "Session History",
                "History",
                "Study",
                MinWidthCells: 4,
                MinHeightCells: 2,
                AllowStatusBarPlacement: false,
                AllowDesktopPlacement: true,
                ResizeMode: DesktopComponentResizeMode.Free),
            new DesktopComponentDefinition(
                BuiltInComponentIds.DesktopStudyNoiseCurve,
                "Noise Curve",
                "DataLine",
                "Study",
                MinWidthCells: 4,
                MinHeightCells: 2,
                AllowStatusBarPlacement: false,
                AllowDesktopPlacement: true),
            new DesktopComponentDefinition(
                BuiltInComponentIds.DesktopStudyNoiseDistribution,
                "Noise Distribution",
                "DataLine",
                "Study",
                MinWidthCells: 4,
                MinHeightCells: 2,
                AllowStatusBarPlacement: false,
                AllowDesktopPlacement: true,
                ResizeMode: DesktopComponentResizeMode.Free),
            new DesktopComponentDefinition(
                BuiltInComponentIds.DesktopStudyScoreOverview,
                "Study Score Overview",
                "DataLine",
                "Study",
                MinWidthCells: 4,
                MinHeightCells: 4,
                AllowStatusBarPlacement: false,
                AllowDesktopPlacement: true),
            new DesktopComponentDefinition(
                BuiltInComponentIds.DesktopStudyDeductionReasons,
                "Deduction Reasons",
                "DataLine",
                "Study",
                MinWidthCells: 4,
                MinHeightCells: 2,
                AllowStatusBarPlacement: false,
                AllowDesktopPlacement: true,
                ResizeMode: DesktopComponentResizeMode.Free),
            new DesktopComponentDefinition(
                BuiltInComponentIds.DesktopStudyInterruptDensity,
                "Interrupt Density",
                "DataLine",
                "Study",
                MinWidthCells: 4,
                MinHeightCells: 2,
                AllowStatusBarPlacement: false,
                AllowDesktopPlacement: true,
                ResizeMode: DesktopComponentResizeMode.Free),
            new DesktopComponentDefinition(
                BuiltInComponentIds.DesktopDailyPoetry,
                "Daily Poetry",
                "Book",
                "Info",
                MinWidthCells: 4,
                MinHeightCells: 2,
                AllowStatusBarPlacement: false,
                AllowDesktopPlacement: true),
            new DesktopComponentDefinition(
                BuiltInComponentIds.DesktopDailyArtwork,
                "Daily Artwork",
                "Image",
                "Info",
                MinWidthCells: 4,
                MinHeightCells: 2,
                AllowStatusBarPlacement: false,
                AllowDesktopPlacement: true),
            new DesktopComponentDefinition(
                BuiltInComponentIds.DesktopDailyWord,
                "Daily Word",
                "Book",
                "Info",
                MinWidthCells: 4,
                MinHeightCells: 2,
                AllowStatusBarPlacement: false,
                AllowDesktopPlacement: true),
            new DesktopComponentDefinition(
                BuiltInComponentIds.DesktopDailyWord2x2,
                "Daily Word 2x2",
                "Book",
                "Info",
                MinWidthCells: 2,
                MinHeightCells: 2,
                AllowStatusBarPlacement: false,
                AllowDesktopPlacement: true),
            new DesktopComponentDefinition(
                BuiltInComponentIds.DesktopCnrDailyNews,
                "CNR Daily News",
                "News",
                "Info",
                MinWidthCells: 4,
                MinHeightCells: 2,
                AllowStatusBarPlacement: false,
                AllowDesktopPlacement: true),
            new DesktopComponentDefinition(
                BuiltInComponentIds.DesktopIfengNews,
                "iFeng News",
                "News",
                "Info",
                MinWidthCells: 4,
                MinHeightCells: 4,
                AllowStatusBarPlacement: false,
                AllowDesktopPlacement: true),
            new DesktopComponentDefinition(
                BuiltInComponentIds.DesktopJuyaNews,
                "橘鸦早报",
                "News",
                "Info",
                MinWidthCells: 4,
                MinHeightCells: 4,
                AllowStatusBarPlacement: false,
                AllowDesktopPlacement: true,
                ResizeMode: DesktopComponentResizeMode.Free),
            new DesktopComponentDefinition(
                BuiltInComponentIds.DesktopBilibiliHotSearch,
                "Bilibili Hot Search",
                "News",
                "Info",
                MinWidthCells: 4,
                MinHeightCells: 2,
                AllowStatusBarPlacement: false,
                AllowDesktopPlacement: true),
            new DesktopComponentDefinition(
                BuiltInComponentIds.DesktopBaiduHotSearch,
                "Baidu Hot Search",
                "News",
                "Info",
                MinWidthCells: 4,
                MinHeightCells: 2,
                AllowStatusBarPlacement: false,
                AllowDesktopPlacement: true),
            new DesktopComponentDefinition(
                BuiltInComponentIds.DesktopStcn24Forum,
                "STCN 24",
                "News",
                "Info",
                MinWidthCells: 4,
                MinHeightCells: 4,
                AllowStatusBarPlacement: false,
                AllowDesktopPlacement: true),
            new DesktopComponentDefinition(
                BuiltInComponentIds.DesktopExchangeRateCalculator,
                "Exchange Rate Converter",
                "Calculator",
                "Calculator",
                MinWidthCells: 4,
                MinHeightCells: 4,
                AllowStatusBarPlacement: false,
                AllowDesktopPlacement: true),
            new DesktopComponentDefinition(
                BuiltInComponentIds.DesktopWhiteboard,
                "Blackboard Portrait",
                "Edit",
                "Board",
                MinWidthCells: 2,
                MinHeightCells: 4,
                AllowStatusBarPlacement: false,
                AllowDesktopPlacement: true,
                ResizeMode: DesktopComponentResizeMode.Free),
            new DesktopComponentDefinition(
                BuiltInComponentIds.DesktopBlackboardLandscape,
                "Blackboard Landscape",
                "Edit",
                "Board",
                MinWidthCells: 4,
                MinHeightCells: 2,
                AllowStatusBarPlacement: false,
                AllowDesktopPlacement: true,
                ResizeMode: DesktopComponentResizeMode.Free),
            new DesktopComponentDefinition(
                BuiltInComponentIds.DesktopBrowser,
                "Browser",
                "Globe",
                "Board",
                MinWidthCells: 4,
                MinHeightCells: 4,
                AllowStatusBarPlacement: false,
                AllowDesktopPlacement: true,
                ResizeMode: DesktopComponentResizeMode.Free),
            new DesktopComponentDefinition(
                BuiltInComponentIds.DesktopOfficeRecentDocuments,
                "Office Recent Documents",
                "Folder",
                "File",
                MinWidthCells: 4,
                MinHeightCells: 2,
                AllowStatusBarPlacement: false,
                AllowDesktopPlacement: true),
            new DesktopComponentDefinition(
                BuiltInComponentIds.DesktopRemovableStorage,
                "Removable Storage",
                "Storage",
                "File",
                MinWidthCells: 2,
                MinHeightCells: 2,
                AllowStatusBarPlacement: false,
                AllowDesktopPlacement: true),
            new DesktopComponentDefinition(
                BuiltInComponentIds.Date,
                "Calendar",
                "Calendar",
                "Date",
                MinWidthCells: 4,
                MinHeightCells: 2,
                AllowStatusBarPlacement: false,
                AllowDesktopPlacement: true),
            new DesktopComponentDefinition(
                BuiltInComponentIds.MonthCalendar,
                "Month Calendar",
                "CalendarMonth",
                "Date",
                MinWidthCells: 2,
                MinHeightCells: 2,
                AllowStatusBarPlacement: false,
                AllowDesktopPlacement: true),
            new DesktopComponentDefinition(
                BuiltInComponentIds.LunarCalendar,
                "Lunar Calendar",
                "Calendar",
                "Date",
                MinWidthCells: 2,
                MinHeightCells: 2,
                AllowStatusBarPlacement: false,
                AllowDesktopPlacement: true),
            new DesktopComponentDefinition(
                BuiltInComponentIds.HolidayCalendar,
                "Holiday Countdown",
                "Calendar",
                "Date",
                MinWidthCells: 2,
                MinHeightCells: 2,
                AllowStatusBarPlacement: false,
                AllowDesktopPlacement: true),
            new DesktopComponentDefinition(
                BuiltInComponentIds.DesktopZhiJiaoHub,
                "智教Hub",
                "Image",
                "Info",
                MinWidthCells: 2,
                MinHeightCells: 2,
                AllowStatusBarPlacement: false,
                AllowDesktopPlacement: true,
                ResizeMode: DesktopComponentResizeMode.Free)
        };

        return new ComponentRegistry(builtIn);
    }

    public ComponentRegistry RegisterExtensions(IEnumerable<IComponentExtensionProvider> providers)
    {
        var merged = _definitions.Values.ToList();
        foreach (var provider in providers)
        {
            var externalDefinitions = provider.GetComponents();
            if (externalDefinitions is null)
            {
                continue;
            }

            merged.AddRange(externalDefinitions);
        }

        return new ComponentRegistry(merged);
    }

    public ComponentRegistry RegisterComponents(IEnumerable<DesktopComponentDefinition> definitions)
    {
        var merged = _definitions.Values.ToList();
        merged.AddRange(definitions);
        return new ComponentRegistry(merged);
    }

    public bool TryGetDefinition(string componentId, out DesktopComponentDefinition definition)
    {
        return _definitions.TryGetValue(componentId, out definition!);
    }

    public bool IsKnownComponent(string componentId)
    {
        return _definitions.ContainsKey(componentId);
    }

    public bool AllowsStatusBarPlacement(string componentId)
    {
        return _definitions.TryGetValue(componentId, out var definition) && definition.AllowStatusBarPlacement;
    }

    public IReadOnlyList<DesktopComponentDefinition> GetAll()
    {
        return _definitions.Values.OrderBy(d => d.Category).ThenBy(d => d.DisplayName).ToList();
    }
}
