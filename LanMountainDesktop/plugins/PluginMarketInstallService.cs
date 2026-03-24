using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Security.Cryptography;
using LanMountainDesktop.PluginSdk;
using LanMountainDesktop.Services;

namespace LanMountainDesktop.Services.PluginMarket;

internal sealed class AirAppMarketInstallService : IDisposable
{
    private const string HelperExecutableName = "LanMountainDesktop.PluginsInstallHelper.exe";

    private readonly PluginRuntimeService _runtime;
    private readonly PluginsInstallHelperClient _helperClient = new();
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

        if (OperatingSystem.IsWindows())
        {
            var helperPath = ResolveHelperPath();
            if (!File.Exists(helperPath))
            {
                return new AirAppMarketInstallResult(
                    false,
                    null,
                    $"Plugins install helper was not found at '{helperPath}'.");
            }
        }

        Directory.CreateDirectory(_downloadsDirectory);
        var sources = plugin.GetPackageSourcesInInstallOrder();
        if (sources.Count == 0)
        {
            return new AirAppMarketInstallResult(
                false,
                null,
                "Plugin does not declare any package sources.");
        }

        AppLogger.Info(
            "PluginMarket",
            $"Starting install. PluginId='{plugin.Id}'; Version='{plugin.Version}'; Sources='{string.Join(", ", sources.Select(source => source.SourceKind.ToString()))}'.");

        var sourceErrors = new List<string>();
        foreach (var source in sources)
        {
            var attemptResult = await TryInstallFromSourceAsync(plugin, source, cancellationToken).ConfigureAwait(false);
            if (attemptResult.Success)
            {
                return new AirAppMarketInstallResult(true, attemptResult.Manifest, null);
            }

            if (attemptResult.Fatal)
            {
                return new AirAppMarketInstallResult(false, null, attemptResult.ErrorMessage);
            }

            if (!string.IsNullOrWhiteSpace(attemptResult.ErrorMessage))
            {
                sourceErrors.Add($"{source.SourceKind}: {attemptResult.ErrorMessage}");
            }
        }

