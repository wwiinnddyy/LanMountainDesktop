using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using LanMountainDesktop.PluginSdk;

namespace LanMountainDesktop.Services.PluginMarket;

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
        var normalized = AirAppMarketIndexDocument.NormalizeValue(value);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        if (Enum.TryParse(normalized, ignoreCase: true, out kind))
        {
            return true;
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
}

internal enum AirAppMarketLoadSource
{
    Local = 0,
    Network = 1,
    Cache = 2
}

internal enum AirAppMarketInstallState
{
    NotInstalled = 0,
    UpdateAvailable = 1,
    Installed = 2
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
    string? ErrorMessage);

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
        var contracts = Contracts ?? [];
        var normalizedContracts = new List<AirAppMarketSharedContractEntry>(contracts.Count);
        var seenContracts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var contract in contracts)
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

        var plugins = Plugins ?? [];
        var normalizedPlugins = new List<AirAppMarketPluginEntry>(plugins.Count);
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var plugin in plugins)
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
            SchemaVersion = RequireValue(SchemaVersion, nameof(SchemaVersion), sourceName),
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

    private static string RequireValue(string? value, string propertyName, string sourceName)
    {
        var normalized = NormalizeValue(value);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new InvalidOperationException($"Market index '{sourceName}' is missing required property '{propertyName}'.");
        }

        return normalized;
    }

    internal static string? NormalizeValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
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

        version = new Version(
            Math.Max(0, parsed.Major),
            Math.Max(0, parsed.Minor),
            Math.Max(0, parsed.Build));
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
        var normalizedSha = AirAppMarketIndexDocument.NormalizeValue(Sha256)?.ToLowerInvariant()
            ?? throw new InvalidOperationException(
                $"Market index '{sourceName}' is missing required property '{nameof(Sha256)}' for a shared contract.");
        if (normalizedSha.Length != 64 || normalizedSha.Any(ch => !Uri.IsHexDigit(ch)))
        {
            throw new InvalidOperationException(
                $"Market index '{sourceName}' declares invalid SHA-256 '{normalizedSha}' for shared contract '{Id}'.");
        }

        var normalizedDownloadUrl = AirAppMarketIndexDocument.NormalizeValue(DownloadUrl)
            ?? throw new InvalidOperationException(
                $"Market index '{sourceName}' is missing required property '{nameof(DownloadUrl)}' for shared contract '{Id}'.");
        AirAppMarketIndexDocument.EnsureUrl(normalizedDownloadUrl, nameof(DownloadUrl), sourceName);

        if (PackageSizeBytes <= 0)
        {
            throw new InvalidOperationException(
                $"Market index '{sourceName}' declares invalid packageSizeBytes '{PackageSizeBytes}' for shared contract '{Id}'.");
        }

        return new AirAppMarketSharedContractEntry
        {
            Id = AirAppMarketIndexDocument.NormalizeValue(Id)
                ?? throw new InvalidOperationException($"Market index '{sourceName}' is missing a shared contract id."),
            Version = AirAppMarketIndexDocument.NormalizeVersion(Version, nameof(Version), sourceName),
            AssemblyName = AirAppMarketIndexDocument.NormalizeValue(AssemblyName)
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
            Id = AirAppMarketIndexDocument.NormalizeValue(Id)
                ?? throw new InvalidOperationException(
                    $"Market index '{sourceName}' is missing dependency id for a plugin entry."),
            Version = AirAppMarketIndexDocument.NormalizeVersion(Version, nameof(Version), sourceName),
            AssemblyName = AirAppMarketIndexDocument.NormalizeValue(AssemblyName)
                ?? throw new InvalidOperationException(
                    $"Market index '{sourceName}' is missing assemblyName for dependency '{Id}'.")
        };
    }
}

internal sealed class AirAppMarketPluginManifestEntry
{
    public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public string Author { get; init; } = string.Empty;

    public string Version { get; init; } = string.Empty;

    public string ApiVersion { get; init; } = string.Empty;

    public string EntranceAssembly { get; init; } = string.Empty;

    public List<AirAppMarketPluginDependencyEntry> SharedContracts { get; init; } = [];

