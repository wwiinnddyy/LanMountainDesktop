using System.Text.Json;

namespace LanMountainDesktop.AirAppSdk;

/// <summary>
/// AirApp manifest (airapp.json).
/// </summary>
public sealed record AirAppManifest(
    string Id,
    string Name,
    string EntranceAssembly,
    string? Description = null,
    string? Author = null,
    string? Version = null,
    string? ApiVersion = null,
    AirAppRuntimeConfiguration? Runtime = null,
    IReadOnlyList<AirAppComponentManifest>? Components = null,
    IReadOnlyList<AirAppWindowManifest>? Windows = null,
    IReadOnlyList<string>? Permissions = null,
    IReadOnlyList<AirAppSharedContractReference>? SharedContracts = null)
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    /// <summary>
    /// Load manifest from file.
    /// </summary>
    public static AirAppManifest Load(string manifestPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestPath);

        using var stream = File.OpenRead(manifestPath);
        return Load(stream, manifestPath);
    }

    /// <summary>
    /// Load manifest from stream.
    /// </summary>
    public static AirAppManifest Load(Stream stream, string sourceName)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);

        var manifest = JsonSerializer.Deserialize<AirAppManifest>(stream, SerializerOptions);
        if (manifest is null)
        {
            throw new InvalidOperationException($"Failed to deserialize AirApp manifest '{sourceName}'.");
        }

        return manifest.NormalizeAndValidate(sourceName);
    }

    /// <summary>
    /// Resolve entrance assembly path.
    /// </summary>
    public string ResolveEntranceAssemblyPath(string manifestPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestPath);

        if (Path.IsPathRooted(EntranceAssembly))
        {
            return Path.GetFullPath(EntranceAssembly);
        }

        var manifestDirectory = Path.GetDirectoryName(Path.GetFullPath(manifestPath))
            ?? throw new InvalidOperationException($"Failed to determine directory of '{manifestPath}'.");

        return Path.GetFullPath(Path.Combine(manifestDirectory, EntranceAssembly));
    }

    /// <summary>
    /// Get runtime mode.
    /// </summary>
    public AirAppRuntimeMode RuntimeMode =>
        AirAppRuntimeModes.TryParse(Runtime?.Mode, out var mode) ? mode : AirAppRuntimeMode.InProcess;

    private AirAppManifest NormalizeAndValidate(string manifestPath)
    {
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
            Runtime = normalizedRuntime,
            Components = Components ?? Array.Empty<AirAppComponentManifest>(),
            Windows = Windows ?? Array.Empty<AirAppWindowManifest>(),
            Permissions = Permissions ?? Array.Empty<string>(),
            SharedContracts = SharedContracts ?? Array.Empty<AirAppSharedContractReference>()
        };

        // Validate API version
        if (!System.Version.TryParse(normalized.ApiVersion, out var requestedVersion))
        {
            throw new InvalidOperationException(
                $"AirApp manifest '{manifestPath}' declares invalid API version '{normalized.ApiVersion}'.");
        }

        if (!System.Version.TryParse(AirAppSdkInfo.ApiVersion, out var currentVersion))
        {
            throw new InvalidOperationException($"AirApp SDK API version '{AirAppSdkInfo.ApiVersion}' is invalid.");
        }

        if (requestedVersion.Major != currentVersion.Major)
        {
            throw new InvalidOperationException(
                $"AirApp '{normalized.Id}' targets API version '{normalized.ApiVersion}' (major {requestedVersion.Major}), " +
                $"but the host provides '{AirAppSdkInfo.ApiVersion}' (major {currentVersion.Major}). " +
                $"This host only supports v{currentVersion.Major}.x AirApps and rejects v{requestedVersion.Major}.x packages. " +
                $"Migrate the AirApp manifest and code to API {AirAppSdkInfo.ApiVersion}, then rebuild and republish.");
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
/// Component declaration in manifest.
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
/// Window declaration in manifest.
/// </summary>
public sealed record AirAppWindowManifest(
    string Id,
    string Name,
    double DefaultWidth = 800,
    double DefaultHeight = 600,
    string? Description = null);

/// <summary>
/// Shared contract reference.
/// </summary>
public sealed record AirAppSharedContractReference(
    string Id,
    string Version);

/// <summary>
/// Runtime configuration.
/// </summary>
public sealed record AirAppRuntimeConfiguration
{
    public string? Mode { get; init; }
    public IReadOnlyList<string>? Capabilities { get; init; }

    internal AirAppRuntimeConfiguration NormalizeAndValidate(string manifestPath)
    {
        return this with
        {
            Mode = string.IsNullOrWhiteSpace(Mode) ? "in-process" : Mode.Trim().ToLowerInvariant(),
            Capabilities = Capabilities ?? Array.Empty<string>()
        };
    }
}
