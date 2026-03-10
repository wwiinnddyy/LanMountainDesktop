using System.Net.Http.Headers;

namespace LanMountainDesktop.PluginMarketplace;

internal sealed class AirAppMarketIndexService : IDisposable
{
    private readonly AirAppMarketCacheService _cacheService;
    private readonly HttpClient _httpClient;

    public AirAppMarketIndexService(AirAppMarketCacheService cacheService)
    {
        _cacheService = cacheService;
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(20)
        };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("LanMountainDesktop-PluginMarketplace/1.0");
        _httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<AirAppMarketLoadResult> LoadAsync(CancellationToken cancellationToken = default)
    {
        Exception? networkError = null;

        try
        {
            using var response = await _httpClient.GetAsync(
                AirAppMarketDefaults.DefaultIndexUrl,
                cancellationToken);
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            response.EnsureSuccessStatusCode();

            var document = AirAppMarketIndexDocument.Load(json, AirAppMarketDefaults.DefaultIndexUrl);
            _cacheService.SaveIndexJson(json);
            return new AirAppMarketLoadResult(true, document, AirAppMarketLoadSource.Network, null, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            networkError = ex;
        }

        if (_cacheService.TryReadIndexJson(out var cachedJson))
        {
            try
            {
                var cachedDocument = AirAppMarketIndexDocument.Load(cachedJson, _cacheService.CacheFilePath);
                return new AirAppMarketLoadResult(
                    true,
                    cachedDocument,
                    AirAppMarketLoadSource.Cache,
                    networkError?.Message,
                    null);
            }
            catch (Exception cacheEx)
            {
                return new AirAppMarketLoadResult(
                    false,
                    null,
                    null,
                    null,
                    $"{networkError?.Message ?? "Unknown network error"} | Cached index invalid: {cacheEx.Message}");
            }
        }

        return new AirAppMarketLoadResult(false, null, null, null, networkError?.Message ?? "Unknown network error");
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
