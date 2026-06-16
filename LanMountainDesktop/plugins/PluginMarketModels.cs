using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using LanMountainDesktop.PluginSdk;
using static LanMountainDesktop.Services.PluginMarket.AirAppMarketDefaults;
using static LanMountainDesktop.Services.PluginMarket.AirAppMarketIndexDocument;

namespace LanMountainDesktop.Services.PluginMarket;

/// <summary>
/// Market index schema version. The host only understands this single self-contained flat format.
/// </summary>
internal static class AirAppMarketSchema
{
    public const string Version = "3.0.0";
}

internal static class AirAppMarketDefaults
{
    public const string DefaultIndexUrl =
        "https://raw.githubusercontent.com/wwiinnddyy/LanAirApp/main/airappmarket/index.json";

    public static string BuildGitHubRawUrl(
        string owner,
        string repositoryName,
        string branch,
        string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryName);
        ArgumentException.ThrowIfNullOrWhiteSpace(branch);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"https://raw.githubusercontent.com/{owner.Trim()}/{repositoryName.Trim()}/{branch.Trim().TrimStart('/')}/{relativePath.Trim().TrimStart('/').Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/')}");
    }

    public static string BuildGitHubReleaseDownloadUrl(
        string owner,
        string repositoryName,
        string releaseTag,
        string assetName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryName);
        ArgumentException.ThrowIfNullOrWhiteSpace(releaseTag);
        ArgumentException.ThrowIfNullOrWhiteSpace(assetName);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"https://github.com/{owner.Trim()}/{repositoryName.Trim()}/releases/download/{Uri.EscapeDataString(releaseTag.Trim())}/{Uri.EscapeDataString(assetName.Trim())}");
    }

    public static string? TryGetWorkspaceIndexPath()
    {
        var relativePath = Path.Combine("airappmarket", "index.json");
        return TryResolveWorkspacePath("LanAirApp", relativePath);
    }

    public static bool TryResolveWorkspaceFile(string url, out string localPath)
    {
        localPath = string.Empty;

        if (File.Exists(url))
        {
            localPath = Path.GetFullPath(url);
            return true;
        }

        if (Uri.TryCreate(url, UriKind.Absolute, out var fileUri) &&
            fileUri.IsFile)
        {
            var filePath = fileUri.LocalPath;
            if (File.Exists(filePath))
            {
                localPath = Path.GetFullPath(filePath);
                return true;
            }
        }

        string repositoryName;
        string relativePath;

        if (TryParseWorkspaceUrl(url, out repositoryName, out relativePath))
        {
            // Already parsed from workspace://{repository}/{relativePath}.
        }
        else if (TryParseGitHubReleaseDownloadUrl(url, out repositoryName, out var releaseAssetName))
        {
            relativePath = releaseAssetName;
        }
        else if (!TryParseRawGitHubUrl(url, out repositoryName, out relativePath))
        {
            return false;
        }

        var candidatePath = TryResolveWorkspacePath(repositoryName, relativePath);
        if (candidatePath is null)
        {
            return false;
        }

        localPath = candidatePath;
        return true;
    }

    public static bool TryParseGitHubRepositoryUrl(
        string? url,
        out string owner,
        out string repositoryName)
    {
        owner = string.Empty;
        repositoryName = string.Empty;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var segments = uri.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length != 2)
        {
            return false;
        }

        owner = segments[0];
        repositoryName = segments[1];
        return !string.IsNullOrWhiteSpace(owner) && !string.IsNullOrWhiteSpace(repositoryName);
    }

    private static string? TryResolveWorkspacePath(string repositoryName, string relativePath)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && current.Exists)
        {
            var solutionPath = Path.Combine(current.FullName, "LanMountainDesktop.slnx");
            if (File.Exists(solutionPath))
            {
                var workspaceRoot = current.Parent;
                if (workspaceRoot is null)
                {
                    return null;
                }

                var candidateRepositoryPath = Path.Combine(workspaceRoot.FullName, repositoryName);
                if (!Directory.Exists(candidateRepositoryPath))
                {
                    return null;
                }

                var candidatePath = Path.GetFullPath(Path.Combine(candidateRepositoryPath, relativePath));
                if (File.Exists(candidatePath))
                {
                    return candidatePath;
                }

                return null;
            }

            current = current.Parent;
        }

        return null;
    }

    private static bool TryParseRawGitHubUrl(
        string url,
        out string repositoryName,
        out string relativePath)
    {
        repositoryName = string.Empty;
        relativePath = string.Empty;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Host, "raw.githubusercontent.com", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var segments = uri.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length < 4)
        {
            return false;
        }

        repositoryName = segments[1];
        relativePath = Path.Combine(segments[3..]).Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        return !string.IsNullOrWhiteSpace(repositoryName) && !string.IsNullOrWhiteSpace(relativePath);
    }

    private static bool TryParseWorkspaceUrl(
        string url,
        out string repositoryName,
        out string relativePath)
    {
        repositoryName = string.Empty;
        relativePath = string.Empty;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, "workspace", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        repositoryName = uri.Host;
        var path = Uri.UnescapeDataString(uri.AbsolutePath).TrimStart('/');
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        relativePath = path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        return !string.IsNullOrWhiteSpace(repositoryName) && !string.IsNullOrWhiteSpace(relativePath);
    }

    public static bool TryParsePackageSourceKind(string? value, out PluginPackageSourceKind kind)
    {
        kind = PluginPackageSourceKind.ReleaseAsset;
        var normalized = NormalizeValue(value);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        switch (normalized)
        {
            case "releaseAsset":
                kind = PluginPackageSourceKind.ReleaseAsset;
                return true;
            case "rawFallback":
                kind = PluginPackageSourceKind.RawFallback;
                return true;
            case "workspaceLocal":
                kind = PluginPackageSourceKind.WorkspaceLocal;
                return true;
            default:
                return false;
        }
    }

    public static int GetPackageSourceOrder(PluginPackageSourceKind kind)
    {
        return kind switch
        {
            PluginPackageSourceKind.ReleaseAsset => 0,
            PluginPackageSourceKind.RawFallback => 1,
            PluginPackageSourceKind.WorkspaceLocal => 2,
            _ => int.MaxValue
        };
    }

    private static bool TryParseGitHubReleaseDownloadUrl(
        string url,
        out string repositoryName,
        out string assetName)
    {
        repositoryName = string.Empty;
        assetName = string.Empty;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var segments = uri.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length != 6 ||
            !string.Equals(segments[2], "releases", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(segments[3], "download", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        repositoryName = segments[1];
        assetName = Uri.UnescapeDataString(segments[5]);
        return !string.IsNullOrWhiteSpace(repositoryName) && !string.IsNullOrWhiteSpace(assetName);
    }

    internal static string? NormalizeValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}

internal enum AirAppMarketLoadSource
{
    Local = 0,
    Network = 1,
    Cache = 2
}

internal sealed record AirAppMarketLoadResult(
    bool Success,
    AirAppMarketIndexDocument? Document,
    AirAppMarketLoadSource? Source,
    string? SourceLocation,
    string? WarningMessage,
    string? ErrorMessage);

internal sealed record AirAppMarketInstallResult(
    bool Success,
    PluginManifest? Manifest,
    string? ErrorMessage,
    bool RestartRequired = false);

/// <summary>
/// The market index document. Self-contained flat schema (schemaVersion 3.0.0).
/// Every plugin entry carries all of its display and acquisition metadata inline;
/// the host never needs to call back to GitHub to enrich entries.
/// </summary>
internal sealed class AirAppMarketIndexDocument
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public string SchemaVersion { get; init; } = string.Empty;

    public string SourceId { get; init; } = string.Empty;

    public string SourceName { get; init; } = string.Empty;

    public DateTimeOffset GeneratedAt { get; init; }

    public List<AirAppMarketSharedContractEntry> Contracts { get; init; } = [];

    public List<AirAppMarketPluginEntry> Plugins { get; init; } = [];

    public static AirAppMarketIndexDocument Load(string json, string sourceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);

        var document = JsonSerializer.Deserialize<AirAppMarketIndexDocument>(
            json.TrimStart('\uFEFF'),
            SerializerOptions);

        if (document is null)
        {
            throw new InvalidOperationException($"Failed to parse market index '{sourceName}'.");
        }

        return document.ValidateAndNormalize(sourceName);
    }

    private AirAppMarketIndexDocument ValidateAndNormalize(string sourceName)
    {
        var schemaVersion = RequireValue(SchemaVersion, nameof(SchemaVersion), sourceName);
        if (!string.Equals(schemaVersion, AirAppMarketSchema.Version, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Market index '{sourceName}' uses schemaVersion '{schemaVersion}', but the host only supports '{AirAppMarketSchema.Version}'.");
        }

        var normalizedContracts = new List<AirAppMarketSharedContractEntry>((Contracts ?? []).Count);
        var seenContracts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var contract in Contracts ?? [])
        {
            var normalizedContract = contract.ValidateAndNormalize(sourceName);
            var contractKey = $"{normalizedContract.Id}@{normalizedContract.Version}";
            if (!seenContracts.Add(contractKey))
            {
                throw new InvalidOperationException(
                    $"Market index '{sourceName}' contains duplicate shared contract '{contractKey}'.");
            }

            normalizedContracts.Add(normalizedContract);
        }

        var normalizedPlugins = new List<AirAppMarketPluginEntry>((Plugins ?? []).Count);
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var plugin in Plugins ?? [])
        {
            var normalizedPlugin = plugin.ValidateAndNormalize(sourceName);
            if (!seenIds.Add(normalizedPlugin.Id))
            {
                throw new InvalidOperationException(
                    $"Market index '{sourceName}' contains duplicate plugin id '{normalizedPlugin.Id}'.");
            }

            normalizedPlugins.Add(normalizedPlugin);
        }

        return new AirAppMarketIndexDocument
        {
            SchemaVersion = AirAppMarketSchema.Version,
            SourceId = RequireValue(SourceId, nameof(SourceId), sourceName),
            SourceName = RequireValue(SourceName, nameof(SourceName), sourceName),
            GeneratedAt = GeneratedAt == default
                ? throw new InvalidOperationException($"Market index '{sourceName}' is missing a valid generatedAt timestamp.")
                : GeneratedAt,
            Contracts = normalizedContracts
                .OrderBy(contract => contract.Id, StringComparer.OrdinalIgnoreCase)
                .ThenBy(contract => contract.Version, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            Plugins = normalizedPlugins
                .OrderBy(plugin => plugin.Name, StringComparer.OrdinalIgnoreCase)
                .ToList()
        };
    }

    internal static string RequireValue(string? value, string propertyName, string sourceName)
    {
        var normalized = NormalizeValue(value);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new InvalidOperationException($"Market index '{sourceName}' is missing required property '{propertyName}'.");
        }

        return normalized;
    }

    internal static string NormalizeVersion(string? value, string propertyName, string sourceName)
    {
        var normalized = RequireValue(value, propertyName, sourceName);
        if (!TryParseVersion(normalized, out _))
        {
            throw new InvalidOperationException(
                $"Market index '{sourceName}' declares invalid version '{normalized}' for '{propertyName}'.");
        }

        return normalized;
    }

    internal static string NormalizeReleaseTag(string? value, string propertyName, string sourceName)
    {
        var normalized = RequireValue(value, propertyName, sourceName);
        if (!normalized.StartsWith("v", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Market index '{sourceName}' declares invalid release tag '{normalized}' for '{propertyName}'. Expected format 'v1.2.3'.");
        }

        if (!TryParseVersion(normalized[1..], out _))
        {
            throw new InvalidOperationException(
                $"Market index '{sourceName}' declares invalid release tag '{normalized}' for '{propertyName}'.");
        }

        return normalized;
    }

    internal static void EnsureUrl(string url, string propertyName, string sourceName)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException(
                $"Market index '{sourceName}' declares invalid URL '{url}' for '{propertyName}'.");
        }
    }

    internal static string NormalizeGitHubRepositoryUrl(
        string url,
        string propertyName,
        string sourceName)
    {
        EnsureUrl(url, propertyName, sourceName);

        if (!AirAppMarketDefaults.TryParseGitHubRepositoryUrl(url, out var owner, out var repositoryName))
        {
            throw new InvalidOperationException(
                $"Market index '{sourceName}' declares invalid GitHub repository url '{url}' for '{propertyName}'.");
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"https://github.com/{owner}/{repositoryName}");
    }

    internal static bool TryParseVersion(string? value, out Version? version)
    {
        version = null;
        var normalized = NormalizeValue(value);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        if (normalized.StartsWith("v", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[1..];
        }

        var separatorIndex = normalized.IndexOfAny(['-', '+', ' ']);
        if (separatorIndex > 0)
        {
            normalized = normalized[..separatorIndex];
        }

        if (!Version.TryParse(normalized, out var parsed))
        {
            return false;
        }

        var major = Math.Max(0, parsed.Major);
        var minor = Math.Max(0, parsed.Minor);
        var build = Math.Max(0, parsed.Build >= 0 ? parsed.Build : 0);
        var revision = Math.Max(0, parsed.Revision >= 0 ? parsed.Revision : 0);
        version = revision > 0
            ? new Version(major, minor, build, revision)
            : new Version(major, minor, build);
        return true;
    }
}

