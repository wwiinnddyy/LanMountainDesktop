using System.Text.Json;

namespace LanMountainDesktop.AirAppSdk;

public sealed record AirAppManifest(
    string Id,
    string Name,
    string EntranceAssembly,
    string? Description = null,
    string? Author = null,
    string? Version = null,
    string? ApiVersion = null,
    IReadOnlyList<AirAppSharedContractReference>? SharedContracts = null,
    AirAppRuntimeConfiguration? Runtime = null,
    IReadOnlyList<AirAppComponentManifest>? Components = null,
    IReadOnlyList<AirAppWindowManifest>? Windows = null,
    IReadOnlyList<string>? Permissions = null)
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static AirAppManifest Load(string manifestPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestPath);

        using var stream = File.OpenRead(manifestPath);
        return Load(stream, manifestPath);
    }

    public static AirAppManifest Load(Stream stream, string sourceName)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);

        var manifest = JsonSerializer.Deserialize<AirAppManifest>(stream, SerializerOptions);
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

    public AirAppRuntimeMode RuntimeMode =>
        AirAppRuntimeModes.TryParse(Runtime?.Mode, out var mode) ? mode : AirAppRuntimeMode.InProcess;

    private AirAppManifest NormalizeAndValidate(string manifestPath)
    {
        var normalizedSharedContracts = NormalizeSharedContracts(manifestPath, SharedContracts);
        var normalizedRuntime = (Runtime ?? new AirAppRuntimeConfiguration()).NormalizeAndValidate(manifestPath);
        var normalized = this with
        {
            Id = RequireValue(Id, nameof(Id), manifestPath),
            Name = RequireValue(Name, nameof(Name), manifestPath),
            EntranceAssembly = RequireValue(EntranceAssembly, nameof(EntranceAssembly), manifestPath),
            Description = NormalizeOptionalValue(Description),
            Author = NormalizeOptionalValue(Author),
            Version = NormalizeOptionalValue(Version),
            ApiVersion = NormalizeOptionalValue(ApiVersion) ?? AirAppSdkInfo.ApiVersion,
            SharedContracts = normalizedSharedContracts,
            Runtime = normalizedRuntime,
            Components = Components ?? Array.Empty<AirAppComponentManifest>(),
            Windows = Windows ?? Array.Empty<AirAppWindowManifest>(),
            Permissions = Permissions ?? Array.Empty<string>()
        };

        if (!System.Version.TryParse(normalized.ApiVersion, out var requestedVersion))
        {
            throw new InvalidOperationException(
                $"AirApp manifest '{manifestPath}' declares an invalid API version '{normalized.ApiVersion}'.");
        }

        if (!System.Version.TryParse(AirAppSdkInfo.ApiVersion, out var currentVersion))
        {
            throw new InvalidOperationException($"AirApp SDK API version '{AirAppSdkInfo.ApiVersion}' is invalid.");
        }

        if (requestedVersion.Major != currentVersion.Major)
        {
            throw new InvalidOperationException(
                $"AirApp '{normalized.Id}' targets API version '{normalized.ApiVersion}', " +
                $"but the host provides '{AirAppSdkInfo.ApiVersion}'. " +
                $"This host only supports API {AirAppSdkInfo.ApiVersion} plugins.");
        }

        return normalized;
    }

    private static IReadOnlyList<AirAppSharedContractReference> NormalizeSharedContracts(
        string manifestPath,
        IReadOnlyList<AirAppSharedContractReference>? sharedContracts)
    {
        if (sharedContracts is null || sharedContracts.Count == 0)
        {
            return Array.Empty<AirAppSharedContractReference>();
        }

        var normalized = new List<AirAppSharedContractReference>(sharedContracts.Count);
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var contract in sharedContracts)
        {
            if (contract is null)
            {
                throw new InvalidOperationException(
                    $"AirApp manifest '{manifestPath}' contains a null shared contract declaration.");
            }

            var normalizedContract = contract.NormalizeAndValidate(manifestPath);
            var contractKey = $"{normalizedContract.Id}@{normalizedContract.Version}";
            if (!seenIds.Add(contractKey))
            {
                throw new InvalidOperationException(
                    $"AirApp manifest '{manifestPath}' declares duplicate shared contract '{contractKey}'.");
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
                $"AirApp manifest '{manifestPath}' is missing required property '{propertyName}'.");
        }

        return normalized;
    }

    private static string? NormalizeOptionalValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}

/// <summary>
/// Component declaration in the AirApp manifest.
/// </summary>
public sealed record AirAppComponentManifest(
    string Id,
    string Name,
    int DefaultWidth = 2,
    int DefaultHeight = 2,
    string? Description = null,
    string? Category = null,
    string? IconKey = null);

/// <summary>
/// Window declaration in the AirApp manifest.
/// </summary>
public sealed record AirAppWindowManifest(
    string Id,
    string Name,
    double DefaultWidth = 800,
    double DefaultHeight = 600,
    string? Description = null);
