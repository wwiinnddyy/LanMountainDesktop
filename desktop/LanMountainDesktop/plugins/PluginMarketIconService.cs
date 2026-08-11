using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using LanMountainDesktop.Services.Settings;

namespace LanMountainDesktop.Services.PluginMarket;

/// <summary>
/// Loads plugin icons from the local workspace, the on-disk asset cache, or the network,
/// writing successful network fetches back into the cache so subsequent loads are offline-friendly.
/// </summary>
public sealed class AirAppMarketIconService : IDisposable
{
    private readonly PluginMarketAssetCacheService? _cache;
    private readonly HttpClient _httpClient;

    public AirAppMarketIconService()
        : this(cache: null)
    {
    }

    public AirAppMarketIconService(PluginMarketAssetCacheService? cache)
    {
        _cache = cache;
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(20)
        };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("LanMountainDesktop-PluginMarketplace/1.0");
    }

    public async Task<Bitmap> LoadAsync(
        PluginCatalogItemInfo plugin,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plugin);

        if (AirAppMarketDefaults.TryResolveWorkspaceFile(plugin.IconUrl, out var localIconPath))
        {
            return new Bitmap(localIconPath);
        }

        if (_cache is not null &&
            _cache.TryGetIcon(plugin.Id, plugin.IconUrl, plugin.Version) is { } cachedIconPath)
        {
            try
            {
                return new Bitmap(cachedIconPath);
            }
            catch
            {
                // Stale or corrupt cache entry — fall through to a fresh fetch.
            }
        }

        using var response = await _httpClient.GetAsync(plugin.IconUrl, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var networkStream = await response.Content.ReadAsStreamAsync(cancellationToken);

        if (_cache is not null)
        {
            using var cachedCopy = new MemoryStream();
            await networkStream.CopyToAsync(cachedCopy, cancellationToken);
            cachedCopy.Position = 0;
            using var storeCopy = new MemoryStream(cachedCopy.ToArray());
            await _cache.StoreIconAsync(plugin.Id, plugin.IconUrl, plugin.Version, storeCopy, cancellationToken);

            cachedCopy.Position = 0;
            return new Bitmap(cachedCopy);
        }

        using var memory = new MemoryStream();
        await networkStream.CopyToAsync(memory, cancellationToken);
        memory.Position = 0;
        return new Bitmap(memory);
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
