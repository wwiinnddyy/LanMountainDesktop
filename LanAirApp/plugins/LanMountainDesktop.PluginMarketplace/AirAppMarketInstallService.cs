using System.Security.Cryptography;
using LanMountainDesktop.PluginSdk;

namespace LanMountainDesktop.PluginMarketplace;

internal sealed class AirAppMarketInstallService : IDisposable
{
    private readonly IPluginPackageManager _packageManager;
    private readonly HttpClient _httpClient;
    private readonly string _downloadsDirectory;

    public AirAppMarketInstallService(IPluginPackageManager packageManager, string dataDirectory)
    {
        _packageManager = packageManager;
        _downloadsDirectory = Path.Combine(dataDirectory, "downloads");
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(2)
        };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("LanMountainDesktop-PluginMarketplace/1.0");
    }

    public async Task<AirAppMarketInstallResult> InstallAsync(
        AirAppMarketPluginEntry plugin,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plugin);

        Directory.CreateDirectory(_downloadsDirectory);
        var downloadPath = Path.Combine(
            _downloadsDirectory,
            $"{SanitizeFileName(plugin.Id)}-{SanitizeFileName(plugin.Version)}.laapp");

        try
        {
            using var response = await _httpClient.GetAsync(
                plugin.DownloadUrl,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();

            await using (var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var destinationStream = File.Create(downloadPath))
            {
                await responseStream.CopyToAsync(destinationStream, cancellationToken);
            }

            await using var hashStream = File.OpenRead(downloadPath);
            var hashBytes = await SHA256.HashDataAsync(hashStream, cancellationToken);
            var actualHash = Convert.ToHexString(hashBytes).ToLowerInvariant();
            if (!string.Equals(actualHash, plugin.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(downloadPath);
                return new AirAppMarketInstallResult(
                    false,
                    null,
                    $"SHA-256 mismatch. Expected {plugin.Sha256}, actual {actualHash}.");
            }

            var installResult = _packageManager.InstallPackage(downloadPath);
            return new AirAppMarketInstallResult(true, installResult, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new AirAppMarketInstallResult(false, null, ex.Message);
        }
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    private static string SanitizeFileName(string value)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        return new string(value.Select(ch => invalidChars.Contains(ch) ? '_' : ch).ToArray());
    }
}