    public AirAppMarketPluginManifestEntry ValidateAndNormalize(string sourceName)
    {
        return new AirAppMarketPluginManifestEntry
        {
            Id = AirAppMarketIndexDocument.NormalizeValue(Id)
                ?? throw new InvalidOperationException($"Market index '{sourceName}' is missing manifest.id."),
            Name = AirAppMarketIndexDocument.NormalizeValue(Name)
                ?? throw new InvalidOperationException($"Market index '{sourceName}' is missing manifest.name."),
            Description = AirAppMarketIndexDocument.NormalizeValue(Description)
                ?? throw new InvalidOperationException($"Market index '{sourceName}' is missing manifest.description."),
            Author = AirAppMarketIndexDocument.NormalizeValue(Author)
                ?? throw new InvalidOperationException($"Market index '{sourceName}' is missing manifest.author."),
            Version = AirAppMarketIndexDocument.NormalizeVersion(Version, nameof(Version), sourceName),
            ApiVersion = AirAppMarketIndexDocument.NormalizeVersion(ApiVersion, nameof(ApiVersion), sourceName),
            EntranceAssembly = AirAppMarketIndexDocument.NormalizeValue(EntranceAssembly) ?? string.Empty,
            SharedContracts = NormalizeDependencies(sourceName, SharedContracts)
        };
    }

    private static List<AirAppMarketPluginDependencyEntry> NormalizeDependencies(
        string sourceName,
        IReadOnlyList<AirAppMarketPluginDependencyEntry>? dependencies)
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
                    $"Market index '{sourceName}' declares duplicate dependency '{dependencyKey}' in plugin manifest.");
            }

            normalizedDependencies.Add(normalizedDependency);
        }

        return normalizedDependencies;
    }
}

internal sealed class AirAppMarketPluginCompatibilityEntry
{
    public string MinHostVersion { get; init; } = string.Empty;

    public string PluginApiVersion { get; init; } = string.Empty;

    public AirAppMarketPluginCompatibilityEntry ValidateAndNormalize(string sourceName)
    {
        return new AirAppMarketPluginCompatibilityEntry
        {
            MinHostVersion = AirAppMarketIndexDocument.NormalizeVersion(
                MinHostVersion,
                nameof(MinHostVersion),
                sourceName),
            PluginApiVersion = AirAppMarketIndexDocument.NormalizeVersion(
                PluginApiVersion,
                nameof(PluginApiVersion),
                sourceName)
        };
    }
}

internal sealed class AirAppMarketPluginRepositoryEntry
{
    public string IconUrl { get; init; } = string.Empty;

    public string ProjectUrl { get; init; } = string.Empty;

    public string ReadmeUrl { get; init; } = string.Empty;

    public string HomepageUrl { get; init; } = string.Empty;

    public string RepositoryUrl { get; init; } = string.Empty;

    public List<string> Tags { get; init; } = [];

    public string ReleaseNotes { get; init; } = string.Empty;

    public AirAppMarketPluginRepositoryEntry ValidateAndNormalize(string sourceName)
    {
        var normalizedRepositoryUrl = AirAppMarketIndexDocument.NormalizeGitHubRepositoryUrl(
            AirAppMarketIndexDocument.NormalizeValue(RepositoryUrl)
            ?? throw new InvalidOperationException($"Market index '{sourceName}' is missing repository.repositoryUrl."),
            nameof(RepositoryUrl),
            sourceName);

        var normalizedIconUrl = AirAppMarketIndexDocument.NormalizeValue(IconUrl) ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(normalizedIconUrl))
        {
            AirAppMarketIndexDocument.EnsureUrl(normalizedIconUrl, nameof(IconUrl), sourceName);
        }

