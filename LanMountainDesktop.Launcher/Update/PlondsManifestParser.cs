using System.Text.Json;
using LanMountainDesktop.Launcher.Models;

namespace LanMountainDesktop.Launcher.Update;

internal static class PlondsManifestParser
{
    public static List<PlondsFileEntry> CollectFileEntries(PlondsFileMap fileMap)
    {
        var files = new List<PlondsFileEntry>();
        if (fileMap.Files is { Count: > 0 })
        {
            files.AddRange(fileMap.Files);
        }

        if (fileMap.Components is null)
        {
            return files;
        }

        foreach (var component in fileMap.Components)
        {
            if (component.Files is { Count: > 0 })
            {
                files.AddRange(component.Files);
            }
        }

        return files;
    }

    public static void PopulateFromRawJson(string fileMapJson, PlondsFileMap fileMap, ICollection<PlondsFileEntry> files)
    {
        if (string.IsNullOrWhiteSpace(fileMapJson))
        {
            return;
        }

        using var document = JsonDocument.Parse(fileMapJson);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        fileMap.FromVersion ??= ReadStringIgnoreCase(root, "fromversion");
        fileMap.ToVersion ??= ReadStringIgnoreCase(root, "toversion");
        fileMap.Version ??= ReadStringIgnoreCase(root, "version");
        fileMap.Platform ??= ReadStringIgnoreCase(root, "platform");
        fileMap.Arch ??= ReadStringIgnoreCase(root, "arch");
        fileMap.DistributionId ??= ReadStringIgnoreCase(root, "distributionid");
        PopulateMetadata(root, fileMap.Metadata);

        if (TryGetPropertyIgnoreCase(root, "files", out var rootFilesNode))
        {
            ParseFilesNode(rootFilesNode, null, files);
        }

        if (TryGetPropertyIgnoreCase(root, "components", out var componentsNode))
        {
            ParseComponentsNode(componentsNode, files);
        }
    }

