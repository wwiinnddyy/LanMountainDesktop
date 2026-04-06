using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Avalonia.Controls;
using LanMountainDesktop.ComponentSystem;
using LanMountainDesktop.Models;
using LanMountainDesktop.Plugins;
using LanMountainDesktop.PluginSdk;
using LanMountainDesktop.Views.ComponentEditors;

namespace LanMountainDesktop.Services;

public static class DesktopComponentEditorRegistryFactory
{
    public static DesktopComponentEditorRegistry Create(
        ComponentRegistry componentRegistry,
        PluginRuntimeService? pluginRuntimeService)
    {
        ArgumentNullException.ThrowIfNull(componentRegistry);

        var registrations = GetBuiltInRegistrations(componentRegistry).ToList();
        var registeredIds = new HashSet<string>(
            registrations.Select(registration => registration.ComponentId),
            StringComparer.OrdinalIgnoreCase);

        if (pluginRuntimeService is not null)
        {
            foreach (var contribution in pluginRuntimeService.DesktopComponentEditors)
            {
                var registration = contribution.Registration;
                if (!componentRegistry.TryGetDefinition(registration.ComponentId, out var definition) ||
                    !definition.AllowDesktopPlacement ||
                    !registeredIds.Add(registration.ComponentId))
                {
                    continue;
                }

                registrations.Add(new DesktopComponentEditorRegistration(
                    registration.ComponentId,
                    context => CreatePluginEditor(contribution, context),
                    registration.PreferredWidth,
                    registration.PreferredHeight,
                    registration.MinScale,
                    registration.MaxScale));
            }
        }

        return new DesktopComponentEditorRegistry(componentRegistry, registrations);
    }

