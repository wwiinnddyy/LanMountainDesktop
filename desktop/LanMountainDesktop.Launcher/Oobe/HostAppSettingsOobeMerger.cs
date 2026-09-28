using System.Text.Json;
using LanMountainDesktop.Shared.Data;
using System.Text.Json.Nodes;
using LanMountainDesktop.Shared.Contracts.Launcher;
using LanMountainDesktop.Shared.IO;

namespace LanMountainDesktop.Launcher.Oobe;

/// <summary>
/// 在 OOBE 中向 Host 的 settings.json 写入启动与展示相关字段，属性名与 Host
/// AppSettingsSnapshot 的 JSON 序列化一致（PascalCase）。
/// </summary>
public static class HostAppSettingsOobeMerger
{
    public const string ShowInTaskbarKey = "ShowInTaskbar";
    public const string EnableFadeTransitionKey = "EnableFadeTransition";
    public const string EnableSlideTransitionKey = "EnableSlideTransition";
    public const string EnableFusedDesktopKey = "EnableFusedDesktop";
    public const string EnableThreeFingerSwipeKey = "EnableThreeFingerSwipe";
    public const string AutoStartWithWindowsKey = "AutoStartWithWindows";
    public const string MultiInstanceLaunchBehaviorKey = "MultiInstanceLaunchBehavior";
    public const string ThemeModeKey = "ThemeMode";

    /// <summary>
    /// 两个遥测开关的键名＝宿主 <c>AppSettingsSnapshot</c> 的属性名（跨二进制只靠字符串对齐，
    /// 拼错一个字母不报错，症状是"向导里选了不开、宿主照旧上报"）。
    /// 由 <c>OobeHostSettingsContractTests</c> 逐个核对宿主有没有这个属性。
    /// </summary>
    public const string UploadAnonymousCrashDataKey = "UploadAnonymousCrashData";
    public const string UploadAnonymousUsageDataKey = "UploadAnonymousUsageData";

    /// <summary>
    /// 主题档的取值是宿主的口径（<c>AppSettingsSnapshot.ThemeMode</c>）。
    /// 这里能取到宿主常量类的话就该换成常量；取不到是因为 Launcher 与宿主是两个二进制，
    /// 由 <c>OobeHostSettingsContractTests</c> 钉住这两个字面量与宿主常量一致。
    /// </summary>
    public const string ThemeModeLightValue = "light";

    public const string ThemeModeDarkValue = "dark";

