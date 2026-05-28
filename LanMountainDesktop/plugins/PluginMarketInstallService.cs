using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Security.Cryptography;
using LanMountainDesktop.PluginSdk;
using LanMountainDesktop.Services;

namespace LanMountainDesktop.Services.PluginMarket;

internal sealed class AirAppMarketInstallService : IDisposable
{
    private readonly PluginRuntimeService _runtime;
    private readonly HttpClient _httpClient;
    private readonly ResumableDownloadService _downloadService;
    private readonly AirAppMarketReleaseResolverService _releaseResolverService;
    private readonly PendingPluginUpgradeService _pendingUpgradeService;
    private readonly string _downloadsDirectory;
    private readonly Version? _hostVersion;

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
        _pendingUpgradeService = new PendingPluginUpgradeService(runtime.PluginsDirectory);
        _hostVersion = typeof(App).Assembly.GetName().Version;
    }

    public async Task<AirAppMarketInstallResult> InstallAsync(
        AirAppMarketPluginEntry plugin,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plugin);

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

        var compatibilityError = ValidateCompatibility(plugin);
        if (!string.IsNullOrWhiteSpace(compatibilityError))
        {
            AppLogger.Warn("PluginMarket", $"Compatibility check failed. PluginId='{plugin.Id}'; Error='{compatibilityError}'.");
            return new AirAppMarketInstallResult(false, null, compatibilityError);
        }

        return await StageInstallOrUpgradeAsync(
            plugin,
            sources,
            IsPluginInstalled(plugin.Id),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<AirAppMarketInstallResult> StageInstallOrUpgradeAsync(
        AirAppMarketPluginEntry plugin,
        IReadOnlyList<AirAppMarketPluginPackageSourceEntry> sources,
        bool isUpgrade,
        CancellationToken cancellationToken)
    {
        AppLogger.Info(
            "PluginMarket",
            $"Detected {(isUpgrade ? "upgrade" : "new install")} scenario. Downloading package for deferred install. PluginId='{plugin.Id}'.");

        var sourceErrors = new List<string>();
        foreach (var source in sources)
        {
            var downloadResult = await DownloadPackageAsync(plugin, source, cancellationToken).ConfigureAwait(false);
            if (!downloadResult.Success || string.IsNullOrWhiteSpace(downloadResult.PackagePath))
            {
                if (!string.IsNullOrWhiteSpace(downloadResult.ErrorMessage))
                {
                    sourceErrors.Add($"{source.SourceKind}: {downloadResult.ErrorMessage}");
                }

                continue;
            }

            try
            {
                var manifest = ReadManifestFromPackage(downloadResult.PackagePath);
                _pendingUpgradeService.AddPendingInstallOrUpgrade(
                    manifest.Id,
                    downloadResult.PackagePath,
                    manifest.Version ?? plugin.Version);

                AppLogger.Info(
                    "PluginMarket",
                    $"Plugin package queued for next restart. PluginId='{manifest.Id}'; Version='{manifest.Version ?? plugin.Version}'; PackagePath='{downloadResult.PackagePath}'; IsUpgrade={isUpgrade}.");

                return new AirAppMarketInstallResult(true, manifest, null, RestartRequired: true);
            }
            catch (Exception ex)
            {
                TryDeleteFile(downloadResult.PackagePath);
                sourceErrors.Add($"{source.SourceKind}: {ex.Message}");
            }
        }

        var combinedMessage = sourceErrors.Count == 0
            ? $"Failed to stage plugin '{plugin.Id}' from all available package sources."
            : $"Failed to stage plugin '{plugin.Id}' from all available package sources. {string.Join(" ", sourceErrors)}";
        return new AirAppMarketInstallResult(false, null, combinedMessage);
    }

    private bool IsPluginInstalled(string pluginId)
    {
        return _runtime.Catalog.Any(entry =>
            string.Equals(entry.Manifest.Id, pluginId, StringComparison.OrdinalIgnoreCase));
    }

    private string? ValidateCompatibility(AirAppMarketPluginEntry plugin)
    {
        if (_hostVersion is null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(plugin.MinHostVersion))
        {
            if (!AirAppMarketIndexDocument.TryParseVersion(plugin.MinHostVersion, out var minHostVersion) ||
                minHostVersion is null)
            {
                return $"Plugin '{plugin.Id}' declares invalid minimum host version '{plugin.MinHostVersion}'.";
            }

            if (_hostVersion < minHostVersion)
            {
                return $"Plugin '{plugin.Id}' requires host version {plugin.MinHostVersion} or newer. Current host version is {_hostVersion}.";
            }
        }

        if (!string.IsNullOrWhiteSpace(plugin.ApiVersion))
        {
            if (!AirAppMarketIndexDocument.TryParseVersion(plugin.ApiVersion, out var pluginApiVersion) ||
                pluginApiVersion is null)
            {
                return $"Plugin '{plugin.Id}' declares invalid API version '{plugin.ApiVersion}'.";
            }

            var hostApiVersion = PluginSdkInfo.ApiVersion;
            if (hostApiVersion is not null)
            {
                if (!AirAppMarketIndexDocument.TryParseVersion(hostApiVersion, out var hostApiVersionParsed) ||
                    hostApiVersionParsed is null)
                {
                    AppLogger.Warn("PluginMarket", $"Host API version '{hostApiVersion}' could not be parsed. Skipping API version check.");
                }
                else if (pluginApiVersion.Major != hostApiVersionParsed.Major)
                {
                    return $"Plugin '{plugin.Id}' uses incompatible API version {plugin.ApiVersion}. Host API version is {hostApiVersion}. Major version must match.";
                }
            }
        }

        return null;
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
                new DownloadOptions(ExpectedSizeBytes: plugin.PackageSizeBytes > 0 ? plugin.PackageSizeBytes : null),
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
            new DownloadOptions(ExpectedSizeBytes: plugin.PackageSizeBytes > 0 ? plugin.PackageSizeBytes : null),
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

        if (plugin.PackageSizeBytes > 0 && actualSize != plugin.PackageSizeBytes)
        {
            AppLogger.Error(
                "PluginMarket",
                $"Package verification failed. PluginId='{plugin.Id}'; Version='{plugin.Version}'; DownloadPath='{attemptPath}'; ExpectedSize='{plugin.PackageSizeBytes}'; ActualSize='{actualSize}'.");
            return new AirAppMarketVerificationResult(
                false,
                $"Package verification failed. Expected size {plugin.PackageSizeBytes}, actual size {actualSize}.");
        }

        if (!string.IsNullOrWhiteSpace(plugin.Sha256) &&
            !string.Equals(actualHash, plugin.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            AppLogger.Error(
                "PluginMarket",
                $"Package hash verification failed. PluginId='{plugin.Id}'; Version='{plugin.Version}'; DownloadPath='{attemptPath}'; ExpectedHash='{plugin.Sha256}'; ActualHash='{actualHash}'.");
            return new AirAppMarketVerificationResult(
                false,
                $"Package verification failed. Expected SHA-256 {plugin.Sha256}, actual {actualHash}.");
        }

        return new AirAppMarketVerificationResult(true, null);
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

    private async Task<DownloadPackageResult> DownloadPackageAsync(
        AirAppMarketPluginEntry plugin,
        AirAppMarketPluginPackageSourceEntry source,
        CancellationToken cancellationToken)
    {
        var packagePath = Path.Combine(
            _downloadsDirectory,
            $"{SanitizeFileName(plugin.Id)}-{SanitizeFileName(plugin.Version)}-{SanitizeFileName(source.SourceKind.ToString())}-{Guid.NewGuid():N}.laapp");

        try
        {
            var resolvedDownloadUrl = await _releaseResolverService.ResolveDownloadUrlAsync(plugin, source, cancellationToken).ConfigureAwait(false);
            AppLogger.Info(
                "PluginMarket",
                $"Downloading package for deferred plugin install. PluginId='{plugin.Id}'; Source='{resolvedDownloadUrl}'.");

            var acquireResult = await AcquirePackageAsync(plugin, source, resolvedDownloadUrl, packagePath, cancellationToken).ConfigureAwait(false);
            if (!acquireResult.Success)
            {
                TryDeleteFile(packagePath);
                return new DownloadPackageResult(false, null, acquireResult.ErrorMessage);
            }

            var verificationResult = await VerifyPackageAsync(plugin, packagePath, cancellationToken).ConfigureAwait(false);
            if (!verificationResult.Success)
            {
                TryDeleteFile(packagePath);
                return new DownloadPackageResult(false, null, verificationResult.ErrorMessage);
            }

            return new DownloadPackageResult(true, packagePath, null);
        }
        catch (OperationCanceledException)
        {
            TryDeleteFile(packagePath);
            throw;
        }
        catch (Exception ex)
        {
            TryDeleteFile(packagePath);
            return new DownloadPackageResult(false, null, ex.Message);
        }
    }

    private static PluginManifest ReadManifestFromPackage(string packagePath)
    {
        using var archive = System.IO.Compression.ZipFile.OpenRead(packagePath);
        var entries = archive.Entries
            .Where(entry => string.Equals(entry.Name, "plugin.json", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (entries.Length == 0)
        {
            throw new InvalidOperationException($"Plugin package '{packagePath}' does not contain 'plugin.json'.");
        }

        if (entries.Length > 1)
        {
            throw new InvalidOperationException($"Plugin package '{packagePath}' contains multiple 'plugin.json' files.");
        }

        using var stream = entries[0].Open();
        return PluginManifest.Load(stream, $"{packagePath}!/{entries[0].FullName}");
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

    private sealed record AirAppMarketAcquisitionResult(
        bool Success,
        string? ErrorMessage);

    private sealed record AirAppMarketVerificationResult(
        bool Success,
        string? ErrorMessage);

    private sealed record DownloadPackageResult(
        bool Success,
        string? PackagePath,
        string? ErrorMessage);
}