    public static PlondsUpdateMetadata? LoadMetadata(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var text = File.ReadAllText(path);
            return string.IsNullOrWhiteSpace(text)
                ? null
                : JsonSerializer.Deserialize(text, AppJsonContext.Default.PlondsUpdateMetadata);
        }
        catch
        {
            return null;
        }
    }

    public static string? ResolveSourceVersion(PlondsFileMap fileMap, PlondsUpdateMetadata? metadata)
    {
        return FirstNonEmpty(
            metadata?.FromVersion,
            fileMap.FromVersion,
            TryGetMetadataValue(fileMap.Metadata, "fromVersion"),
            TryGetMetadataValue(fileMap.Metadata, "sourceVersion"));
    }

    public static string? ResolveTargetVersion(PlondsFileMap fileMap, PlondsUpdateMetadata? metadata)
    {
        return FirstNonEmpty(
            metadata?.ToVersion,
            fileMap.ToVersion,
            fileMap.Version,
            TryGetMetadataValue(fileMap.Metadata, "toVersion"),
            TryGetMetadataValue(fileMap.Metadata, "targetVersion"));
    }

    public static bool TryGetExpectedSha512(PlondsFileEntry file, out byte[] expected)
    {
        expected = [];
        if (file.Sha512Bytes is { Length: > 0 })
        {
            expected = file.Sha512Bytes;
            return true;
        }

        if (file.Hash is not null)
        {
            if (file.Hash.Bytes is { Length: > 0 })
            {
                expected = file.Hash.Bytes;
                return true;
            }

            if ((string.IsNullOrWhiteSpace(file.Hash.Algorithm) ||
                 file.Hash.Algorithm.Contains("sha512", StringComparison.OrdinalIgnoreCase)) &&
                UpdateHash.TryParseHashBytes(file.Hash.Value, out expected))
            {
                return true;
            }
        }

        if (UpdateHash.TryParseHashBytes(file.Sha512, out expected))
        {
            return true;
        }

        return UpdateHash.TryParseHashBytes(file.Sha512Base64, out expected);
    }

    public static bool TryGetExpectedObjectSha512(PlondsFileEntry file, out byte[] expected)
    {
        expected = [];
        if (file.Hash is null)
        {
            return false;
        }

        if (file.Hash.Bytes is { Length: > 0 })
        {
            expected = file.Hash.Bytes;
            return true;
        }

        if (!string.IsNullOrWhiteSpace(file.Hash.Algorithm) &&
            !file.Hash.Algorithm.Contains("sha512", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return UpdateHash.TryParseHashBytes(file.Hash.Value, out expected);
    }

    private static void ParseComponentsNode(JsonElement componentsNode, ICollection<PlondsFileEntry> files)
    {
        if (componentsNode.ValueKind == JsonValueKind.Object)
        {
            foreach (var component in componentsNode.EnumerateObject())
            {
                if (component.Value.ValueKind == JsonValueKind.Object &&
                    TryGetPropertyIgnoreCase(component.Value, "files", out var componentFilesNode))
                {
                    ParseFilesNode(componentFilesNode, component.Name, files);
                }
            }

            return;
        }

        if (componentsNode.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var component in componentsNode.EnumerateArray())
        {
            if (component.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var componentName = ReadStringIgnoreCase(component, "name");
            if (TryGetPropertyIgnoreCase(component, "files", out var componentFilesNode))
            {
                ParseFilesNode(componentFilesNode, componentName, files);
            }
        }
    }

    private static void ParseFilesNode(JsonElement filesNode, string? componentName, ICollection<PlondsFileEntry> files)
    {
        if (filesNode.ValueKind == JsonValueKind.Object)
        {
            foreach (var fileEntry in filesNode.EnumerateObject())
            {
                if (fileEntry.Value.ValueKind == JsonValueKind.Object &&
                    TryCreateFileEntry(fileEntry.Name, componentName, fileEntry.Value, out var parsed))
                {
                    files.Add(parsed);
                }
            }

            return;
        }

        if (filesNode.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var fileEntry in filesNode.EnumerateArray())
        {
            if (fileEntry.ValueKind == JsonValueKind.Object &&
                TryCreateFileEntry(ReadStringIgnoreCase(fileEntry, "path"), componentName, fileEntry, out var parsed))
            {
                files.Add(parsed);
            }
        }
    }

    private static bool TryCreateFileEntry(string? fallbackPath, string? componentName, JsonElement node, out PlondsFileEntry entry)
    {
        entry = new PlondsFileEntry();
        var path = ReadStringIgnoreCase(node, "path");
        if (string.IsNullOrWhiteSpace(path))
        {
            path = fallbackPath;
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var archiveSha512 = ReadByteArrayIgnoreCase(node, "archivesha512");
        var archiveSha512Text = ReadStringIgnoreCase(node, "archivesha512");
        entry = new PlondsFileEntry
        {
            Path = path,
            Action = FirstNonEmpty(ReadStringIgnoreCase(node, "action"), "replace"),
            Url = ReadStringIgnoreCase(node, "archivedownloadurl") ?? ReadStringIgnoreCase(node, "downloadurl") ?? ReadStringIgnoreCase(node, "url"),
            ObjectUrl = ReadStringIgnoreCase(node, "objecturl"),
            ObjectPath = ReadStringIgnoreCase(node, "objectpath") ?? ReadStringIgnoreCase(node, "archivepath"),
            ObjectKey = ReadStringIgnoreCase(node, "objectkey"),
            ArchivePath = ReadStringIgnoreCase(node, "archivepath"),
            Sha256 = ReadStringIgnoreCase(node, "sha256") ?? ReadStringIgnoreCase(node, "filesha256"),
            Sha512 = ReadStringIgnoreCase(node, "filesha512") ?? ReadStringIgnoreCase(node, "sha512"),
            Sha512Bytes = ReadByteArrayIgnoreCase(node, "filesha512") ?? ReadByteArrayIgnoreCase(node, "sha512"),
            Metadata = BuildMetadata(node, componentName)
        };

        if (archiveSha512 is { Length: > 0 } || !string.IsNullOrWhiteSpace(archiveSha512Text))
        {
            entry.Hash = new PlondsHashDescriptor
            {
                Algorithm = "sha512",
                Bytes = archiveSha512,
                Value = archiveSha512Text ?? (archiveSha512 is { Length: > 0 }
                    ? Convert.ToHexString(archiveSha512).ToLowerInvariant()
                    : null)
            };
        }
        else if (TryGetPropertyIgnoreCase(node, "hash", out var hashNode) && hashNode.ValueKind == JsonValueKind.Object)
        {
            entry.Hash = new PlondsHashDescriptor
            {
                Algorithm = ReadStringIgnoreCase(hashNode, "algorithm"),
                Value = ReadStringIgnoreCase(hashNode, "value"),
                Bytes = ReadByteArrayIgnoreCase(hashNode, "bytes")
            };
        }

        return true;
    }

    private static Dictionary<string, string> BuildMetadata(JsonElement node, string? componentName)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(componentName))
        {
            metadata["component"] = componentName;
        }

        PopulateMetadata(node, metadata);
        return metadata;
    }

    private static void PopulateMetadata(JsonElement node, Dictionary<string, string> metadata)
    {
        if (!TryGetPropertyIgnoreCase(node, "metadata", out var metadataNode) ||
            metadataNode.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var property in metadataNode.EnumerateObject())
        {
            if (property.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                continue;
            }

            var value = property.Value.ValueKind == JsonValueKind.String
                ? property.Value.GetString()
                : property.Value.ToString();
            if (!string.IsNullOrWhiteSpace(value))
            {
                metadata[property.Name] = value;
            }
        }
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement node, string propertyName, out JsonElement value)
    {
        if (node.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in node.EnumerateObject())
            {
                if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    private static string? ReadStringIgnoreCase(JsonElement node, string propertyName)
    {
        if (!TryGetPropertyIgnoreCase(node, propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
                ? null
                : value.ToString();
    }

    private static byte[]? ReadByteArrayIgnoreCase(JsonElement node, string propertyName)
    {
        return TryGetPropertyIgnoreCase(node, propertyName, out var value)
            ? ParseByteArrayValue(value)
            : null;
    }

    private static byte[]? ParseByteArrayValue(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.String)
        {
            return UpdateHash.TryParseHashBytes(value.GetString(), out var parsed) ? parsed : null;
        }

        if (value.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var bytes = new byte[value.GetArrayLength()];
        var index = 0;
        foreach (var element in value.EnumerateArray())
        {
            if (!element.TryGetInt32(out var number) || number < byte.MinValue || number > byte.MaxValue)
            {
                return null;
            }

            bytes[index++] = (byte)number;
        }

        return bytes;
    }

    private static string? TryGetMetadataValue(Dictionary<string, string>? metadata, string key)
    {
        if (metadata is null || metadata.Count == 0)
        {
            return null;
        }

        foreach (var pair in metadata)
        {
            if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(pair.Value))
            {
                return pair.Value;
            }
        }

        return null;
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }
}
