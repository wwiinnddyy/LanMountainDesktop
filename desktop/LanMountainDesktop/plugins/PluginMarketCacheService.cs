using System;
using System.IO;

namespace LanMountainDesktop.Services.PluginMarket;

internal sealed class AirAppMarketCacheService
{
    private readonly string _cacheDirectory;

    public AirAppMarketCacheService(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        _cacheDirectory = Path.Combine(dataDirectory, "cache");
    }

    public string CacheFilePath => Path.Combine(_cacheDirectory, "index.json");

    public void SaveIndexJson(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        Directory.CreateDirectory(_cacheDirectory);
        File.WriteAllText(CacheFilePath, json);
    }

    public bool TryReadIndexJson(out string json)
    {
        try
        {
            if (!File.Exists(CacheFilePath))
            {
                json = string.Empty;
                return false;
            }

            json = File.ReadAllText(CacheFilePath);
            return !string.IsNullOrWhiteSpace(json);
        }
        catch
        {
            json = string.Empty;
            return false;
        }
    }
}
