using LanMountainDesktop.PluginSdk;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace VoiceHubLanDesktop;

/// <summary>
/// VoiceHub 广播站排期插件入口
/// </summary>
[PluginEntrance]
public sealed class VoiceHubPlugin : PluginBase
{
    public override void Initialize(HostBuilderContext context, IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(services);

        var localizer = CreateLocalizer(context);

        // 注册服务
        services.AddSingleton<VoiceHubApiService>();
        services.AddSingleton<VoiceHubScheduleService>();

        // 注册桌面组件 - 最小 3x4 网格，允许等比例缩放
        services.AddPluginDesktopComponent<VoiceHubScheduleWidget>(
            CreateScheduleComponentOptions(localizer));

        // 注册设置页面
        services.AddPluginSettingsSection(
            id: "voicehub-settings",
            titleLocalizationKey: "settings.title",
            configure: builder =>
            {
                builder.AddText(
                    key: "apiUrl",
                    titleLocalizationKey: "settings.apiUrl.title",
                    descriptionLocalizationKey: "settings.apiUrl.description",
                    defaultValue: "https://voicehub.lao-shui.top/api/songs/public");

                builder.AddBoolean(
                    key: "showRequester",
                    titleLocalizationKey: "settings.showRequester.title",
                    descriptionLocalizationKey: "settings.showRequester.description",
                    defaultValue: true);

                builder.AddBoolean(
                    key: "showVoteCount",
                    titleLocalizationKey: "settings.showVoteCount.title",
                    descriptionLocalizationKey: "settings.showVoteCount.description",
                    defaultValue: false);

                builder.AddSelection(
                    key: "refreshInterval",
                    titleLocalizationKey: "settings.refreshInterval.title",
                    descriptionLocalizationKey: "settings.refreshInterval.description",
                    defaultValue: "60",
                    choices:
                    [
                        new SettingsOptionChoice("5分钟", "5"),
                        new SettingsOptionChoice("15分钟", "15"),
                        new SettingsOptionChoice("30分钟", "30"),
                        new SettingsOptionChoice("1小时", "60"),
                        new SettingsOptionChoice("2小时", "120")
                    ]);
            },
            descriptionLocalizationKey: "settings.description",
            iconKey: "Settings",
            sortOrder: 0);
    }

    private static PluginLocalizer CreateLocalizer(HostBuilderContext context)
    {
        var pluginDirectory = context.Properties.TryGetValue("LanMountainDesktop.PluginDirectory", out var directoryValue) &&
                              directoryValue is string resolvedPluginDirectory &&
                              !string.IsNullOrWhiteSpace(resolvedPluginDirectory)
            ? resolvedPluginDirectory
            : AppContext.BaseDirectory;

        var properties = context.Properties
            .Where(pair => pair.Key is string)
            .ToDictionary(pair => (string)pair.Key, pair => (object?)pair.Value, StringComparer.OrdinalIgnoreCase);

        return new PluginLocalizer(pluginDirectory, PluginLocalizer.ResolveLanguageCode(properties));
    }

    private static PluginDesktopComponentOptions CreateScheduleComponentOptions(PluginLocalizer localizer)
    {
        return new PluginDesktopComponentOptions
        {
            ComponentId = "com.voicehub.schedule",
            DisplayName = localizer.GetString("widget.display_name", "广播站排期"),
            DisplayNameLocalizationKey = "widget.display_name",
            IconKey = "Radio",
            Category = localizer.GetString("widget.category", "信息"),
            MinWidthCells = 3,
            MinHeightCells = 4,
            AllowDesktopPlacement = true,
            AllowStatusBarPlacement = false,
            ResizeMode = PluginDesktopComponentResizeMode.Proportional,
            CornerRadiusPreset = PluginCornerRadiusPreset.Default
        };
    }
}