    private static IEnumerable<DesktopComponentEditorRegistration> GetBuiltInRegistrations(ComponentRegistry componentRegistry)
    {
        var registrations = new Dictionary<string, DesktopComponentEditorRegistration>(StringComparer.OrdinalIgnoreCase)
        {
            [BuiltInComponentIds.DesktopClock] = new(
                BuiltInComponentIds.DesktopClock,
                context => new ClockComponentEditor(context)),
            [BuiltInComponentIds.DesktopWorldClock] = new(
                BuiltInComponentIds.DesktopWorldClock,
                context => new WorldClockComponentEditor(context),
                preferredWidth: 820d,
                preferredHeight: 620d),
            [BuiltInComponentIds.DesktopClassSchedule] = new(
                BuiltInComponentIds.DesktopClassSchedule,
                context => new ClassScheduleComponentEditor(context),
                preferredWidth: 860d,
                preferredHeight: 640d),
            [BuiltInComponentIds.DesktopDailyArtwork] = new(
                BuiltInComponentIds.DesktopDailyArtwork,
                context => new DailyArtworkComponentEditor(context)),
            [BuiltInComponentIds.DesktopStudyEnvironment] = new(
                BuiltInComponentIds.DesktopStudyEnvironment,
                context => new StudyEnvironmentComponentEditor(context)),
            [BuiltInComponentIds.DesktopRemovableStorage] = new(
                BuiltInComponentIds.DesktopRemovableStorage,
                context => new RemovableStorageComponentEditor(context)),
            [BuiltInComponentIds.DesktopWhiteboard] = new(
                BuiltInComponentIds.DesktopWhiteboard,
                context => new WhiteboardComponentEditor(context)),
            [BuiltInComponentIds.DesktopBlackboardLandscape] = new(
                BuiltInComponentIds.DesktopBlackboardLandscape,
                context => new WhiteboardComponentEditor(context)),
            [BuiltInComponentIds.DesktopOfficeRecentDocuments] = new(
                BuiltInComponentIds.DesktopOfficeRecentDocuments,
                context => new OfficeRecentDocumentsComponentEditor(context)),
            [BuiltInComponentIds.DesktopWeather] = CreateWeatherRegistration(BuiltInComponentIds.DesktopWeather),
            [BuiltInComponentIds.DesktopWeatherClock] = CreateWeatherRegistration(BuiltInComponentIds.DesktopWeatherClock),
            [BuiltInComponentIds.DesktopHourlyWeather] = CreateWeatherRegistration(BuiltInComponentIds.DesktopHourlyWeather),
            [BuiltInComponentIds.DesktopMultiDayWeather] = CreateWeatherRegistration(BuiltInComponentIds.DesktopMultiDayWeather),
            [BuiltInComponentIds.DesktopExtendedWeather] = CreateWeatherRegistration(BuiltInComponentIds.DesktopExtendedWeather),
            [BuiltInComponentIds.DesktopCnrDailyNews] = new(
                BuiltInComponentIds.DesktopCnrDailyNews,
                context => new ToggleIntervalComponentEditor(
                    context,
                    new ToggleIntervalComponentEditorOptions
                    {
                        DescriptionKey = "cnr.settings.desc",
                        DescriptionFallback = "Configure auto rotation for this CNR news widget.",
                        ToggleLabelKey = "cnr.settings.auto_rotate",
                        ToggleLabelFallback = "Auto rotate",
                        ToggleDescriptionKey = "component.editor.instance_scope",
                        ToggleDescriptionFallback = "Changes are stored per component instance.",
                        IntervalLabelKey = "cnr.settings.rotate_interval",
                        IntervalLabelFallback = "Rotate interval",
                        DefaultInterval = 60,
                        GetEnabled = snapshot => snapshot.CnrDailyNewsAutoRotateEnabled,
                        SetEnabled = (snapshot, value) => snapshot.CnrDailyNewsAutoRotateEnabled = value,
                        GetInterval = snapshot => snapshot.CnrDailyNewsAutoRotateIntervalMinutes,
                        SetInterval = (snapshot, value) => snapshot.CnrDailyNewsAutoRotateIntervalMinutes = value,
                        ChangedKeys =
                        [
                            nameof(ComponentSettingsSnapshot.CnrDailyNewsAutoRotateEnabled),
                            nameof(ComponentSettingsSnapshot.CnrDailyNewsAutoRotateIntervalMinutes)
                        ]
                    })),
            [BuiltInComponentIds.DesktopIfengNews] = new(
                BuiltInComponentIds.DesktopIfengNews,
                context => new ToggleIntervalComponentEditor(
                    context,
                    new ToggleIntervalComponentEditorOptions
                    {
                        DescriptionKey = "ifeng.settings.desc",
                        DescriptionFallback = "Configure auto refresh and source channel for this iFeng widget.",
                        ToggleLabelKey = "ifeng.settings.auto_refresh",
                        ToggleLabelFallback = "Auto refresh",
                        ToggleDescriptionKey = "component.editor.instance_scope",
                        ToggleDescriptionFallback = "Changes are stored per component instance.",
                        IntervalLabelKey = "ifeng.settings.refresh_interval",
                        IntervalLabelFallback = "Refresh interval",
                        DefaultInterval = 20,
                        GetEnabled = snapshot => snapshot.IfengNewsAutoRefreshEnabled,
                        SetEnabled = (snapshot, value) => snapshot.IfengNewsAutoRefreshEnabled = value,
                        GetInterval = snapshot => snapshot.IfengNewsAutoRefreshIntervalMinutes,
                        SetInterval = (snapshot, value) => snapshot.IfengNewsAutoRefreshIntervalMinutes = value,
                        ExtraSelectorLabelKey = "ifeng.settings.channel",
                        ExtraSelectorLabelFallback = "Channel",
                        ExtraOptions =
                        [
                            new ComponentEditorSelectionOption(
                                IfengNewsChannelTypes.Comprehensive,
                                "ifeng.settings.channel.comprehensive",
                                "Comprehensive"),
                            new ComponentEditorSelectionOption(
                                IfengNewsChannelTypes.Mainland,
                                "ifeng.settings.channel.mainland",
                                "Mainland"),
                            new ComponentEditorSelectionOption(
                                IfengNewsChannelTypes.Taiwan,
                                "ifeng.settings.channel.taiwan",
                                "Taiwan")
                        ],
                        GetExtraValue = snapshot => IfengNewsChannelTypes.Normalize(snapshot.IfengNewsChannelType),
                        SetExtraValue = (snapshot, value) => snapshot.IfengNewsChannelType = IfengNewsChannelTypes.Normalize(value),
                        ChangedKeys =
                        [
                            nameof(ComponentSettingsSnapshot.IfengNewsAutoRefreshEnabled),
                            nameof(ComponentSettingsSnapshot.IfengNewsAutoRefreshIntervalMinutes),
                            nameof(ComponentSettingsSnapshot.IfengNewsChannelType)
                        ]
                    })),
            [BuiltInComponentIds.DesktopDailyWord] = CreateDailyWordRegistration(BuiltInComponentIds.DesktopDailyWord),
            [BuiltInComponentIds.DesktopDailyWord2x2] = CreateDailyWordRegistration(BuiltInComponentIds.DesktopDailyWord2x2),
            [BuiltInComponentIds.DesktopBilibiliHotSearch] = new(
                BuiltInComponentIds.DesktopBilibiliHotSearch,
                context => new ToggleIntervalComponentEditor(
                    context,
                    new ToggleIntervalComponentEditorOptions
                    {
                        DescriptionKey = "bilibili.settings.desc",
                        DescriptionFallback = "Configure auto refresh for this Bilibili hot search widget.",
                        ToggleLabelKey = "bilibili.settings.auto_refresh",
                        ToggleLabelFallback = "Auto refresh",
                        ToggleDescriptionKey = "component.editor.instance_scope",
                        ToggleDescriptionFallback = "Changes are stored per component instance.",
                        IntervalLabelKey = "bilibili.settings.refresh_interval",
                        IntervalLabelFallback = "Refresh interval",
                        DefaultInterval = 15,
                        GetEnabled = snapshot => snapshot.BilibiliHotSearchAutoRefreshEnabled,
                        SetEnabled = (snapshot, value) => snapshot.BilibiliHotSearchAutoRefreshEnabled = value,
                        GetInterval = snapshot => snapshot.BilibiliHotSearchAutoRefreshIntervalMinutes,
                        SetInterval = (snapshot, value) => snapshot.BilibiliHotSearchAutoRefreshIntervalMinutes = value,
                        ChangedKeys =
                        [
                            nameof(ComponentSettingsSnapshot.BilibiliHotSearchAutoRefreshEnabled),
                            nameof(ComponentSettingsSnapshot.BilibiliHotSearchAutoRefreshIntervalMinutes)
                        ]
                    })),
            [BuiltInComponentIds.DesktopBaiduHotSearch] = new(
                BuiltInComponentIds.DesktopBaiduHotSearch,
                context => new ToggleIntervalComponentEditor(
                    context,
                    new ToggleIntervalComponentEditorOptions
                    {
                        DescriptionKey = "baidu.settings.desc",
                        DescriptionFallback = "Configure auto refresh and source for this Baidu hot search widget.",
                        ToggleLabelKey = "baidu.settings.auto_refresh",
                        ToggleLabelFallback = "Auto refresh",
                        ToggleDescriptionKey = "component.editor.instance_scope",
                        ToggleDescriptionFallback = "Changes are stored per component instance.",
                        IntervalLabelKey = "baidu.settings.refresh_interval",
                        IntervalLabelFallback = "Refresh interval",
                        DefaultInterval = 15,
                        GetEnabled = snapshot => snapshot.BaiduHotSearchAutoRefreshEnabled,
                        SetEnabled = (snapshot, value) => snapshot.BaiduHotSearchAutoRefreshEnabled = value,
                        GetInterval = snapshot => snapshot.BaiduHotSearchAutoRefreshIntervalMinutes,
                        SetInterval = (snapshot, value) => snapshot.BaiduHotSearchAutoRefreshIntervalMinutes = value,
                        ExtraSelectorLabelKey = "baidu.settings.source",
                        ExtraSelectorLabelFallback = "Source",
                        ExtraOptions =
                        [
                            new ComponentEditorSelectionOption(
                                BaiduHotSearchSourceTypes.Official,
                                "baidu.settings.source.official",
                                "Official"),
                            new ComponentEditorSelectionOption(
                                BaiduHotSearchSourceTypes.ThirdPartyRss,
                                "baidu.settings.source.third_party",
                                "Third-party RSS")
                        ],
                        GetExtraValue = snapshot => BaiduHotSearchSourceTypes.Normalize(snapshot.BaiduHotSearchSourceType),
                        SetExtraValue = (snapshot, value) => snapshot.BaiduHotSearchSourceType = BaiduHotSearchSourceTypes.Normalize(value),
                        ChangedKeys =
                        [
                            nameof(ComponentSettingsSnapshot.BaiduHotSearchAutoRefreshEnabled),
                            nameof(ComponentSettingsSnapshot.BaiduHotSearchAutoRefreshIntervalMinutes),
                            nameof(ComponentSettingsSnapshot.BaiduHotSearchSourceType)
                        ]
                    })),
            [BuiltInComponentIds.DesktopStcn24Forum] = new(
                BuiltInComponentIds.DesktopStcn24Forum,
                context => new ToggleIntervalComponentEditor(
                    context,
                    new ToggleIntervalComponentEditorOptions
                    {
                        DescriptionKey = "stcn.settings.desc",
                        DescriptionFallback = "Configure auto refresh and sort mode for this STCN forum widget.",
                        ToggleLabelKey = "stcn.settings.auto_refresh",
                        ToggleLabelFallback = "Auto refresh",
                        ToggleDescriptionKey = "component.editor.instance_scope",
                        ToggleDescriptionFallback = "Changes are stored per component instance.",
                        IntervalLabelKey = "stcn.settings.refresh_interval",
                        IntervalLabelFallback = "Refresh interval",
                        DefaultInterval = 20,
                        GetEnabled = snapshot => snapshot.Stcn24ForumAutoRefreshEnabled,
                        SetEnabled = (snapshot, value) => snapshot.Stcn24ForumAutoRefreshEnabled = value,
                        GetInterval = snapshot => snapshot.Stcn24ForumAutoRefreshIntervalMinutes,
                        SetInterval = (snapshot, value) => snapshot.Stcn24ForumAutoRefreshIntervalMinutes = value,
                        ExtraSelectorLabelKey = "stcn.settings.sort_mode",
                        ExtraSelectorLabelFallback = "Sort mode",
                        ExtraOptions = Stcn24ForumSourceTypes.SupportedValues
                            .Select(value => new ComponentEditorSelectionOption(
                                value,
                                $"stcn.settings.source.{value}",
                                value))
                            .ToArray(),
                        GetExtraValue = snapshot => Stcn24ForumSourceTypes.Normalize(snapshot.Stcn24ForumSourceType),
                        SetExtraValue = (snapshot, value) => snapshot.Stcn24ForumSourceType = Stcn24ForumSourceTypes.Normalize(value),
                        ChangedKeys =
                        [
                            nameof(ComponentSettingsSnapshot.Stcn24ForumAutoRefreshEnabled),
                            nameof(ComponentSettingsSnapshot.Stcn24ForumAutoRefreshIntervalMinutes),
                            nameof(ComponentSettingsSnapshot.Stcn24ForumSourceType)
                        ]
                    })),
            [BuiltInComponentIds.DesktopZhiJiaoHub] = new(
                BuiltInComponentIds.DesktopZhiJiaoHub,
                context => new ZhiJiaoHubComponentEditor(context),
                preferredWidth: 480d,
                preferredHeight: 520d),
            [BuiltInComponentIds.DesktopNotificationBox] = new(
                BuiltInComponentIds.DesktopNotificationBox,
                context => new NotificationBoxComponentEditor(context),
                preferredWidth: 480d,
                preferredHeight: 520d)
        };

        foreach (var componentId in GetBuiltInDesktopComponentIds(componentRegistry))
        {
            if (registrations.ContainsKey(componentId))
            {
                continue;
            }

            registrations[componentId] = new DesktopComponentEditorRegistration(
                componentId,
                context => new InformationalComponentEditor(
                    context,
                    $"This {context.Definition.DisplayName} component currently exposes instance-scoped editor metadata only."));
        }

        return registrations.Values;
    }

