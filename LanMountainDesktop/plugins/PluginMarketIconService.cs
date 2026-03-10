using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;

namespace LanMountainDesktop.Views.SettingsPages;

internal sealed class AirAppMarketIconService : IDisposable
{
    private readonly HttpClient _httpClient;

    public AirAppMarketIconService()
    {
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(20)
        };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("LanMountainDesktop-PluginMarketplace/1.0");
    }

    public async Task<Bitmap> LoadAsync(
        AirAppMarketPluginEntry plugin,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plugin);

        if (AirAppMarketDefaults.TryResolveWorkspaceFile(plugin.IconUrl, out var localIconPath))
        {
            return new Bitmap(localIconPath);
        }

        using var response = await _httpClient.GetAsync(plugin.IconUrl, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory, cancellationToken);
        memory.Position = 0;
        return new Bitmap(memory);
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