internal sealed class AirAppMarketSharedContractEntry
{
    public string Id { get; init; } = string.Empty;

    public string Version { get; init; } = string.Empty;

    public string AssemblyName { get; init; } = string.Empty;

    public string DownloadUrl { get; init; } = string.Empty;

    public string Sha256 { get; init; } = string.Empty;

    public long PackageSizeBytes { get; init; }

    public AirAppMarketSharedContractEntry ValidateAndNormalize(string sourceName)
    {
        var normalizedSha = NormalizeValue(Sha256)?.ToLowerInvariant()
            ?? throw new InvalidOperationException(
                $"Market index '{sourceName}' is missing required property '{nameof(Sha256)}' for a shared contract.");
        if (normalizedSha.Length != 64 || normalizedSha.Any(ch => !Uri.IsHexDigit(ch)))
        {
            throw new InvalidOperationException(
                $"Market index '{sourceName}' declares invalid SHA-256 '{normalizedSha}' for shared contract '{Id}'.");
        }

        var normalizedDownloadUrl = NormalizeValue(DownloadUrl)
            ?? throw new InvalidOperationException(
                $"Market index '{sourceName}' is missing required property '{nameof(DownloadUrl)}' for shared contract '{Id}'.");
        EnsureUrl(normalizedDownloadUrl, nameof(DownloadUrl), sourceName);

        if (PackageSizeBytes <= 0)
        {
            throw new InvalidOperationException(
                $"Market index '{sourceName}' declares invalid packageSizeBytes '{PackageSizeBytes}' for shared contract '{Id}'.");
        }

        return new AirAppMarketSharedContractEntry
        {
            Id = NormalizeValue(Id)
                ?? throw new InvalidOperationException($"Market index '{sourceName}' is missing a shared contract id."),
            Version = NormalizeVersion(Version, nameof(Version), sourceName),
            AssemblyName = NormalizeValue(AssemblyName)
                ?? throw new InvalidOperationException($"Market index '{sourceName}' is missing assemblyName for shared contract '{Id}'."),
            DownloadUrl = normalizedDownloadUrl,
            Sha256 = normalizedSha,
            PackageSizeBytes = PackageSizeBytes
        };
    }
}

