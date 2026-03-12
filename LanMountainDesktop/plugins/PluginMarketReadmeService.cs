using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace LanMountainDesktop.Services.PluginMarket;

internal sealed class AirAppMarketReadmeService : IDisposable
{
    private readonly HttpClient _httpClient;

    public AirAppMarketReadmeService()
    {
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(20)
        };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("LanMountainDesktop-PluginMarketplace/1.0");
    }

    public async Task<string> LoadAsync(
        AirAppMarketPluginEntry plugin,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plugin);

        if (AirAppMarketDefaults.TryResolveWorkspaceFile(plugin.ReadmeUrl, out var localReadmePath))
        {
            return await File.ReadAllTextAsync(localReadmePath, cancellationToken);
        }

        using var response = await _httpClient.GetAsync(plugin.ReadmeUrl, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
