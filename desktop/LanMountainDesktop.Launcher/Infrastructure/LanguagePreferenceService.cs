using System.Globalization;
using System.Text.Json.Nodes;
using LanMountainDesktop.Shared.Contracts.Localization;

namespace LanMountainDesktop.Launcher.Infrastructure;

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
                return LanguageCodes.Default;
            }

            var root = JsonNode.Parse(File.ReadAllText(settingsPath))?.AsObject();
            if (root is not null &&
                root.TryGetPropertyValue("LanguageCode", out var node) &&
                node is JsonValue value &&
                value.TryGetValue<string>(out var code) &&
                !string.IsNullOrWhiteSpace(code))
            {
                return LanguageCodes.Normalize(code);
            }
        }
        catch
        {
        }

        return LanguageCodes.Default;
    }

    public static void ApplyLanguage(string languageCode)
    {
        var normalized = LanguageCodes.Normalize(languageCode);
        var culture = CultureInfo.GetCultureInfo(normalized);
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        Thread.CurrentThread.CurrentCulture = culture;
        Thread.CurrentThread.CurrentUICulture = culture;
    }
}