internal sealed class AirAppMarketPluginDependencyEntry
{
    public string Id { get; init; } = string.Empty;

    public string Version { get; init; } = string.Empty;

    public string AssemblyName { get; init; } = string.Empty;

    public AirAppMarketPluginDependencyEntry ValidateAndNormalize(string sourceName)
    {
        return new AirAppMarketPluginDependencyEntry
        {
            Id = NormalizeValue(Id)
                ?? throw new InvalidOperationException(
                    $"Market index '{sourceName}' is missing dependency id for a plugin entry."),
            Version = NormalizeVersion(Version, nameof(Version), sourceName),
            AssemblyName = NormalizeValue(AssemblyName)
                ?? throw new InvalidOperationException(
                    $"Market index '{sourceName}' is missing assemblyName for dependency '{Id}'.")
        };
    }
}

internal sealed class AirAppMarketPluginPackageSourceEntry
{
    public string Kind { get; init; } = string.Empty;

    public string Url { get; init; } = string.Empty;

    public PluginPackageSourceKind SourceKind { get; init; } = PluginPackageSourceKind.ReleaseAsset;

    public AirAppMarketPluginPackageSourceEntry ValidateAndNormalize(string sourceName, string pluginId)
    {
        var normalizedKind = NormalizeValue(Kind)
            ?? throw new InvalidOperationException(
                $"Market index '{sourceName}' is missing package source kind for plugin '{pluginId}'.");
        if (!AirAppMarketDefaults.TryParsePackageSourceKind(normalizedKind, out var sourceKind))
        {
            throw new InvalidOperationException(
                $"Market index '{sourceName}' declares invalid package source kind '{normalizedKind}' for plugin '{pluginId}'.");
        }

        var normalizedUrl = NormalizeValue(Url)
            ?? throw new InvalidOperationException(
                $"Market index '{sourceName}' is missing package source url for plugin '{pluginId}'.");
        EnsurePackageSourceUrl(normalizedUrl, sourceName, pluginId);

        return new AirAppMarketPluginPackageSourceEntry
        {
            Kind = sourceKind switch
            {
                PluginPackageSourceKind.ReleaseAsset => "releaseAsset",
                PluginPackageSourceKind.RawFallback => "rawFallback",
                PluginPackageSourceKind.WorkspaceLocal => "workspaceLocal",
                _ => normalizedKind
            },
            Url = normalizedUrl,
            SourceKind = sourceKind
        };
    }

