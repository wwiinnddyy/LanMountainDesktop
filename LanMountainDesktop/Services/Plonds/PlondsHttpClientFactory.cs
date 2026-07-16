using System.Net;
using System.Net.Http.Headers;

namespace LanMountainDesktop.Services.Plonds;

/// <summary>
/// Shared HTTP client for PLONDS manifest and package downloads.
/// CDN buckets (e.g. rains3) commonly reject anonymous clients without a User-Agent.
/// </summary>
internal static class PlondsHttpClientFactory
{
    public static HttpClient Create(TimeSpan? timeout = null)
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            AllowAutoRedirect = true,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        };

        var client = new HttpClient(handler)
        {
            Timeout = timeout ?? TimeSpan.FromMinutes(10)
        };

        client.DefaultRequestHeaders.UserAgent.Clear();
        client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("LanMountainDesktop", GetProductVersion()));
        client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("(+https://github.com/wwiinnddyy/LanMountainDesktop)"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));

        return client;
    }

    private static string GetProductVersion()
    {
        try
        {
            var version = Shared.Contracts.Launcher.AppVersionProvider.ResolveForCurrentProcess().Version;
            return string.IsNullOrWhiteSpace(version) ? "1.0" : version.Trim();
        }
        catch
        {
            return "1.0";
        }
    }
}