        var normalizedProjectUrl = AirAppMarketIndexDocument.NormalizeValue(ProjectUrl) ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(normalizedProjectUrl))
        {
            AirAppMarketIndexDocument.NormalizeGitHubRepositoryUrl(
                normalizedProjectUrl,
                nameof(ProjectUrl),
                sourceName);
        }

        var normalizedReadmeUrl = AirAppMarketIndexDocument.NormalizeValue(ReadmeUrl) ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(normalizedReadmeUrl))
        {
            AirAppMarketIndexDocument.EnsureUrl(normalizedReadmeUrl, nameof(ReadmeUrl), sourceName);
        }

        var normalizedHomepageUrl = AirAppMarketIndexDocument.NormalizeValue(HomepageUrl) ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(normalizedHomepageUrl))
        {
            AirAppMarketIndexDocument.EnsureUrl(normalizedHomepageUrl, nameof(HomepageUrl), sourceName);
        }

        var normalizedTags = (Tags ?? [])
            .Select(AirAppMarketIndexDocument.NormalizeValue)
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Select(tag => tag!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(tag => tag, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new AirAppMarketPluginRepositoryEntry
        {
            IconUrl = normalizedIconUrl,
            ProjectUrl = normalizedProjectUrl,
            ReadmeUrl = normalizedReadmeUrl,
            HomepageUrl = normalizedHomepageUrl,
            RepositoryUrl = normalizedRepositoryUrl,
            Tags = normalizedTags,
            ReleaseNotes = AirAppMarketIndexDocument.NormalizeValue(ReleaseNotes) ?? string.Empty
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
        var normalizedKind = AirAppMarketIndexDocument.NormalizeValue(Kind)
            ?? throw new InvalidOperationException(
                $"Market index '{sourceName}' is missing package source kind for plugin '{pluginId}'.");
        if (!AirAppMarketDefaults.TryParsePackageSourceKind(normalizedKind, out var sourceKind))
        {
            throw new InvalidOperationException(
                $"Market index '{sourceName}' declares invalid package source kind '{normalizedKind}' for plugin '{pluginId}'.");
        }

        var normalizedUrl = AirAppMarketIndexDocument.NormalizeValue(Url)
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
            if (File.Exists(url))
            {
                return;
            }

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

internal sealed class AirAppMarketPluginPublicationEntry
{
    public string ReleaseTag { get; init; } = string.Empty;

    public string ReleaseAssetName { get; init; } = string.Empty;

    public DateTimeOffset PublishedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }

    public long PackageSizeBytes { get; init; }

    public string Sha256 { get; init; } = string.Empty;

    public string Md5 { get; init; } = string.Empty;

    public List<AirAppMarketPluginPackageSourceEntry> PackageSources { get; init; } = [];

    public AirAppMarketPluginPublicationEntry ValidateAndNormalize(string sourceName, string pluginId)
    {
        var normalizedPackageSources = NormalizePackageSources(PackageSources, sourceName, pluginId);
        var normalizedReleaseTag = AirAppMarketIndexDocument.NormalizeValue(ReleaseTag) ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(normalizedReleaseTag))
        {
            normalizedReleaseTag = AirAppMarketIndexDocument.NormalizeReleaseTag(
                normalizedReleaseTag,
                nameof(ReleaseTag),
                sourceName);
        }

        var normalizedReleaseAssetName = AirAppMarketIndexDocument.NormalizeValue(ReleaseAssetName) ?? string.Empty;
        var normalizedSha256 = AirAppMarketIndexDocument.NormalizeValue(Sha256)?.ToLowerInvariant() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(normalizedSha256) &&
            (normalizedSha256.Length != 64 || normalizedSha256.Any(ch => !Uri.IsHexDigit(ch))))
        {
            throw new InvalidOperationException(
                $"Market index '{sourceName}' declares invalid SHA-256 '{normalizedSha256}' for plugin '{pluginId}'.");
        }

        var normalizedMd5 = AirAppMarketIndexDocument.NormalizeValue(Md5)?.ToLowerInvariant() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(normalizedMd5) &&
            (normalizedMd5.Length != 32 || normalizedMd5.Any(ch => !Uri.IsHexDigit(ch))))
        {
            throw new InvalidOperationException(
                $"Market index '{sourceName}' declares invalid MD5 '{normalizedMd5}' for plugin '{pluginId}'.");
        }

        return new AirAppMarketPluginPublicationEntry
        {
            ReleaseTag = normalizedReleaseTag,
            ReleaseAssetName = normalizedReleaseAssetName,
            PublishedAt = PublishedAt,
            UpdatedAt = UpdatedAt,
            PackageSizeBytes = PackageSizeBytes,
            Sha256 = normalizedSha256,
            Md5 = normalizedMd5,
            PackageSources = normalizedPackageSources
        };
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
}