    internal static void EnsurePackageSourceUrl(string url, string sourceName, string pluginId)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException(
                $"Market index '{sourceName}' declares invalid package source url '{url}' for plugin '{pluginId}'.");
        }

        if (uri.IsFile ||
            uri.Scheme == Uri.UriSchemeHttp ||
            uri.Scheme == Uri.UriSchemeHttps ||
            string.Equals(uri.Scheme, "workspace", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        throw new InvalidOperationException(
            $"Market index '{sourceName}' declares unsupported package source url scheme '{uri.Scheme}' for plugin '{pluginId}'.");
    }
}

/// <summary>
/// A single market plugin entry in the self-contained flat schema.
/// All display and acquisition metadata lives directly on this object; there are no
/// nested manifest/compatibility/repository/publication fallback objects.
/// </summary>
internal sealed class AirAppMarketPluginEntry
{
    public string PluginId { get; init; } = string.Empty;

    public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public string Author { get; init; } = string.Empty;

    public string Version { get; init; } = string.Empty;

    public string ApiVersion { get; init; } = string.Empty;

    public string MinHostVersion { get; init; } = string.Empty;

    public string EntranceAssembly { get; init; } = string.Empty;

    public string IconUrl { get; init; } = string.Empty;

    public string ReadmeUrl { get; init; } = string.Empty;

