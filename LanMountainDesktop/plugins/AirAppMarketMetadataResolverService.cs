using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LanMountainDesktop.PluginSdk;

namespace LanMountainDesktop.Services.PluginMarket;

internal sealed class AirAppMarketMetadataResolverService : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly ConcurrentDictionary<string, string> _defaultBranchCache = new(StringComparer.OrdinalIgnoreCase);

    public AirAppMarketMetadataResolverService(HttpClient? httpClient = null)
    {
        if (httpClient is null)
        {
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(20)
            };
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("LanMountainDesktop-PluginMarketplace/1.0");
            _httpClient.DefaultRequestHeaders.Accept.Add(
                new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
            _ownsHttpClient = true;
        }
        else
        {
            _httpClient = httpClient;
            _ownsHttpClient = false;
        }
    }

    public async Task<AirAppMarketIndexDocument> EnrichAsync(
        AirAppMarketIndexDocument document,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (document.Plugins.Count == 0)
        {
            return document;
        }

        var enrichedPlugins = new List<AirAppMarketPluginEntry>(document.Plugins.Count);
        foreach (var plugin in document.Plugins)
        {
            enrichedPlugins.Add(await EnrichPluginAsync(plugin, cancellationToken).ConfigureAwait(false));
        }

        return new AirAppMarketIndexDocument
        {
            SchemaVersion = document.SchemaVersion,
            SourceId = document.SourceId,
            SourceName = document.SourceName,
            GeneratedAt = document.GeneratedAt,
            Contracts = document.Contracts,
            Plugins = enrichedPlugins
        };
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    private async Task<AirAppMarketPluginEntry> EnrichPluginAsync(
        AirAppMarketPluginEntry entry,
        CancellationToken cancellationToken)
    {
        if (!AirAppMarketDefaults.TryParseGitHubRepositoryUrl(entry.RepositoryUrl, out var owner, out var repositoryName) &&
            !AirAppMarketDefaults.TryParseGitHubRepositoryUrl(entry.ProjectUrl, out owner, out repositoryName))
        {
            return entry;
        }

        var branchCandidates = await GetBranchCandidatesAsync(owner, repositoryName, cancellationToken).ConfigureAwait(false);
        PluginManifest? manifest = null;
        AirAppMarketRepositoryTemplate? template = null;

        foreach (var branch in branchCandidates)
        {
            manifest ??= await TryLoadPluginManifestAsync(owner, repositoryName, branch, cancellationToken).ConfigureAwait(false);
            template ??= await TryLoadTemplateAsync(owner, repositoryName, branch, cancellationToken).ConfigureAwait(false);

            if (manifest is not null && template is not null)
            {
                break;
            }
        }

        var repository = entry.Repository ?? new AirAppMarketPluginRepositoryEntry();
        var resolvedManifest = manifest;
        var resolvedPackageSources = entry.PackageSources.Count > 0
            ? entry.PackageSources
            : entry.Publication?.PackageSources ?? [];
        var firstPackageSourceUrl = resolvedPackageSources.FirstOrDefault()?.Url ?? entry.DownloadUrl;
        var existingManifest = entry.Manifest;
        var existingCompatibility = entry.Compatibility;
        var existingPublication = entry.Publication;

        return new AirAppMarketPluginEntry
        {
            PluginId = AirAppMarketIndexDocument.NormalizeValue(entry.PluginId) ?? entry.PluginId,
            Manifest = resolvedManifest is null
                ? entry.Manifest
                : new AirAppMarketPluginManifestEntry
                {
                    Id = resolvedManifest.Id,
                    Name = resolvedManifest.Name,
                    Description = resolvedManifest.Description ?? string.Empty,
                    Author = resolvedManifest.Author ?? string.Empty,
                    Version = resolvedManifest.Version ?? string.Empty,
                    ApiVersion = resolvedManifest.ApiVersion ?? string.Empty,
                    EntranceAssembly = resolvedManifest.EntranceAssembly,
                    SharedContracts = resolvedManifest.SharedContracts?
                        .Select(contract => new AirAppMarketPluginDependencyEntry
                        {
                            Id = contract.Id,
                            Version = contract.Version,
                            AssemblyName = contract.AssemblyName
                        })
                        .ToList()
                        ?? []
                },
            Compatibility = entry.Compatibility is not null || template is not null || !string.IsNullOrWhiteSpace(entry.MinHostVersion) || !string.IsNullOrWhiteSpace(entry.ApiVersion)
                ? new AirAppMarketPluginCompatibilityEntry
                {
                    MinHostVersion = FirstNonEmpty(
                        template?.MinHostVersion,
                        existingCompatibility?.MinHostVersion,
                        entry.MinHostVersion),
                    PluginApiVersion = FirstNonEmpty(
                        resolvedManifest?.ApiVersion,
                        existingCompatibility?.PluginApiVersion,
                        existingCompatibility?.ApiVersion,
                        existingManifest?.ApiVersion,
                        entry.ApiVersion)
                        ?? string.Empty
                }
                : null,
            Repository = new AirAppMarketPluginRepositoryEntry
            {
                IconUrl = FirstNonEmpty(template?.IconUrl, repository.IconUrl, entry.IconUrl) ?? string.Empty,
                ProjectUrl = FirstNonEmpty(template?.ProjectUrl, repository.ProjectUrl, entry.ProjectUrl) ?? string.Empty,
                ReadmeUrl = FirstNonEmpty(template?.ReadmeUrl, repository.ReadmeUrl, entry.ReadmeUrl) ?? string.Empty,
                HomepageUrl = FirstNonEmpty(template?.HomepageUrl, repository.HomepageUrl, entry.HomepageUrl) ?? string.Empty,
                RepositoryUrl = FirstNonEmpty(template?.RepositoryUrl, repository.RepositoryUrl, entry.RepositoryUrl, entry.ProjectUrl)
                    ?? string.Empty,
                Tags = FirstNonEmptyList(template?.Tags, repository.Tags, entry.Tags),
                ReleaseNotes = FirstNonEmpty(template?.ReleaseNotes, repository.ReleaseNotes, entry.ReleaseNotes) ?? string.Empty
            },
            Publication = entry.Publication,
            Capabilities = entry.Capabilities,
            Id = FirstNonEmpty(resolvedManifest?.Id, existingManifest?.Id, entry.Id, entry.PluginId) ?? entry.PluginId,
            Name = FirstNonEmpty(resolvedManifest?.Name, existingManifest?.Name, entry.Name) ?? string.Empty,
            Description = FirstNonEmpty(resolvedManifest?.Description, existingManifest?.Description, entry.Description) ?? string.Empty,
            Author = FirstNonEmpty(resolvedManifest?.Author, existingManifest?.Author, entry.Author) ?? string.Empty,
            Version = FirstNonEmpty(resolvedManifest?.Version, existingManifest?.Version, entry.Version) ?? string.Empty,
            ApiVersion = FirstNonEmpty(
                resolvedManifest?.ApiVersion,
                existingCompatibility?.PluginApiVersion,
                existingCompatibility?.ApiVersion,
                existingManifest?.ApiVersion,
                entry.ApiVersion) ?? string.Empty,
            MinHostVersion = FirstNonEmpty(template?.MinHostVersion, existingCompatibility?.MinHostVersion, entry.MinHostVersion) ?? string.Empty,
            DownloadUrl = FirstNonEmpty(firstPackageSourceUrl, entry.DownloadUrl) ?? string.Empty,
            Sha256 = FirstNonEmpty(existingPublication?.Sha256, entry.Sha256) ?? string.Empty,
            PackageSizeBytes = existingPublication?.PackageSizeBytes > 0 ? existingPublication.PackageSizeBytes : entry.PackageSizeBytes,
            IconUrl = FirstNonEmpty(template?.IconUrl, repository.IconUrl, entry.IconUrl) ?? string.Empty,
            ReleaseTag = FirstNonEmpty(existingPublication?.ReleaseTag, entry.ReleaseTag) ?? string.Empty,
            ReleaseAssetName = FirstNonEmpty(existingPublication?.ReleaseAssetName, entry.ReleaseAssetName) ?? string.Empty,
            ProjectUrl = FirstNonEmpty(template?.ProjectUrl, repository.ProjectUrl, entry.ProjectUrl) ?? string.Empty,
            ReadmeUrl = FirstNonEmpty(template?.ReadmeUrl, repository.ReadmeUrl, entry.ReadmeUrl) ?? string.Empty,
            HomepageUrl = FirstNonEmpty(template?.HomepageUrl, repository.HomepageUrl, entry.HomepageUrl) ?? string.Empty,
            RepositoryUrl = FirstNonEmpty(template?.RepositoryUrl, repository.RepositoryUrl, entry.RepositoryUrl, entry.ProjectUrl)
                ?? string.Empty,
            Tags = FirstNonEmptyList(template?.Tags, repository.Tags, entry.Tags),
            SharedContracts = resolvedManifest?.SharedContracts
                ?.Select(contract => new AirAppMarketPluginDependencyEntry
                {
                    Id = contract.Id,
                    Version = contract.Version,
                    AssemblyName = contract.AssemblyName
                })
                .ToList()
                ?? entry.SharedContracts,
            PackageSources = resolvedPackageSources,
            Md5 = FirstNonEmpty(existingPublication?.Md5, entry.Md5) ?? string.Empty,
            PublishedAt = existingPublication?.PublishedAt ?? entry.PublishedAt,
            UpdatedAt = existingPublication?.UpdatedAt ?? entry.UpdatedAt,
            ReleaseNotes = FirstNonEmpty(template?.ReleaseNotes, repository.ReleaseNotes, entry.ReleaseNotes) ?? string.Empty
        };
    }

    private async Task<PluginManifest?> TryLoadPluginManifestAsync(
        string owner,
        string repositoryName,
        string branch,
        CancellationToken cancellationToken)
    {
        var candidateUrl = AirAppMarketDefaults.BuildGitHubRawUrl(owner, repositoryName, branch, "plugin.json");
        var text = await TryReadTextAsync(candidateUrl, cancellationToken).ConfigureAwait(false);
        if (text is null)
        {
            return null;
        }

        try
        {
            await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));
            return PluginManifest.Load(stream, candidateUrl);
        }
        catch
        {
            return null;
        }
    }

    private async Task<AirAppMarketRepositoryTemplate?> TryLoadTemplateAsync(
        string owner,
        string repositoryName,
        string branch,
        CancellationToken cancellationToken)
    {
        var candidateUrl = AirAppMarketDefaults.BuildGitHubRawUrl(owner, repositoryName, branch, "airappmarket-entry.template.json");
        var text = await TryReadTextAsync(candidateUrl, cancellationToken).ConfigureAwait(false);
        if (text is null)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<AirAppMarketRepositoryTemplate>(text, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private async Task<IReadOnlyList<string>> GetBranchCandidatesAsync(
        string owner,
        string repositoryName,
        CancellationToken cancellationToken)
    {
        var candidates = new List<string>(4);

        if (_defaultBranchCache.TryGetValue(FormatRepositoryKey(owner, repositoryName), out var cachedBranch) &&
            !string.IsNullOrWhiteSpace(cachedBranch))
        {
            candidates.Add(cachedBranch);
        }
        else
        {
            var defaultBranch = await TryGetDefaultBranchAsync(owner, repositoryName, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(defaultBranch))
            {
                _defaultBranchCache[FormatRepositoryKey(owner, repositoryName)] = defaultBranch;
                candidates.Add(defaultBranch);
            }
        }

        candidates.Add("main");
        candidates.Add("master");

        return candidates
            .Where(branch => !string.IsNullOrWhiteSpace(branch))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private async Task<string?> TryGetDefaultBranchAsync(
        string owner,
        string repositoryName,
        CancellationToken cancellationToken)
    {
        var url = $"https://api.github.com/repos/{owner}/{repositoryName}";
        try
        {
            using var response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
            var responseText = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            using var document = JsonDocument.Parse(responseText);
            if (document.RootElement.TryGetProperty("default_branch", out var branchNode))
            {
                return AirAppMarketIndexDocument.NormalizeValue(branchNode.GetString());
            }
        }
        catch
        {
            // Fallback to conventional branches.
        }

        return null;
    }

    private async Task<string?> TryReadTextAsync(string url, CancellationToken cancellationToken)
    {
        if (AirAppMarketDefaults.TryResolveWorkspaceFile(url, out var localPath))
        {
            try
            {
                return await File.ReadAllTextAsync(localPath, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                return null;
            }
        }

        try
        {
            using var response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    private static string FormatRepositoryKey(string owner, string repositoryName)
    {
        return $"{owner.Trim()}/{repositoryName.Trim()}";
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

    private static List<string> FirstNonEmptyList(params IReadOnlyList<string>?[] lists)
    {
        foreach (var list in lists)
        {
            if (list is null || list.Count == 0)
            {
                continue;
            }

            var normalized = list
                .Select(AirAppMarketIndexDocument.NormalizeValue)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (normalized.Count > 0)
            {
                return normalized;
            }
        }

        return [];
    }

    private sealed record AirAppMarketRepositoryTemplate(
        string? MinHostVersion,
        string? IconUrl,
        string? ProjectUrl,
        string? ReadmeUrl,
        string? HomepageUrl,
        string? RepositoryUrl,
        List<string>? Tags,
        string? ReleaseNotes);
}
