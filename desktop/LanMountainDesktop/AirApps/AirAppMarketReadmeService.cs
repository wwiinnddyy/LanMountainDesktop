using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using LanMountainDesktop.Services.Settings;

namespace LanMountainDesktop.Services.AirAppMarket;

/// <summary>
/// Loads AirApp README markdown from the local workspace, the on-disk asset cache, or the network,
/// writing successful network fetches back into the cache so subsequent loads are offline-friendly.
/// </summary>
public sealed class AirAppMarketReadmeService : IDisposable
{
    private readonly AirAppMarketAssetCacheService? _cache;
    private readonly HttpClient _httpClient;

    public AirAppMarketReadmeService()
        : this(cache: null)
    {
    }

    public AirAppMarketReadmeService(AirAppMarketAssetCacheService? cache)
    {
        _cache = cache;
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(20)
        };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(HttpUserAgents.AirAppMarketplace);
    }

    public async Task<string> LoadAsync(
        AirAppCatalogItemInfo airApp,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(airApp);

        if (AirAppMarketDefaults.TryResolveWorkspaceFile(airApp.ReadmeUrl, out var localReadmePath))
        {
            return await File.ReadAllTextAsync(localReadmePath, cancellationToken);
        }

        if (_cache is not null &&
            _cache.TryGetReadme(airApp.Id, airApp.ReadmeUrl, airApp.Version) is { } cachedReadmePath)
        {
            try
            {
                return await File.ReadAllTextAsync(cachedReadmePath, cancellationToken);
            }
            catch
            {
                // Stale cache entry — fall through to a fresh fetch and overwrite it.
            }
        }

        using var response = await _httpClient.GetAsync(airApp.ReadmeUrl, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var networkStream = await response.Content.ReadAsStreamAsync(cancellationToken);

        if (_cache is not null)
        {
            using var cachedCopy = new MemoryStream();
            await networkStream.CopyToAsync(cachedCopy, cancellationToken);
            cachedCopy.Position = 0;
            using var storeCopy = new MemoryStream(cachedCopy.ToArray());
            await _cache.StoreReadmeAsync(airApp.Id, airApp.ReadmeUrl, airApp.Version, storeCopy, cancellationToken);

            cachedCopy.Position = 0;
            using var reader = new StreamReader(cachedCopy);
            return await reader.ReadToEndAsync(cancellationToken);
        }

        using var directReader = new StreamReader(networkStream);
        return await directReader.ReadToEndAsync(cancellationToken);
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
