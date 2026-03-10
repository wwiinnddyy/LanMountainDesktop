using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using LanMountainDesktop.Services;

namespace LanMountainDesktop.Views.SettingsPages;

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

        if (!plugin.HasReleaseDownloadMetadata)
        {
            return plugin.DownloadUrl;
        }

        if (!TryGetRepositoryIdentity(plugin, out var owner, out var repositoryName))
        {
            return plugin.DownloadUrl;
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
            var release = await updateService.GetReleaseByTagAsync(plugin.ReleaseTag, cancellationToken);
            var asset = release?.Assets.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, plugin.ReleaseAssetName, StringComparison.OrdinalIgnoreCase));

            return asset?.BrowserDownloadUrl ?? plugin.DownloadUrl;
        }
        catch
        {
            return plugin.DownloadUrl;
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
