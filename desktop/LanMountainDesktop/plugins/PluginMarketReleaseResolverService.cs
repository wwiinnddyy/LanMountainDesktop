using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using LanMountainDesktop.Services;

namespace LanMountainDesktop.Services.PluginMarket;

internal sealed class AirAppMarketReleaseResolverService
{
    private readonly HttpClient _httpClient;

    public AirAppMarketReleaseResolverService(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public async Task<string> ResolveDownloadUrlAsync(
        AirAppMarketPluginEntry plugin,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plugin);

        var firstSource = plugin.GetPackageSourcesInInstallOrder().FirstOrDefault();
        if (firstSource is null)
        {
            return plugin.DownloadUrl;
        }

        return await ResolveDownloadUrlAsync(plugin, firstSource, cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> ResolveDownloadUrlAsync(
        AirAppMarketPluginEntry plugin,
        AirAppMarketPluginPackageSourceEntry source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        ArgumentNullException.ThrowIfNull(source);

        return source.SourceKind switch
        {
            PluginPackageSourceKind.ReleaseAsset => await ResolveReleaseAssetDownloadUrlAsync(plugin, source, cancellationToken).ConfigureAwait(false),
            PluginPackageSourceKind.RawFallback => source.Url,
            PluginPackageSourceKind.WorkspaceLocal => source.Url,
            _ => source.Url
        };
    }

    private async Task<string> ResolveReleaseAssetDownloadUrlAsync(
        AirAppMarketPluginEntry plugin,
        AirAppMarketPluginPackageSourceEntry source,
        CancellationToken cancellationToken)
    {
        var sourceUrl = source.Url;
        if (!plugin.HasReleaseDownloadMetadata)
        {
            return sourceUrl;
        }

        if (!TryGetRepositoryIdentity(plugin, out var owner, out var repositoryName))
        {
            return sourceUrl;
        }

        var releaseDownloadUrl = AirAppMarketDefaults.BuildGitHubReleaseDownloadUrl(
            owner,
            repositoryName,
            plugin.ReleaseTag,
            plugin.ReleaseAssetName);

        if (AirAppMarketDefaults.TryResolveWorkspaceFile(releaseDownloadUrl, out _))
        {
            return releaseDownloadUrl;
        }

        try
        {
            using var updateService = new GitHubReleaseUpdateService(owner, repositoryName, _httpClient);
            var release = await updateService.GetReleaseByTagAsync(plugin.ReleaseTag, cancellationToken).ConfigureAwait(false);
            var asset = release?.Assets.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, plugin.ReleaseAssetName, StringComparison.OrdinalIgnoreCase));

            return asset?.BrowserDownloadUrl ?? releaseDownloadUrl;
        }
        catch
        {
            return releaseDownloadUrl;
        }
    }

    private static bool TryGetRepositoryIdentity(
        AirAppMarketPluginEntry plugin,
        out string owner,
        out string repositoryName)
    {
        owner = string.Empty;
        repositoryName = string.Empty;

        return AirAppMarketDefaults.TryParseGitHubRepositoryUrl(plugin.RepositoryUrl, out owner, out repositoryName) ||
               AirAppMarketDefaults.TryParseGitHubRepositoryUrl(plugin.ProjectUrl, out owner, out repositoryName);
    }
}
