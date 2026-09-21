using System.Text.Json;
using LanMountainDesktop.Shared.Contracts.Data;
using LanMountainDesktop.Shared.Contracts.Deployment;

namespace LanMountainDesktop.Shared.IPC;

public static class AirAppRuntimeDataRootResolver
{

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
        var configPath = Path.Combine(
            normalizedAppRoot,
            DeploymentLayout.LauncherStateDirectoryName,
            DataLocationContract.ConfigFileName);
        if (!File.Exists(configPath))
        {
            return defaultSystemDataPath;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(configPath));
            var root = document.RootElement;
            var mode = GetString(root, DataLocationContract.ModePropertyName);

            if (string.Equals(mode, DataLocationContract.PortableModeValue, StringComparison.OrdinalIgnoreCase))
            {
                return Path.GetFullPath(
                    GetString(root, DataLocationContract.PortablePathPropertyName)
                    ?? Path.Combine(normalizedAppRoot, DataLocationContract.DesktopFolderName));
            }

            return Path.GetFullPath(GetString(root, DataLocationContract.SystemPathPropertyName) ?? defaultSystemDataPath);
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
