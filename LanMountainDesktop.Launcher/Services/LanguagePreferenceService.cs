using System.Globalization;
using System.Text.Json.Nodes;

namespace LanMountainDesktop.Launcher.Services;

internal static class LanguagePreferenceService
{
    public static string ResolveLanguageCode(string appRoot)
    {
        try
        {
            var dataLocationResolver = new DataLocationResolver(appRoot);
            var settingsPath = HostAppSettingsOobeMerger.GetSettingsFilePath(dataLocationResolver.ResolveDataRoot());
            if (!File.Exists(settingsPath))
            {
                return "zh-CN";
            }

            var root = JsonNode.Parse(File.ReadAllText(settingsPath))?.AsObject();
            if (root is not null &&
                root.TryGetPropertyValue("LanguageCode", out var node) &&
                node is JsonValue value &&
                value.TryGetValue<string>(out var code) &&
                !string.IsNullOrWhiteSpace(code))
            {
                return NormalizeLanguageCode(code);
            }
        }
        catch
        {
        }

        return "zh-CN";
    }

    public static void ApplyLanguage(string languageCode)
    {
        var normalized = NormalizeLanguageCode(languageCode);
        var culture = CultureInfo.GetCultureInfo(normalized);
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        Thread.CurrentThread.CurrentCulture = culture;
        Thread.CurrentThread.CurrentUICulture = culture;
    }

    private static string NormalizeLanguageCode(string code)
    {
        return code.ToLowerInvariant() switch
        {
            "en-us" or "en" => "en-US",
            "ja-jp" or "ja" => "ja-JP",
            "ko-kr" or "ko" => "ko-KR",
            _ => "zh-CN"
        };
    }
}