    public static string GetSettingsFilePath(string dataRoot) =>
        Path.Combine(Path.GetFullPath(dataRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)), UserDataRoot.SettingsFileName);

    public static HostAppSettingsStartupDefaults LoadStartupDefaults(string settingsPath)
    {
        if (!File.Exists(settingsPath))
        {
            return HostAppSettingsStartupDefaults.Fallback;
        }

        try
        {
            var root = JsonNode.Parse(File.ReadAllText(settingsPath))?.AsObject();
            if (root is null)
            {
                return HostAppSettingsStartupDefaults.Fallback;
            }

            var fade = ReadBool(root, EnableFadeTransitionKey, defaultValue: true);
            var slide = ReadBool(root, EnableSlideTransitionKey, defaultValue: false);
            var normalized = StartupVisualPreferencesResolver.FromFlags(fade, slide);

            return new HostAppSettingsStartupDefaults(
                ShowInTaskbar: ReadBool(root, ShowInTaskbarKey, defaultValue: false),
                EnableFadeTransition: normalized.EnableFadeTransition,
                EnableSlideTransition: normalized.EnableSlideTransition,
                FusedPopupExperience: ReadBool(root, EnableFusedDesktopKey, defaultValue: false) &&
                                      ReadBool(root, EnableThreeFingerSwipeKey, defaultValue: false),
                AutoStartWithWindows: ReadBool(root, AutoStartWithWindowsKey, defaultValue: false));
        }
        catch (Exception ex)
        {
            Logger.Warn($"HostAppSettingsOobeMerger: failed to read '{settingsPath}'. {ex.Message}");
            return HostAppSettingsStartupDefaults.Fallback;
        }
    }

    public static MultiInstanceLaunchBehavior LoadMultiInstanceLaunchBehavior(string settingsPath)
    {
        if (!File.Exists(settingsPath))
        {
            return MultiInstanceLaunchBehavior.NotifyAndOpenDesktop;
        }

        try
        {
            var root = JsonNode.Parse(File.ReadAllText(settingsPath))?.AsObject();
            if (root is null)
            {
                return MultiInstanceLaunchBehavior.NotifyAndOpenDesktop;
            }

            return ReadMultiInstanceLaunchBehavior(root);
        }
        catch (Exception ex)
        {
            Logger.Warn($"HostAppSettingsOobeMerger: failed to read multi-instance behavior from '{settingsPath}'. {ex.Message}");
            return MultiInstanceLaunchBehavior.NotifyAndOpenDesktop;
        }
    }

    public static void MergeStartupPresentation(string settingsPath, HostAppSettingsStartupChoices choices)
    {
        var root = ReadOrCreateSettingsObject(settingsPath);

        var normalized = StartupVisualPreferencesResolver.FromFlags(
            choices.EnableFadeTransition,
            choices.EnableSlideTransition);

        root[ShowInTaskbarKey] = choices.ShowInTaskbar;
        root[EnableFadeTransitionKey] = normalized.EnableFadeTransition;
        root[EnableSlideTransitionKey] = normalized.EnableSlideTransition;
        root[EnableFusedDesktopKey] = choices.FusedPopupExperience;
        root[EnableThreeFingerSwipeKey] = choices.FusedPopupExperience;
        root[AutoStartWithWindowsKey] = choices.AutoStartWithWindows;
        root[ThemeModeKey] = string.IsNullOrWhiteSpace(choices.ThemeMode)
            ? ThemeModeLightValue
            : choices.ThemeMode;

        WriteSettingsObject(settingsPath, root);
    }

    /// <summary>
    /// 把向导第五步答的两个遥测开关落到<b>宿主自己读的那份</b> settings.json 上。
    /// 这两个键此前只写进向导目录里的 <c>privacy-config.json</c>，而全仓没有任何读者，
    /// 宿主的两道闸读的是 <c>AppSettingsSnapshot.UploadAnonymousCrashData</c> /
    /// <c>UploadAnonymousUsageData</c>——于是用户在向导里选了"不开"，宿主照旧上报。
    /// 与 <see cref="MergeStartupPresentation"/> 同样是覆盖写：重跑向导就是重新回答，
    /// 与启动/主题那几个键一个口径。
    /// </summary>
    public static void MergePrivacyChoices(
        string settingsPath,
        bool crashTelemetryEnabled,
        bool usageTelemetryEnabled)
    {
        var root = ReadOrCreateSettingsObject(settingsPath);

        root[UploadAnonymousCrashDataKey] = crashTelemetryEnabled;
        root[UploadAnonymousUsageDataKey] = usageTelemetryEnabled;

        WriteSettingsObject(settingsPath, root);
    }

    private static JsonObject ReadOrCreateSettingsObject(string settingsPath)
    {
        var directory = Path.GetDirectoryName(settingsPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (!File.Exists(settingsPath))
        {
            return new JsonObject();
        }

        try
        {
            return JsonNode.Parse(File.ReadAllText(settingsPath))?.AsObject() ?? new JsonObject();
        }
        catch (Exception ex)
        {
            Logger.Warn($"HostAppSettingsOobeMerger: replacing invalid JSON at '{settingsPath}'. {ex.Message}");
            return new JsonObject();
        }
    }

    private static void WriteSettingsObject(string settingsPath, JsonObject root)
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        AtomicFileWriter.WriteText(settingsPath, root.ToJsonString(options), "OOBE");
    }

    private static bool ReadBool(JsonObject root, string key, bool defaultValue)
    {
        if (!root.TryGetPropertyValue(key, out var node) || node is null)
        {
            return defaultValue;
        }

        return node switch
        {
            JsonValue v when v.TryGetValue<bool>(out var b) => b,
            JsonValue v when v.TryGetValue<string>(out var s) => bool.TryParse(s, out var p) && p,
            _ => defaultValue
        };
    }

    private static MultiInstanceLaunchBehavior ReadMultiInstanceLaunchBehavior(JsonObject root)
    {
        if (!root.TryGetPropertyValue(MultiInstanceLaunchBehaviorKey, out var node) || node is null)
        {
            return MultiInstanceLaunchBehavior.NotifyAndOpenDesktop;
        }

        if (node is JsonValue value)
        {
            if (value.TryGetValue<string>(out var text) &&
                Enum.TryParse<MultiInstanceLaunchBehavior>(text, ignoreCase: true, out var parsed))
            {
                return parsed;
            }

            if (value.TryGetValue<int>(out var numeric) &&
                Enum.IsDefined(typeof(MultiInstanceLaunchBehavior), numeric))
            {
                return (MultiInstanceLaunchBehavior)numeric;
            }
        }

        return MultiInstanceLaunchBehavior.NotifyAndOpenDesktop;
    }
}

public readonly record struct HostAppSettingsStartupDefaults(
    bool ShowInTaskbar,
    bool EnableFadeTransition,
    bool EnableSlideTransition,
    bool FusedPopupExperience,
    bool AutoStartWithWindows)
{
    public static HostAppSettingsStartupDefaults Fallback { get; } = new(
        ShowInTaskbar: false,
        EnableFadeTransition: true,
        EnableSlideTransition: false,
        FusedPopupExperience: false,
        AutoStartWithWindows: false);
}

public readonly record struct HostAppSettingsStartupChoices(
    bool ShowInTaskbar,
    bool EnableFadeTransition,
    bool EnableSlideTransition,
    bool FusedPopupExperience,
    bool AutoStartWithWindows,
    string ThemeMode = HostAppSettingsOobeMerger.ThemeModeLightValue);