    private static IEnumerable<string> GetBuiltInDesktopComponentIds(ComponentRegistry componentRegistry)
    {
        return typeof(BuiltInComponentIds)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(string))
            .Select(field => field.GetRawConstantValue() as string)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .Where(id => componentRegistry.TryGetDefinition(id, out var definition) && definition.AllowDesktopPlacement)
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static DesktopComponentEditorRegistration CreateWeatherRegistration(string componentId)
    {
        return new DesktopComponentEditorRegistration(
            componentId,
            context => new ToggleIntervalComponentEditor(
                context,
                new ToggleIntervalComponentEditorOptions
                {
                    DescriptionKey = "weather.settings.desc",
                    DescriptionFallback = "Configure weather auto refresh for this component instance.",
                    ToggleLabelKey = "weather.settings.auto_refresh",
                    ToggleLabelFallback = "Auto refresh",
                    ToggleDescriptionKey = "component.editor.instance_scope",
                    ToggleDescriptionFallback = "Changes are stored per component instance.",
                    IntervalLabelKey = "weather.settings.refresh_interval",
                    IntervalLabelFallback = "Refresh interval",
                    DefaultInterval = 12,
                    GetEnabled = snapshot => snapshot.WeatherAutoRefreshEnabled,
                    SetEnabled = (snapshot, value) => snapshot.WeatherAutoRefreshEnabled = value,
                    GetInterval = snapshot => snapshot.WeatherAutoRefreshIntervalMinutes,
                    SetInterval = (snapshot, value) => snapshot.WeatherAutoRefreshIntervalMinutes = value,
                    ChangedKeys =
                    [
                        nameof(ComponentSettingsSnapshot.WeatherAutoRefreshEnabled),
                        nameof(ComponentSettingsSnapshot.WeatherAutoRefreshIntervalMinutes)
                    ]
                }));
    }

