using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Security.Cryptography;
using LanMountainDesktop.PluginSdk;
using LanMountainDesktop.Services;

namespace LanMountainDesktop.Views.SettingsPages;

internal sealed class AirAppMarketInstallService : IDisposable
{
    private readonly PluginRuntimeService _runtime;
    private readonly HttpClient _httpClient;
    private readonly ResumableDownloadService _downloadService;
    private readonly AirAppMarketReleaseResolverService _releaseResolverService;
    private readonly string _downloadsDirectory;

    public AirAppMarketInstallService(PluginRuntimeService runtime, string dataDirectory)
    {
        _runtime = runtime;
        _downloadsDirectory = Path.Combine(dataDirectory, "downloads");
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(2)
        };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("LanMountainDesktop-PluginMarketplace/1.0");
        _downloadService = new ResumableDownloadService(_httpClient);
        _releaseResolverService = new AirAppMarketReleaseResolverService(_httpClient);
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
            var resolvedDownloadUrl = await _releaseResolverService.ResolveDownloadUrlAsync(plugin, cancellationToken);

            if (AirAppMarketDefaults.TryResolveWorkspaceFile(resolvedDownloadUrl, out var localPackagePath))
            {
                var localCopyResult = await _downloadService.DownloadAsync(
                    localPackagePath,
                    downloadPath,
                    new DownloadOptions(ExpectedSizeBytes: plugin.PackageSizeBytes),
                    cancellationToken: cancellationToken);
                if (!localCopyResult.Success)
                {
                    return new AirAppMarketInstallResult(false, null, localCopyResult.ErrorMessage);
                }
            }
            else
            {
                var downloadResult = await _downloadService.DownloadAsync(
                    resolvedDownloadUrl,
                    downloadPath,
                    new DownloadOptions(ExpectedSizeBytes: plugin.PackageSizeBytes),
                    cancellationToken: cancellationToken);
                if (!downloadResult.Success)
                {
                    return new AirAppMarketInstallResult(false, null, downloadResult.ErrorMessage);
                }
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

            var manifest = _runtime.InstallPluginPackage(downloadPath);
            return new AirAppMarketInstallResult(true, manifest, null);
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