internal sealed class AirAppMarketPluginCapabilitiesEntry
{
    public List<AirAppMarketPluginDependencyEntry> SharedContracts { get; init; } = [];

    public List<string> DesktopComponents { get; init; } = [];

    public List<string> SettingsSections { get; init; } = [];

    public List<string> Exports { get; init; } = [];

    public List<string> MessageTypes { get; init; } = [];

    public AirAppMarketPluginCapabilitiesEntry ValidateAndNormalize(string sourceName)
    {
        return new AirAppMarketPluginCapabilitiesEntry
        {
            SharedContracts = NormalizeDependencies(sourceName, SharedContracts),
            DesktopComponents = NormalizeValues(DesktopComponents),
            SettingsSections = NormalizeValues(SettingsSections),
            Exports = NormalizeValues(Exports),
            MessageTypes = NormalizeValues(MessageTypes)
        };
    }

    private static List<AirAppMarketPluginDependencyEntry> NormalizeDependencies(
        string sourceName,
        IReadOnlyList<AirAppMarketPluginDependencyEntry>? dependencies)
    {
        var normalizedDependencies = new List<AirAppMarketPluginDependencyEntry>((dependencies ?? []).Count);
        var seenDependencies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dependency in dependencies ?? [])
        {
            var normalizedDependency = dependency.ValidateAndNormalize(sourceName);
            var key = $"{normalizedDependency.Id}@{normalizedDependency.Version}";
            if (!seenDependencies.Add(key))
            {
                throw new InvalidOperationException(
                    $"Market index '{sourceName}' declares duplicate capability dependency '{key}'.");
            }

            normalizedDependencies.Add(normalizedDependency);
        }

        return normalizedDependencies;
    }

    private static List<string> NormalizeValues(IReadOnlyList<string>? values)
    {
        return (values ?? [])
            .Select(AirAppMarketIndexDocument.NormalizeValue)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}

internal sealed class AirAppMarketPluginEntry
{
    public string PluginId { get; init; } = string.Empty;

    public AirAppMarketPluginManifestEntry? Manifest { get; init; }

    public AirAppMarketPluginCompatibilityEntry? Compatibility { get; init; }

    public AirAppMarketPluginRepositoryEntry? Repository { get; init; }

    public AirAppMarketPluginPublicationEntry? Publication { get; init; }

    public AirAppMarketPluginCapabilitiesEntry? Capabilities { get; init; }

    public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public string Author { get; init; } = string.Empty;

    public string Version { get; init; } = string.Empty;

    public string ApiVersion { get; init; } = string.Empty;

    public string MinHostVersion { get; init; } = string.Empty;

    public string DownloadUrl { get; init; } = string.Empty;

    public string Sha256 { get; init; } = string.Empty;

    public long PackageSizeBytes { get; init; }

    public string IconUrl { get; init; } = string.Empty;

    public string ReleaseTag { get; init; } = string.Empty;

    public string ReleaseAssetName { get; init; } = string.Empty;

    public string ProjectUrl { get; init; } = string.Empty;

    public string ReadmeUrl { get; init; } = string.Empty;

    public string HomepageUrl { get; init; } = string.Empty;

    public string RepositoryUrl { get; init; } = string.Empty;

    public List<string> Tags { get; init; } = [];

    public List<AirAppMarketPluginDependencyEntry> SharedContracts { get; init; } = [];

    public List<AirAppMarketPluginPackageSourceEntry> PackageSources { get; init; } = [];

    public string Md5 { get; init; } = string.Empty;