    public string ProjectUrl { get; init; } = string.Empty;

    public string HomepageUrl { get; init; } = string.Empty;

    public string RepositoryUrl { get; init; } = string.Empty;

    public string ReleaseTag { get; init; } = string.Empty;

    public string ReleaseAssetName { get; init; } = string.Empty;

    public string Sha256 { get; init; } = string.Empty;

    public string Md5 { get; init; } = string.Empty;

    public long PackageSizeBytes { get; init; }

    public DateTimeOffset PublishedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }

    public string ReleaseNotes { get; init; } = string.Empty;

    public List<string> Tags { get; init; } = [];

    public List<AirAppMarketPluginDependencyEntry> SharedContracts { get; init; } = [];

    public List<AirAppMarketPluginPackageSourceEntry> PackageSources { get; init; } = [];

    public List<string> DesktopComponents { get; init; } = [];

    public List<string> SettingsSections { get; init; } = [];

    public List<string> Exports { get; init; } = [];

    public List<string> MessageTypes { get; init; } = [];

    public string DownloadUrl => PackageSources
        .OrderBy(source => AirAppMarketDefaults.GetPackageSourceOrder(source.SourceKind))
        .FirstOrDefault()?.Url ?? string.Empty;

    public bool HasReleaseDownloadMetadata =>
        !string.IsNullOrWhiteSpace(ReleaseTag) &&
        !string.IsNullOrWhiteSpace(ReleaseAssetName);

    public AirAppMarketPluginEntry ValidateAndNormalize(string sourceName)
    {
        var resolvedId = NormalizeValue(PluginId) ?? NormalizeValue(Id)
            ?? throw new InvalidOperationException($"Market index '{sourceName}' is missing plugin id.");

        var resolvedName = NormalizeValue(Name)
            ?? throw new InvalidOperationException($"Market index '{sourceName}' is missing plugin name for '{resolvedId}'.");

        var resolvedDescription = NormalizeValue(Description)
            ?? throw new InvalidOperationException($"Market index '{sourceName}' is missing plugin description for '{resolvedId}'.");

        var resolvedAuthor = NormalizeValue(Author)
            ?? throw new InvalidOperationException($"Market index '{sourceName}' is missing plugin author for '{resolvedId}'.");

        var resolvedVersion = NormalizeVersion(Version, nameof(Version), sourceName);
        var resolvedApiVersion = NormalizeVersion(ApiVersion, nameof(ApiVersion), sourceName);
        var resolvedMinHostVersion = NormalizeValue(MinHostVersion) ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(resolvedMinHostVersion))
        {
            resolvedMinHostVersion = NormalizeVersion(resolvedMinHostVersion, nameof(MinHostVersion), sourceName);
        }

        var resolvedRepositoryUrl = NormalizeGitHubRepositoryUrl(
            NormalizeValue(RepositoryUrl)
            ?? throw new InvalidOperationException($"Market index '{sourceName}' is missing repositoryUrl for plugin '{resolvedId}'."),
            nameof(RepositoryUrl),
            sourceName);

        var resolvedIconUrl = NormalizeValue(IconUrl) ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(resolvedIconUrl))
        {
            EnsureUrl(resolvedIconUrl, nameof(IconUrl), sourceName);
        }

        var resolvedReadmeUrl = NormalizeValue(ReadmeUrl) ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(resolvedReadmeUrl))
        {
            EnsureUrl(resolvedReadmeUrl, nameof(ReadmeUrl), sourceName);
        }

        var resolvedProjectUrl = NormalizeValue(ProjectUrl) ?? string.Empty;
        var resolvedHomepageUrl = NormalizeValue(HomepageUrl) ?? string.Empty;

        var resolvedReleaseTag = NormalizeValue(ReleaseTag) ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(resolvedReleaseTag))
        {
            resolvedReleaseTag = NormalizeReleaseTag(resolvedReleaseTag, nameof(ReleaseTag), sourceName);
        }

        var resolvedReleaseAssetName = NormalizeValue(ReleaseAssetName) ?? string.Empty;

        var resolvedSha256 = NormalizeValue(Sha256)?.ToLowerInvariant() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(resolvedSha256) &&
            (resolvedSha256.Length != 64 || resolvedSha256.Any(ch => !Uri.IsHexDigit(ch))))
        {
            throw new InvalidOperationException(
                $"Market index '{sourceName}' declares invalid SHA-256 '{resolvedSha256}' for plugin '{resolvedId}'.");
        }

        var resolvedMd5 = NormalizeValue(Md5)?.ToLowerInvariant() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(resolvedMd5) &&
            (resolvedMd5.Length != 32 || resolvedMd5.Any(ch => !Uri.IsHexDigit(ch))))
        {
            throw new InvalidOperationException(
                $"Market index '{sourceName}' declares invalid MD5 '{resolvedMd5}' for plugin '{resolvedId}'.");
        }

        var normalizedPackageSources = NormalizePackageSources(PackageSources, sourceName, resolvedId);
        if (normalizedPackageSources.Count == 0)
        {
            throw new InvalidOperationException(
                $"Market index '{sourceName}' is missing package sources for plugin '{resolvedId}'.");
        }

        return new AirAppMarketPluginEntry
        {
            PluginId = resolvedId,
            Id = resolvedId,
            Name = resolvedName,
            Description = resolvedDescription,
            Author = resolvedAuthor,
            Version = resolvedVersion,
            ApiVersion = resolvedApiVersion,
            MinHostVersion = resolvedMinHostVersion,
            EntranceAssembly = NormalizeValue(EntranceAssembly) ?? string.Empty,
            IconUrl = resolvedIconUrl,
            ReadmeUrl = resolvedReadmeUrl,
            ProjectUrl = resolvedProjectUrl,
            HomepageUrl = resolvedHomepageUrl,
            RepositoryUrl = resolvedRepositoryUrl,
            ReleaseTag = resolvedReleaseTag,
            ReleaseAssetName = resolvedReleaseAssetName,
            Sha256 = resolvedSha256,
            Md5 = resolvedMd5,
            PackageSizeBytes = PackageSizeBytes,
            PublishedAt = PublishedAt,
            UpdatedAt = UpdatedAt,
            ReleaseNotes = NormalizeValue(ReleaseNotes) ?? string.Empty,
            Tags = NormalizeValues(Tags),
            SharedContracts = NormalizeDependencies(SharedContracts, sourceName, resolvedId),
            PackageSources = normalizedPackageSources,
            DesktopComponents = NormalizeValues(DesktopComponents),
            SettingsSections = NormalizeValues(SettingsSections),
            Exports = NormalizeValues(Exports),
            MessageTypes = NormalizeValues(MessageTypes)
        };
    }

    public string GetVersionSummary()
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            "v{0} | API {1} | Host >= {2}",
            string.IsNullOrWhiteSpace(Version) ? "?" : Version,
            string.IsNullOrWhiteSpace(ApiVersion) ? "?" : ApiVersion,
            string.IsNullOrWhiteSpace(MinHostVersion) ? "?" : MinHostVersion);
    }

    public IReadOnlyList<AirAppMarketPluginPackageSourceEntry> GetPackageSourcesInInstallOrder()
    {
        return PackageSources
            .OrderBy(source => AirAppMarketDefaults.GetPackageSourceOrder(source.SourceKind))
            .ToList();
    }

    private static List<AirAppMarketPluginPackageSourceEntry> NormalizePackageSources(
        IReadOnlyList<AirAppMarketPluginPackageSourceEntry>? packageSources,
        string sourceName,
        string pluginId)
    {
        var normalizedSources = new List<AirAppMarketPluginPackageSourceEntry>((packageSources ?? []).Count);
        var seenKinds = new HashSet<PluginPackageSourceKind>();
        var previousOrder = -1;
        foreach (var source in packageSources ?? [])
        {
            var normalizedSource = source.ValidateAndNormalize(sourceName, pluginId);
            var order = AirAppMarketDefaults.GetPackageSourceOrder(normalizedSource.SourceKind);
            if (order < previousOrder)
            {
                throw new InvalidOperationException(
                    $"Market index '{sourceName}' declares packageSources out of order for plugin '{pluginId}'. Expected releaseAsset -> rawFallback -> workspaceLocal.");
            }

            previousOrder = order;
            if (!seenKinds.Add(normalizedSource.SourceKind))
            {
                throw new InvalidOperationException(
                    $"Market index '{sourceName}' declares duplicate package source kind '{normalizedSource.Kind}' for plugin '{pluginId}'.");
            }

            normalizedSources.Add(normalizedSource);
        }

        return normalizedSources;
    }

    private static List<AirAppMarketPluginDependencyEntry> NormalizeDependencies(
        IReadOnlyList<AirAppMarketPluginDependencyEntry>? dependencies,
        string sourceName,
        string pluginId)
    {
        var normalizedDependencies = new List<AirAppMarketPluginDependencyEntry>((dependencies ?? []).Count);
        var seenDependencies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dependency in dependencies ?? [])
        {
            var normalizedDependency = dependency.ValidateAndNormalize(sourceName);
            var dependencyKey = $"{normalizedDependency.Id}@{normalizedDependency.Version}";
            if (!seenDependencies.Add(dependencyKey))
            {
                throw new InvalidOperationException(
                    $"Market index '{sourceName}' declares duplicate dependency '{dependencyKey}' for plugin '{pluginId}'.");
            }

            normalizedDependencies.Add(normalizedDependency);
        }

        return normalizedDependencies;
    }

    private static List<string> NormalizeValues(IReadOnlyList<string>? values)
    {
        return (values ?? [])
            .Select(NormalizeValue)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
