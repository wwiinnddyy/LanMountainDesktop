using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace LanMountainDesktop.Views.SettingsPages;

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

        if (AirAppMarketDefaults.TryGetWorkspaceIndexPath() is { } localIndexPath)
        {
            try
            {
                var json = await File.ReadAllTextAsync(localIndexPath, cancellationToken);
                var document = AirAppMarketIndexDocument.Load(json, localIndexPath);
                _cacheService.SaveIndexJson(json);
                return new AirAppMarketLoadResult(
                    true,
                    document,
                    AirAppMarketLoadSource.Local,
                    localIndexPath,
                    null,
                    null);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                networkError = ex;
            }
        }

        try
        {
            using var response = await _httpClient.GetAsync(
                AirAppMarketDefaults.DefaultIndexUrl,
                cancellationToken);
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            response.EnsureSuccessStatusCode();

            var document = AirAppMarketIndexDocument.Load(json, AirAppMarketDefaults.DefaultIndexUrl);
            _cacheService.SaveIndexJson(json);
            return new AirAppMarketLoadResult(
                true,
                document,
                AirAppMarketLoadSource.Network,
                AirAppMarketDefaults.DefaultIndexUrl,
                null,
                null);
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
                    _cacheService.CacheFilePath,
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
                    null,
                    $"{networkError?.Message ?? "Unknown network error"} | Cached index invalid: {cacheEx.Message}");
            }
        }

        return new AirAppMarketLoadResult(
            false,
            null,
            null,
            null,
            null,
            networkError?.Message ?? "Unknown network error");
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