    public DateTimeOffset PublishedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }

    public string ReleaseNotes { get; init; } = string.Empty;

    public bool HasReleaseDownloadMetadata =>
        !string.IsNullOrWhiteSpace(ReleaseTag) &&
        !string.IsNullOrWhiteSpace(ReleaseAssetName);

    public AirAppMarketPluginEntry ValidateAndNormalize(string sourceName)
    {
        var normalizedManifest = HasManifestData(Manifest)
            ? Manifest!.ValidateAndNormalize(sourceName)
            : null;
        var normalizedCompatibility = HasCompatibilityData(Compatibility)
            ? Compatibility!.ValidateAndNormalize(sourceName)
            : null;
        var normalizedRepository = HasRepositoryData(Repository)
            ? Repository!.ValidateAndNormalize(sourceName)
            : null;
        var normalizedCapabilities = HasCapabilitiesData(Capabilities)
            ? Capabilities!.ValidateAndNormalize(sourceName)
            : null;
        var resolvedPluginId = FirstNonEmpty(
            normalizedManifest?.Id,
            AirAppMarketIndexDocument.NormalizeValue(PluginId),
            AirAppMarketIndexDocument.NormalizeValue(Id))
            ?? throw new InvalidOperationException($"Market index '{sourceName}' is missing plugin id.");
        var normalizedPublication = HasPublicationData(Publication)
            ? Publication!.ValidateAndNormalize(sourceName, resolvedPluginId)
            : null;

        var resolvedPackageSources = NormalizePackageSources(
            normalizedPublication?.PackageSources ?? PackageSources,
            sourceName,
            resolvedPluginId,
            AirAppMarketIndexDocument.NormalizeValue(DownloadUrl));
        if (resolvedPackageSources.Count == 0)
        {
            throw new InvalidOperationException(
                $"Market index '{sourceName}' is missing package sources for plugin '{resolvedPluginId}'.");
        }

        var resolvedRepositoryUrl = FirstNonEmpty(
            normalizedRepository?.RepositoryUrl,
            AirAppMarketIndexDocument.NormalizeValue(RepositoryUrl));
        if (string.IsNullOrWhiteSpace(resolvedRepositoryUrl))
        {
            throw new InvalidOperationException($"Market index '{sourceName}' is missing plugin repositoryUrl.");
        }

        var resolvedDownloadUrl = FirstNonEmpty(
            resolvedPackageSources.FirstOrDefault()?.Url,
            AirAppMarketIndexDocument.NormalizeValue(DownloadUrl))
            ?? string.Empty;

        var resolvedName = FirstNonEmpty(
            normalizedManifest?.Name,
            AirAppMarketIndexDocument.NormalizeValue(Name))
            ?? string.Empty;
        var resolvedDescription = FirstNonEmpty(
            normalizedManifest?.Description,
            AirAppMarketIndexDocument.NormalizeValue(Description))
            ?? string.Empty;
        var resolvedAuthor = FirstNonEmpty(
            normalizedManifest?.Author,
            AirAppMarketIndexDocument.NormalizeValue(Author))
            ?? string.Empty;
        var resolvedVersion = FirstNonEmpty(
            normalizedManifest?.Version,
            AirAppMarketIndexDocument.NormalizeValue(Version))
            ?? string.Empty;
        var resolvedApiVersion = FirstNonEmpty(
            normalizedCompatibility?.PluginApiVersion,
            normalizedManifest?.ApiVersion,
            AirAppMarketIndexDocument.NormalizeValue(ApiVersion))
            ?? string.Empty;
        var resolvedMinHostVersion = FirstNonEmpty(
            normalizedCompatibility?.MinHostVersion,
            AirAppMarketIndexDocument.NormalizeValue(MinHostVersion))
            ?? string.Empty;

        var resolvedIconUrl = FirstNonEmpty(
            normalizedRepository?.IconUrl,
            AirAppMarketIndexDocument.NormalizeValue(IconUrl))
            ?? string.Empty;
        var resolvedProjectUrl = FirstNonEmpty(
            normalizedRepository?.ProjectUrl,
            AirAppMarketIndexDocument.NormalizeValue(ProjectUrl))
            ?? string.Empty;
        var resolvedReadmeUrl = FirstNonEmpty(
            normalizedRepository?.ReadmeUrl,
            AirAppMarketIndexDocument.NormalizeValue(ReadmeUrl))
            ?? string.Empty;
        var resolvedHomepageUrl = FirstNonEmpty(
            normalizedRepository?.HomepageUrl,
            AirAppMarketIndexDocument.NormalizeValue(HomepageUrl))
            ?? string.Empty;

        var resolvedReleaseTag = FirstNonEmpty(
            normalizedPublication?.ReleaseTag,
            AirAppMarketIndexDocument.NormalizeValue(ReleaseTag))
            ?? string.Empty;
        var resolvedReleaseAssetName = FirstNonEmpty(
            normalizedPublication?.ReleaseAssetName,
            AirAppMarketIndexDocument.NormalizeValue(ReleaseAssetName))
            ?? string.Empty;
        var resolvedPackageSize = normalizedPublication?.PackageSizeBytes ?? PackageSizeBytes;
        var resolvedSha256 = FirstNonEmpty(
            normalizedPublication?.Sha256,
            AirAppMarketIndexDocument.NormalizeValue(Sha256)?.ToLowerInvariant())
            ?? string.Empty;
        var resolvedMd5 = FirstNonEmpty(
            normalizedPublication?.Md5,
            AirAppMarketIndexDocument.NormalizeValue(Md5)?.ToLowerInvariant())
            ?? string.Empty;
        var resolvedPublishedAt = normalizedPublication?.PublishedAt ?? PublishedAt;
        var resolvedUpdatedAt = normalizedPublication?.UpdatedAt ?? UpdatedAt;

        var resolvedDependencies = NormalizeDependencies(
            normalizedManifest?.SharedContracts ?? SharedContracts,
            sourceName,
            resolvedPluginId);
        var resolvedTags = (normalizedRepository?.Tags ?? Tags ?? [])
            .Select(AirAppMarketIndexDocument.NormalizeValue)
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Select(tag => tag!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(tag => tag, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var resolvedReleaseNotes = FirstNonEmpty(
            normalizedRepository?.ReleaseNotes,
            AirAppMarketIndexDocument.NormalizeValue(ReleaseNotes))
            ?? string.Empty;

        return new AirAppMarketPluginEntry
        {
            PluginId = resolvedPluginId,
            Manifest = normalizedManifest,
            Compatibility = normalizedCompatibility,
            Repository = normalizedRepository,
            Publication = normalizedPublication,
            Capabilities = normalizedCapabilities,
            Id = resolvedPluginId,
            Name = resolvedName,
            Description = resolvedDescription,
            Author = resolvedAuthor,
            Version = resolvedVersion,
            ApiVersion = resolvedApiVersion,
            MinHostVersion = resolvedMinHostVersion,
            DownloadUrl = resolvedDownloadUrl,
            Sha256 = resolvedSha256,
            Md5 = resolvedMd5,
            PackageSizeBytes = resolvedPackageSize,
            IconUrl = resolvedIconUrl,
            ReleaseTag = resolvedReleaseTag ?? string.Empty,
            ReleaseAssetName = resolvedReleaseAssetName ?? string.Empty,
            ProjectUrl = resolvedProjectUrl,
            ReadmeUrl = resolvedReadmeUrl,
            HomepageUrl = resolvedHomepageUrl,
            RepositoryUrl = resolvedRepositoryUrl,
            Tags = resolvedTags,
            SharedContracts = resolvedDependencies,
            PackageSources = resolvedPackageSources,
            PublishedAt = resolvedPublishedAt,
            UpdatedAt = resolvedUpdatedAt,
            ReleaseNotes = resolvedReleaseNotes
        };
    }

    public string GetVersionSummary()
    {
        if (string.IsNullOrWhiteSpace(Version) &&
            string.IsNullOrWhiteSpace(ApiVersion) &&
            string.IsNullOrWhiteSpace(MinHostVersion))
        {
            return "Unknown";
        }

        return string.Format(
            CultureInfo.InvariantCulture,
            "v{0} | API {1} | Host >= {2}",
            Version,
            ApiVersion,
            MinHostVersion);
    }

    public IReadOnlyList<AirAppMarketPluginPackageSourceEntry> GetPackageSourcesInInstallOrder()
    {
        if (PackageSources.Count > 0)
        {
            return PackageSources
                .OrderBy(source => AirAppMarketDefaults.GetPackageSourceOrder(source.SourceKind))
                .ToList();
        }

        if (string.IsNullOrWhiteSpace(DownloadUrl))
        {
            return [];
        }

        var sourceKind = HasReleaseDownloadMetadata
            ? PluginPackageSourceKind.ReleaseAsset
            : PluginPackageSourceKind.RawFallback;
        return
        [
            new AirAppMarketPluginPackageSourceEntry
            {
                Kind = sourceKind switch
                {
                    PluginPackageSourceKind.ReleaseAsset => "releaseAsset",
                    PluginPackageSourceKind.RawFallback => "rawFallback",
                    PluginPackageSourceKind.WorkspaceLocal => "workspaceLocal",
                    _ => "rawFallback"
                },
                Url = DownloadUrl,
                SourceKind = sourceKind
            }
        ];
    }

    private static bool HasManifestData(AirAppMarketPluginManifestEntry? manifest)
    {
        return manifest is not null &&
               (!string.IsNullOrWhiteSpace(manifest.Id) ||
                !string.IsNullOrWhiteSpace(manifest.Name) ||
                !string.IsNullOrWhiteSpace(manifest.Version));
    }

    private static bool HasCompatibilityData(AirAppMarketPluginCompatibilityEntry? compatibility)
    {
        return compatibility is not null &&
               (!string.IsNullOrWhiteSpace(compatibility.MinHostVersion) ||
                !string.IsNullOrWhiteSpace(compatibility.PluginApiVersion));
    }

    private static bool HasRepositoryData(AirAppMarketPluginRepositoryEntry? repository)
    {
        return repository is not null &&
               (!string.IsNullOrWhiteSpace(repository.IconUrl) ||
                !string.IsNullOrWhiteSpace(repository.ProjectUrl) ||
                !string.IsNullOrWhiteSpace(repository.RepositoryUrl));
    }

    private static bool HasPublicationData(AirAppMarketPluginPublicationEntry? publication)
    {
        return publication is not null &&
               (!string.IsNullOrWhiteSpace(publication.ReleaseTag) ||
                !string.IsNullOrWhiteSpace(publication.ReleaseAssetName) ||
                publication.PackageSources.Count > 0);
    }

    private static bool HasCapabilitiesData(AirAppMarketPluginCapabilitiesEntry? capabilities)
    {
        return capabilities is not null &&
               (capabilities.SharedContracts.Count > 0 ||
                capabilities.DesktopComponents.Count > 0 ||
                capabilities.SettingsSections.Count > 0 ||
                capabilities.Exports.Count > 0 ||
                capabilities.MessageTypes.Count > 0);
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

    private static List<AirAppMarketPluginPackageSourceEntry> NormalizePackageSources(
        IReadOnlyList<AirAppMarketPluginPackageSourceEntry>? packageSources,
        string sourceName,
        string pluginId,
        string? legacyDownloadUrl)
    {
        var normalizedSources = new List<AirAppMarketPluginPackageSourceEntry>((packageSources ?? []).Count + 1);
        foreach (var source in packageSources ?? [])
        {
            normalizedSources.Add(source.ValidateAndNormalize(sourceName, pluginId));
        }

        if (normalizedSources.Count > 0)
        {
            return normalizedSources
                .OrderBy(source => AirAppMarketDefaults.GetPackageSourceOrder(source.SourceKind))
                .ToList();
        }

        var normalizedLegacyDownloadUrl = AirAppMarketIndexDocument.NormalizeValue(legacyDownloadUrl);
        if (!string.IsNullOrWhiteSpace(normalizedLegacyDownloadUrl))
        {
            var legacySource = new AirAppMarketPluginPackageSourceEntry
            {
                Kind = "rawFallback",
                Url = normalizedLegacyDownloadUrl,
                SourceKind = PluginPackageSourceKind.RawFallback
            };
            normalizedSources.Add(legacySource.ValidateAndNormalize(sourceName, pluginId));
            return normalizedSources;
        }

        return normalizedSources;
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            var normalized = AirAppMarketIndexDocument.NormalizeValue(value);
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                return normalized;
            }
        }

        return null;
    }
}
