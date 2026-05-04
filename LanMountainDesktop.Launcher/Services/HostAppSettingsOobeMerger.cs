using System.Text.Json;
using System.Text.Json.Nodes;
using LanMountainDesktop.Shared.Contracts.Launcher;

namespace LanMountainDesktop.Launcher.Services;

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

    public static string GetSettingsFilePath(string dataRoot) =>
        Path.Combine(Path.GetFullPath(dataRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)), "settings.json");

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

    public static void MergeStartupPresentation(string settingsPath, HostAppSettingsStartupChoices choices)
    {
        var directory = Path.GetDirectoryName(settingsPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        JsonObject root;
        if (File.Exists(settingsPath))
        {
            try
            {
                root = JsonNode.Parse(File.ReadAllText(settingsPath))?.AsObject() ?? new JsonObject();
            }
            catch (Exception ex)
            {
                Logger.Warn($"HostAppSettingsOobeMerger: replacing invalid JSON at '{settingsPath}'. {ex.Message}");
                root = new JsonObject();
            }
        }
        else
        {
            root = new JsonObject();
        }

        var normalized = StartupVisualPreferencesResolver.FromFlags(
            choices.EnableFadeTransition,
            choices.EnableSlideTransition);

        root[ShowInTaskbarKey] = choices.ShowInTaskbar;
        root[EnableFadeTransitionKey] = normalized.EnableFadeTransition;
        root[EnableSlideTransitionKey] = normalized.EnableSlideTransition;
        root[EnableFusedDesktopKey] = choices.FusedPopupExperience;
        root[EnableThreeFingerSwipeKey] = choices.FusedPopupExperience;
        root[AutoStartWithWindowsKey] = choices.AutoStartWithWindows;

        var options = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(settingsPath, root.ToJsonString(options));
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
    bool AutoStartWithWindows);
