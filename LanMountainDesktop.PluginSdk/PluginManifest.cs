using System.Text.Json;

namespace LanMountainDesktop.PluginSdk;

public sealed record PluginManifest(
    string Id,
    string Name,
    string EntranceAssembly,
    string? Description = null,
    string? Author = null,
    string? Version = null,
    string? ApiVersion = null,
    IReadOnlyList<PluginSharedContractReference>? SharedContracts = null)
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static PluginManifest Load(string manifestPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestPath);

        using var stream = File.OpenRead(manifestPath);
        return Load(stream, manifestPath);
    }

    public static PluginManifest Load(Stream stream, string sourceName)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);

        var manifest = JsonSerializer.Deserialize<PluginManifest>(stream, SerializerOptions);
        if (manifest is null)
        {
            throw new InvalidOperationException($"Failed to deserialize plugin manifest '{sourceName}'.");
        }

        return manifest.NormalizeAndValidate(sourceName);
    }

    public string ResolveEntranceAssemblyPath(string manifestPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestPath);

        if (Path.IsPathRooted(EntranceAssembly))
        {
            return Path.GetFullPath(EntranceAssembly);
        }

        var manifestDirectory = Path.GetDirectoryName(Path.GetFullPath(manifestPath))
            ?? throw new InvalidOperationException($"Failed to determine the directory of '{manifestPath}'.");

        return Path.GetFullPath(Path.Combine(manifestDirectory, EntranceAssembly));
    }

    private PluginManifest NormalizeAndValidate(string manifestPath)
    {
        var normalizedSharedContracts = NormalizeSharedContracts(manifestPath, SharedContracts);
        var normalized = this with
        {
            Id = RequireValue(Id, nameof(Id), manifestPath),
            Name = RequireValue(Name, nameof(Name), manifestPath),
            EntranceAssembly = RequireValue(EntranceAssembly, nameof(EntranceAssembly), manifestPath),
            Description = NormalizeOptionalValue(Description),
            Author = NormalizeOptionalValue(Author),
            Version = NormalizeOptionalValue(Version),
            ApiVersion = NormalizeOptionalValue(ApiVersion) ?? PluginSdkInfo.ApiVersion,
            SharedContracts = normalizedSharedContracts
        };

        if (!System.Version.TryParse(normalized.ApiVersion, out var requestedVersion))
        {
            throw new InvalidOperationException(
                $"Plugin manifest '{manifestPath}' declares an invalid API version '{normalized.ApiVersion}'.");
        }

        if (!System.Version.TryParse(PluginSdkInfo.ApiVersion, out var currentVersion))
        {
            throw new InvalidOperationException($"Plugin SDK API version '{PluginSdkInfo.ApiVersion}' is invalid.");
        }

        if (requestedVersion.Major != currentVersion.Major)
        {
            throw new InvalidOperationException(
                $"Plugin '{normalized.Id}' targets API version '{normalized.ApiVersion}', but the host provides '{PluginSdkInfo.ApiVersion}'. Upgrade the plugin to API {PluginSdkInfo.ApiVersion}.");
        }

        return normalized;
    }

    private static IReadOnlyList<PluginSharedContractReference> NormalizeSharedContracts(
        string manifestPath,
        IReadOnlyList<PluginSharedContractReference>? sharedContracts)
    {
        if (sharedContracts is null || sharedContracts.Count == 0)
        {
            return Array.Empty<PluginSharedContractReference>();
        }

        var normalized = new List<PluginSharedContractReference>(sharedContracts.Count);
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var contract in sharedContracts)
        {
            if (contract is null)
            {
                throw new InvalidOperationException(
                    $"Plugin manifest '{manifestPath}' contains a null shared contract declaration.");
            }

            var normalizedContract = contract.NormalizeAndValidate(manifestPath);
            var contractKey = $"{normalizedContract.Id}@{normalizedContract.Version}";
            if (!seenIds.Add(contractKey))
            {
                throw new InvalidOperationException(
                    $"Plugin manifest '{manifestPath}' declares duplicate shared contract '{contractKey}'.");
            }

            normalized.Add(normalizedContract);
        }

        return normalized;
    }

    private static string RequireValue(string? value, string propertyName, string manifestPath)
    {
        var normalized = NormalizeOptionalValue(value);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new InvalidOperationException(
                $"Plugin manifest '{manifestPath}' is missing required property '{propertyName}'.");
        }

        return normalized;
    }

    private static string? NormalizeOptionalValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
