using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using LanMountainDesktop.PluginSdk;

namespace LanMountainDesktop.Views.SettingsPages;

internal static class AirAppMarketDefaults
{
    public const string DefaultIndexUrl =
        "https://raw.githubusercontent.com/wwiinnddyy/LanAirApp/main/airappmarket/index.json";

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
        var repositoryRoot = TryGetWorkspaceRepositoryRoot("LanAirApp");
        if (repositoryRoot is null)
        {
            return null;
        }

        var candidatePath = Path.Combine(repositoryRoot, "airappmarket", "index.json");
        return File.Exists(candidatePath) ? candidatePath : null;
    }

    public static bool TryResolveWorkspaceFile(string url, out string localPath)
    {
        localPath = string.Empty;

        string repositoryName;
        string relativePath;

        if (TryParseGitHubReleaseDownloadUrl(url, out repositoryName, out var releaseAssetName))
        {
            relativePath = releaseAssetName;
        }
        else if (!TryParseRawGitHubUrl(url, out repositoryName, out relativePath))
        {
            return false;
        }

        var repositoryRoot = TryGetWorkspaceRepositoryRoot(repositoryName);
        if (repositoryRoot is null)
        {
            return false;
        }

        var candidatePath = Path.GetFullPath(Path.Combine(repositoryRoot, relativePath));
        if (!File.Exists(candidatePath))
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

    private static string? TryGetWorkspaceRepositoryRoot(string repositoryName)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, repositoryName);
            if (Directory.Exists(candidate))
            {
                return candidate;
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

internal sealed class AirAppMarketPluginEntry
{
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

    public DateTimeOffset PublishedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }

    public string ReleaseNotes { get; init; } = string.Empty;

    public bool HasReleaseDownloadMetadata =>
        !string.IsNullOrWhiteSpace(ReleaseTag) &&
        !string.IsNullOrWhiteSpace(ReleaseAssetName);

    public AirAppMarketPluginEntry ValidateAndNormalize(string sourceName)
    {
        var normalizedTags = (Tags ?? [])
            .Select(tag => AirAppMarketIndexDocument.NormalizeValue(tag))
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Select(tag => tag!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(tag => tag, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var normalizedSha = AirAppMarketIndexDocument.NormalizeValue(Sha256)?.ToLowerInvariant()
            ?? throw new InvalidOperationException(
                $"Market index '{sourceName}' is missing required property '{nameof(Sha256)}'.");

        if (normalizedSha.Length != 64 || normalizedSha.Any(ch => !Uri.IsHexDigit(ch)))
        {
            throw new InvalidOperationException(
                $"Market index '{sourceName}' declares invalid SHA-256 '{normalizedSha}' for plugin '{Id}'.");
        }

        var normalizedDownloadUrl = AirAppMarketIndexDocument.NormalizeValue(DownloadUrl)
            ?? throw new InvalidOperationException(
                $"Market index '{sourceName}' is missing required property '{nameof(DownloadUrl)}'.");
        var normalizedIconUrl = AirAppMarketIndexDocument.NormalizeValue(IconUrl)
            ?? throw new InvalidOperationException(
                $"Market index '{sourceName}' is missing required property '{nameof(IconUrl)}'.");
        var normalizedReleaseTag = AirAppMarketIndexDocument.NormalizeValue(ReleaseTag);
        var normalizedReleaseAssetName = AirAppMarketIndexDocument.NormalizeValue(ReleaseAssetName);
        var normalizedProjectUrl = AirAppMarketIndexDocument.NormalizeValue(ProjectUrl)
            ?? throw new InvalidOperationException(
                $"Market index '{sourceName}' is missing required property '{nameof(ProjectUrl)}'.");
        var normalizedReadmeUrl = AirAppMarketIndexDocument.NormalizeValue(ReadmeUrl)
            ?? throw new InvalidOperationException(
                $"Market index '{sourceName}' is missing required property '{nameof(ReadmeUrl)}'.");
        var normalizedHomepageUrl = AirAppMarketIndexDocument.NormalizeValue(HomepageUrl)
            ?? throw new InvalidOperationException(
                $"Market index '{sourceName}' is missing required property '{nameof(HomepageUrl)}'.");
        var normalizedRepositoryUrl = AirAppMarketIndexDocument.NormalizeValue(RepositoryUrl)
            ?? throw new InvalidOperationException(
                $"Market index '{sourceName}' is missing required property '{nameof(RepositoryUrl)}'.");

        AirAppMarketIndexDocument.EnsureUrl(normalizedDownloadUrl, nameof(DownloadUrl), sourceName);
        AirAppMarketIndexDocument.EnsureUrl(normalizedIconUrl, nameof(IconUrl), sourceName);
        normalizedProjectUrl = AirAppMarketIndexDocument.NormalizeGitHubRepositoryUrl(
            normalizedProjectUrl,
            nameof(ProjectUrl),
            sourceName);
        normalizedRepositoryUrl = AirAppMarketIndexDocument.NormalizeGitHubRepositoryUrl(
            normalizedRepositoryUrl,
            nameof(RepositoryUrl),
            sourceName);
        AirAppMarketIndexDocument.EnsureUrl(normalizedReadmeUrl, nameof(ReadmeUrl), sourceName);
        AirAppMarketIndexDocument.EnsureUrl(normalizedHomepageUrl, nameof(HomepageUrl), sourceName);

        if (string.IsNullOrWhiteSpace(normalizedReleaseTag) != string.IsNullOrWhiteSpace(normalizedReleaseAssetName))
        {
            throw new InvalidOperationException(
                $"Market index '{sourceName}' must declare both '{nameof(ReleaseTag)}' and '{nameof(ReleaseAssetName)}' together for plugin '{Id}'.");
        }

        if (!string.IsNullOrWhiteSpace(normalizedReleaseTag))
        {
            normalizedReleaseTag = AirAppMarketIndexDocument.NormalizeReleaseTag(
                normalizedReleaseTag,
                nameof(ReleaseTag),
                sourceName);
        }

        if (PackageSizeBytes <= 0)
        {
            throw new InvalidOperationException(
                $"Market index '{sourceName}' declares invalid packageSizeBytes '{PackageSizeBytes}' for plugin '{Id}'.");
        }

        if (PublishedAt == default || UpdatedAt == default)
        {
            throw new InvalidOperationException(
                $"Market index '{sourceName}' is missing valid publish timestamps for plugin '{Id}'.");
        }

        return new AirAppMarketPluginEntry
        {
            Id = AirAppMarketIndexDocument.NormalizeValue(Id)
                ?? throw new InvalidOperationException($"Market index '{sourceName}' is missing plugin id."),
            Name = AirAppMarketIndexDocument.NormalizeValue(Name)
                ?? throw new InvalidOperationException($"Market index '{sourceName}' is missing plugin name."),
            Description = AirAppMarketIndexDocument.NormalizeValue(Description)
                ?? throw new InvalidOperationException($"Market index '{sourceName}' is missing plugin description."),
            Author = AirAppMarketIndexDocument.NormalizeValue(Author)
                ?? throw new InvalidOperationException($"Market index '{sourceName}' is missing plugin author."),
            Version = AirAppMarketIndexDocument.NormalizeVersion(Version, nameof(Version), sourceName),
            ApiVersion = AirAppMarketIndexDocument.NormalizeVersion(ApiVersion, nameof(ApiVersion), sourceName),
            MinHostVersion = AirAppMarketIndexDocument.NormalizeVersion(MinHostVersion, nameof(MinHostVersion), sourceName),
            DownloadUrl = normalizedDownloadUrl,
            Sha256 = normalizedSha,
            PackageSizeBytes = PackageSizeBytes,
            IconUrl = normalizedIconUrl,
            ReleaseTag = normalizedReleaseTag ?? string.Empty,
            ReleaseAssetName = normalizedReleaseAssetName ?? string.Empty,
            ProjectUrl = normalizedProjectUrl,
            ReadmeUrl = normalizedReadmeUrl,
            HomepageUrl = normalizedHomepageUrl,
            RepositoryUrl = normalizedRepositoryUrl,
            Tags = normalizedTags,
            PublishedAt = PublishedAt,
            UpdatedAt = UpdatedAt,
            ReleaseNotes = AirAppMarketIndexDocument.NormalizeValue(ReleaseNotes)
                ?? throw new InvalidOperationException($"Market index '{sourceName}' is missing release notes for plugin '{Id}'.")
        };
    }

    public string GetVersionSummary()
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            "v{0} | API {1} | Host >= {2}",
            Version,
            ApiVersion,
            MinHostVersion);
    }
}