    private static DesktopComponentEditorRegistration CreateDailyWordRegistration(string componentId)
    {
        return new DesktopComponentEditorRegistration(
            componentId,
            context => new ToggleIntervalComponentEditor(
                context,
                new ToggleIntervalComponentEditorOptions
                {
                    DescriptionKey = "dailyword.settings.desc",
                    DescriptionFallback = "Configure auto refresh for this Daily Word component.",
                    ToggleLabelKey = "dailyword.settings.auto_refresh",
                    ToggleLabelFallback = "Auto refresh",
                    ToggleDescriptionKey = "component.editor.instance_scope",
                    ToggleDescriptionFallback = "Changes are stored per component instance.",
                    IntervalLabelKey = "dailyword.settings.refresh_interval",
                    IntervalLabelFallback = "Refresh interval",
                    DefaultInterval = 360,
                    GetEnabled = snapshot => snapshot.DailyWordAutoRefreshEnabled,
                    SetEnabled = (snapshot, value) => snapshot.DailyWordAutoRefreshEnabled = value,
                    GetInterval = snapshot => snapshot.DailyWordAutoRefreshIntervalMinutes,
                    SetInterval = (snapshot, value) => snapshot.DailyWordAutoRefreshIntervalMinutes = value,
                    ChangedKeys =
                    [
                        nameof(ComponentSettingsSnapshot.DailyWordAutoRefreshEnabled),
                        nameof(ComponentSettingsSnapshot.DailyWordAutoRefreshIntervalMinutes)
                    ]
                }));
    }

    private static Control CreatePluginEditor(
        PluginDesktopComponentEditorContribution contribution,
        DesktopComponentEditorContext context)
    {
        var settingsService = contribution.Plugin.Services.GetService(typeof(ISettingsService)) as ISettingsService
            ?? context.SettingsService;
        var pluginSettings = new PluginScopedSettingsService(
            contribution.Plugin.Manifest.Id,
            settingsService);
        var pluginContext = new PluginDesktopComponentEditorContext(
            contribution.Plugin.Manifest,
            contribution.Plugin.Context.PluginDirectory,
            contribution.Plugin.Context.DataDirectory,
            contribution.Plugin.Services,
            contribution.Plugin.Context.Properties,
            context.ComponentId,
            context.PlacementId,
            pluginSettings,
            context.HostContext);

        return contribution.Registration.EditorFactory(contribution.Plugin.Services, pluginContext);
    }
}
