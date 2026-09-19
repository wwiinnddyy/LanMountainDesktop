using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using LanMountainDesktop.Services;

namespace LanMountainDesktop.Services.AirAppMarket;

internal sealed class AirAppMarketReleaseResolverService
{
    private readonly HttpClient _httpClient;

    public AirAppMarketReleaseResolverService(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public async Task<string> ResolveDownloadUrlAsync(
        AirAppMarketAirAppEntry airApp,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(airApp);

        var firstSource = airApp.GetPackageSourcesInInstallOrder().FirstOrDefault();
        if (firstSource is null)
        {
            return airApp.DownloadUrl;
        }

        return await ResolveDownloadUrlAsync(airApp, firstSource, cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> ResolveDownloadUrlAsync(
        AirAppMarketAirAppEntry airApp,
        AirAppMarketAirAppPackageSourceEntry source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(airApp);
        ArgumentNullException.ThrowIfNull(source);

        return source.SourceKind switch
        {
            AirAppPackageSourceKind.ReleaseAsset => await ResolveReleaseAssetDownloadUrlAsync(airApp, source, cancellationToken).ConfigureAwait(false),
            AirAppPackageSourceKind.RawFallback => source.Url,
            AirAppPackageSourceKind.WorkspaceLocal => source.Url,
            _ => source.Url
        };
    }

    private async Task<string> ResolveReleaseAssetDownloadUrlAsync(
        AirAppMarketAirAppEntry airApp,
        AirAppMarketAirAppPackageSourceEntry source,
        CancellationToken cancellationToken)
    {
        var sourceUrl = source.Url;
        if (!airApp.HasReleaseDownloadMetadata)
        {
            return sourceUrl;
        }

        if (!TryGetRepositoryIdentity(airApp, out var owner, out var repositoryName))
        {
            return sourceUrl;
        }

        var releaseDownloadUrl = AirAppMarketDefaults.BuildGitHubReleaseDownloadUrl(
            owner,
            repositoryName,
            airApp.ReleaseTag,
            airApp.ReleaseAssetName);

        if (AirAppMarketDefaults.TryResolveWorkspaceFile(releaseDownloadUrl, out _))
        {
            return releaseDownloadUrl;
        }

        try
        {
            using var updateService = new GitHubReleaseUpdateService(owner, repositoryName, _httpClient);
            var release = await updateService.GetReleaseByTagAsync(airApp.ReleaseTag, cancellationToken).ConfigureAwait(false);
            var asset = release?.Assets.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, airApp.ReleaseAssetName, StringComparison.OrdinalIgnoreCase));

            return asset?.BrowserDownloadUrl ?? releaseDownloadUrl;
        }
        catch
        {
            return releaseDownloadUrl;
        }
    }

    private static bool TryGetRepositoryIdentity(
        AirAppMarketAirAppEntry airApp,
        out string owner,
        out string repositoryName)
    {
        owner = string.Empty;
        repositoryName = string.Empty;

        return AirAppMarketDefaults.TryParseGitHubRepositoryUrl(airApp.RepositoryUrl, out owner, out repositoryName) ||
               AirAppMarketDefaults.TryParseGitHubRepositoryUrl(airApp.ProjectUrl, out owner, out repositoryName);
    }
}