        var combinedMessage = sourceErrors.Count == 0
            ? $"Failed to install plugin '{plugin.Id}' from all available package sources."
            : $"Failed to install plugin '{plugin.Id}' from all available package sources. {string.Join(" ", sourceErrors)}";
        return new AirAppMarketInstallResult(false, null, combinedMessage);
    }

    private async Task<AirAppMarketInstallAttemptResult> TryInstallFromSourceAsync(
        AirAppMarketPluginEntry plugin,
        AirAppMarketPluginPackageSourceEntry source,
        CancellationToken cancellationToken = default)
    {
        var attemptPath = Path.Combine(
            _downloadsDirectory,
            $"{SanitizeFileName(plugin.Id)}-{SanitizeFileName(plugin.Version)}-{SanitizeFileName(source.SourceKind.ToString())}-{Guid.NewGuid():N}.laapp");

        try
        {
            var resolvedDownloadUrl = await _releaseResolverService.ResolveDownloadUrlAsync(plugin, source, cancellationToken).ConfigureAwait(false);
            AppLogger.Warn(
                "PluginMarket",
                $"Resolved package source for '{plugin.Id}' to '{resolvedDownloadUrl}' using '{source.SourceKind}'.");

            var acquireResult = await AcquirePackageAsync(plugin, source, resolvedDownloadUrl, attemptPath, cancellationToken).ConfigureAwait(false);
            if (!acquireResult.Success)
            {
                TryDeleteFile(attemptPath);
                return new AirAppMarketInstallAttemptResult(false, false, null, acquireResult.ErrorMessage);
            }

            var verificationResult = await VerifyPackageAsync(plugin, attemptPath, cancellationToken).ConfigureAwait(false);
            if (!verificationResult.Success)
            {
                TryDeleteFile(attemptPath);
                return new AirAppMarketInstallAttemptResult(false, false, null, verificationResult.ErrorMessage);
            }

            PluginManifest manifest;
            if (OperatingSystem.IsWindows())
            {
                var helperResult = await _helperClient.InstallPackageAsync(
                    attemptPath,
                    _runtime.PluginsDirectory,
                    cancellationToken).ConfigureAwait(false);
                if (!helperResult.Success || string.IsNullOrWhiteSpace(helperResult.InstalledPackagePath))
                {
                    var helperMessage = helperResult.ErrorMessage ?? "Plugins install helper failed.";
                    AppLogger.Error(
                        "PluginMarket",
                        $"Windows install helper failed for plugin '{plugin.Id}' from source '{source.SourceKind}'. Message='{helperMessage}'.");
                    return new AirAppMarketInstallAttemptResult(false, true, null, helperMessage);
                }

                manifest = _runtime.RegisterInstalledPluginPackage(helperResult.InstalledPackagePath);
            }
            else
            {
                manifest = _runtime.InstallPluginPackage(attemptPath);
            }

            AppLogger.Info(
                "PluginMarket",
                $"Install staged successfully. PluginId='{manifest.Id}'; InstalledName='{manifest.Name}'; PackagePath='{attemptPath}'; SourceKind='{source.SourceKind}'.");
            return new AirAppMarketInstallAttemptResult(true, true, manifest, null);
        }
        catch (OperationCanceledException)
        {
            AppLogger.Warn(
                "PluginMarket",
                $"Install canceled. PluginId='{plugin.Id}'; Version='{plugin.Version}'; SourceKind='{source.SourceKind}'; DownloadPath='{attemptPath}'.");
            throw;
        }
        catch (Exception ex)
        {
            AppLogger.Error(
                "PluginMarket",
                $"Install attempt failed. PluginId='{plugin.Id}'; Version='{plugin.Version}'; SourceKind='{source.SourceKind}'; DownloadPath='{attemptPath}'.",
                ex);
            TryDeleteFile(attemptPath);
            return new AirAppMarketInstallAttemptResult(false, false, null, ex.Message);
        }
    }

    private async Task<AirAppMarketAcquisitionResult> AcquirePackageAsync(
        AirAppMarketPluginEntry plugin,
        AirAppMarketPluginPackageSourceEntry source,
        string resolvedDownloadUrl,
        string attemptPath,
        CancellationToken cancellationToken)
    {
        if (AirAppMarketDefaults.TryResolveWorkspaceFile(resolvedDownloadUrl, out var localPackagePath))
        {
            if (source.SourceKind == PluginPackageSourceKind.WorkspaceLocal)
            {
                AppLogger.Info(
                    "PluginMarket",
                    $"Copying workspace package for '{plugin.Id}' from '{localPackagePath}' to '{attemptPath}'.");
            }

            var localCopyResult = await _downloadService.DownloadAsync(
                localPackagePath,
                attemptPath,
                new DownloadOptions(ExpectedSizeBytes: plugin.PackageSizeBytes),
                cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!localCopyResult.Success)
            {
                return new AirAppMarketAcquisitionResult(false, localCopyResult.ErrorMessage);
            }

            return new AirAppMarketAcquisitionResult(true, null);
        }

        if (source.SourceKind == PluginPackageSourceKind.WorkspaceLocal)
        {
            return new AirAppMarketAcquisitionResult(
                false,
                $"Workspace package source '{source.Url}' could not be resolved to a local file.");
        }

        var downloadResult = await _downloadService.DownloadAsync(
            resolvedDownloadUrl,
            attemptPath,
            new DownloadOptions(ExpectedSizeBytes: plugin.PackageSizeBytes),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!downloadResult.Success)
        {
            return new AirAppMarketAcquisitionResult(false, downloadResult.ErrorMessage);
        }

        return new AirAppMarketAcquisitionResult(true, null);
    }

    private async Task<AirAppMarketVerificationResult> VerifyPackageAsync(
        AirAppMarketPluginEntry plugin,
        string attemptPath,
        CancellationToken cancellationToken)
    {
        var actualSize = new FileInfo(attemptPath).Length;
        string actualHash;
        await using (var hashStream = File.OpenRead(attemptPath))
        {
            var hashBytes = await SHA256.HashDataAsync(hashStream, cancellationToken).ConfigureAwait(false);
            actualHash = Convert.ToHexString(hashBytes).ToLowerInvariant();
        }

        if (actualSize != plugin.PackageSizeBytes || !string.Equals(actualHash, plugin.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            AppLogger.Error(
                "PluginMarket",
                $"Package verification failed. PluginId='{plugin.Id}'; Version='{plugin.Version}'; DownloadPath='{attemptPath}'; ExpectedHash='{plugin.Sha256}'; ActualHash='{actualHash}'; ExpectedSize='{plugin.PackageSizeBytes}'; ActualSize='{actualSize}'.");
            return new AirAppMarketVerificationResult(
                false,
                $"Package verification failed. Expected SHA-256 {plugin.Sha256}, actual {actualHash}. Expected size {plugin.PackageSizeBytes}, actual size {actualSize}.");
        }

        return new AirAppMarketVerificationResult(true, null);
    }

    private static string ResolveHelperPath()
    {
        return Path.Combine(AppContext.BaseDirectory, "PluginsInstallHelper", HelperExecutableName);
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Ignore cleanup failures for temporary install artifacts.
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

    private sealed record AirAppMarketInstallAttemptResult(
        bool Success,
        bool Fatal,
        PluginManifest? Manifest,
        string? ErrorMessage);

    private sealed record AirAppMarketAcquisitionResult(
        bool Success,
        string? ErrorMessage);

    private sealed record AirAppMarketVerificationResult(
        bool Success,
        string? ErrorMessage);
}
