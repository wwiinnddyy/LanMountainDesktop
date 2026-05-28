using System.IO.Compression;
using System.Text.Json;

namespace LanMountainDesktop.PluginPackaging;

public static class PluginPackageManifestReader
{
    public static PluginPackageManifest Read(string packagePath, bool includeLegacyManifest = false)
    {
        using var archive = ZipFile.OpenRead(packagePath);
        var entries = FindManifestEntries(archive, PluginPackagingConstants.ManifestFileName);
        if (entries.Length == 0 && includeLegacyManifest)
        {
            entries = FindManifestEntries(archive, PluginPackagingConstants.LegacyManifestFileName);
        }

        if (entries.Length == 0)
        {
            var expected = includeLegacyManifest
                ? $"'{PluginPackagingConstants.ManifestFileName}' or '{PluginPackagingConstants.LegacyManifestFileName}'"
                : $"'{PluginPackagingConstants.ManifestFileName}'";
            throw new InvalidOperationException($"Plugin package '{packagePath}' does not contain {expected}.");
        }

        if (entries.Length > 1)
        {
            throw new InvalidOperationException(
                $"Plugin package '{packagePath}' contains multiple '{PluginPackagingConstants.ManifestFileName}' files.");
        }

        using var stream = entries[0].Open();
        using var reader = new StreamReader(stream);
        var json = reader.ReadToEnd();
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var id = ReadRequiredString(root, "id");
        var name = ReadRequiredString(root, "name");
        var version = ReadOptionalString(root, "version") ?? string.Empty;
        return new PluginPackageManifest(id, name, version);
    }

    private static ZipArchiveEntry[] FindManifestEntries(ZipArchive archive, string manifestFileName)
    {
        return archive.Entries
            .Where(entry => string.Equals(entry.Name, manifestFileName, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    private static string ReadRequiredString(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value) ||
            value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw new InvalidOperationException($"Plugin manifest is missing required property '{propertyName}'.");
        }

        return value.GetString()!;
    }

    private static string? ReadOptionalString(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return value.GetString();
    }
}
