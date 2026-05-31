using System.Text.Json;

namespace LanMountainDesktop.Shared.IPC;

public static class AirAppRuntimeDataRootResolver
{
    private const string LauncherDataFolderName = ".Launcher";
    private const string ConfigFileName = "data-location.config.json";
    private const string DesktopFolderName = "Desktop";

    public static string ResolveDataRoot(string? appRoot)
    {
        var defaultSystemDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LanMountainDesktop");

        if (string.IsNullOrWhiteSpace(appRoot))
        {
            return defaultSystemDataPath;
        }

        var normalizedAppRoot = Path.GetFullPath(appRoot);
        var configPath = Path.Combine(normalizedAppRoot, LauncherDataFolderName, ConfigFileName);
        if (!File.Exists(configPath))
        {
            return defaultSystemDataPath;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(configPath));
            var root = document.RootElement;
            var mode = GetString(root, "dataLocationMode");

            if (string.Equals(mode, "Portable", StringComparison.OrdinalIgnoreCase))
            {
                return Path.GetFullPath(
                    GetString(root, "portableDataPath")
                    ?? Path.Combine(normalizedAppRoot, DesktopFolderName));
            }

            return Path.GetFullPath(GetString(root, "systemDataPath") ?? defaultSystemDataPath);
        }
        catch
        {
            return defaultSystemDataPath;
        }
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase) &&
                property.Value.ValueKind is JsonValueKind.String)
            {
                var value = property.Value.GetString();
                return string.IsNullOrWhiteSpace(value) ? null : value;
            }
        }

        return null;
    }
}
