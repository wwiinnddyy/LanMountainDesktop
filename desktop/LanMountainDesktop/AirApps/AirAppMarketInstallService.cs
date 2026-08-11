using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Security.Cryptography;
using LanMountainDesktop.AirAppSdk;
using LanMountainDesktop.Services;

namespace LanMountainDesktop.Services.AirAppMarket;

internal sealed class AirAppMarketInstallService : IDisposable
{
    private readonly AirAppRuntimeService _runtime;
    private readonly HttpClient _httpClient;
    private readonly ResumableDownloadService _downloadService;
    private readonly AirAppMarketReleaseResolverService _releaseResolverService;
    private readonly PendingAirAppUpgradeService _pendingUpgradeService;
    private readonly ElevatedAirAppInstallService _elevatedInstallService = new();
    private readonly string _downloadsDirectory;
    private readonly Version? _hostVersion;

    public AirAppMarketInstallService(AirAppRuntimeService runtime, string dataDirectory)
    {
        _runtime = runtime;
        _downloadsDirectory = ResolveDownloadsDirectory(dataDirectory);
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(2)
        };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("LanMountainDesktop-AirAppMarketplace/1.0");
        _downloadService = new ResumableDownloadService(_httpClient);
        _releaseResolverService = new AirAppMarketReleaseResolverService(_httpClient);
        _pendingUpgradeService = new PendingAirAppUpgradeService(runtime.AirAppsDirectory);
        _hostVersion = typeof(App).Assembly.GetName().Version;
    }

    public async Task<AirAppMarketInstallResult> InstallAsync(
        AirAppMarketAirAppEntry plugin,
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
                "AirApp does not declare any package sources.");
        }

        AppLogger.Info(
            "AirAppMarket",
            $"Starting install. AirAppId='{plugin.Id}'; Version='{plugin.Version}'; Sources='{string.Join(", ", sources.Select(source => source.SourceKind.ToString()))}'.");

        var compatibilityError = ValidateCompatibility(plugin);
        if (!string.IsNullOrWhiteSpace(compatibilityError))
        {
            AppLogger.Warn("AirAppMarket", $"Compatibility check failed. AirAppId='{plugin.Id}'; Error='{compatibilityError}'.");
            return new AirAppMarketInstallResult(false, null, compatibilityError);
        }

        return await StageInstallOrUpgradeAsync(
            plugin,
            sources,
            IsAirAppInstalled(plugin.Id),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<AirAppMarketInstallResult> StageInstallOrUpgradeAsync(
        AirAppMarketAirAppEntry plugin,
        IReadOnlyList<AirAppMarketAirAppPackageSourceEntry> sources,
        bool isUpgrade,
        CancellationToken cancellationToken)
    {
        var canWriteAirAppsDirectory = AirAppInstallTargetAccess.CanWriteDirectory(_runtime.AirAppsDirectory);
        AppLogger.Info(
            "AirAppMarket",
            $"Detected {(isUpgrade ? "upgrade" : "new install")} scenario. Downloading package for {(canWriteAirAppsDirectory ? "deferred" : "elevated")} install. AirAppId='{plugin.Id}'; AirAppsDirectory='{_runtime.AirAppsDirectory}'; CanWriteAirAppsDirectory={canWriteAirAppsDirectory}.");

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
                if (!canWriteAirAppsDirectory)
                {
                    var elevatedResult = await _elevatedInstallService.InstallAsync(
                        downloadResult.PackagePath,
                        _runtime.AirAppsDirectory,
                        cancellationToken).ConfigureAwait(false);
                    if (!elevatedResult.Success)
                    {
                        sourceErrors.Add($"{source.SourceKind}: {elevatedResult.ErrorMessage ?? elevatedResult.Message ?? elevatedResult.Code ?? "Elevated install failed."}");
                        continue;
                    }

                    AppLogger.Info(
                        "AirAppMarket",
                        $"AirApp package installed through elevated installer. AirAppId='{manifest.Id}'; Version='{manifest.Version ?? plugin.Version}'; PackagePath='{downloadResult.PackagePath}'; IsUpgrade={isUpgrade}.");

                    return new AirAppMarketInstallResult(true, manifest, null, RestartRequired: true);
                }

                _pendingUpgradeService.AddPendingInstallOrUpgrade(
                    manifest.Id,
                    downloadResult.PackagePath,
                    manifest.Version ?? plugin.Version);

                AppLogger.Info(
                    "AirAppMarket",
                    $"AirApp package queued for next restart. AirAppId='{manifest.Id}'; Version='{manifest.Version ?? plugin.Version}'; PackagePath='{downloadResult.PackagePath}'; IsUpgrade={isUpgrade}.");

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

    private bool IsAirAppInstalled(string pluginId)
    {
        return _runtime.Catalog.Any(entry =>
            string.Equals(entry.Manifest.Id, pluginId, StringComparison.OrdinalIgnoreCase));
    }

    private string? ValidateCompatibility(AirAppMarketAirAppEntry plugin) =>
        AirAppMarketCompatibility.Validate(plugin, _hostVersion, AirAppSdkInfo.ApiVersion);

    private async Task<AirAppMarketAcquisitionResult> AcquirePackageAsync(
        AirAppMarketAirAppEntry plugin,
        AirAppMarketAirAppPackageSourceEntry source,
        string resolvedDownloadUrl,
        string attemptPath,
        CancellationToken cancellationToken)
    {
        if (AirAppMarketDefaults.TryResolveWorkspaceFile(resolvedDownloadUrl, out var localPackagePath))
        {
            if (source.SourceKind == AirAppPackageSourceKind.WorkspaceLocal)
            {
                AppLogger.Info(
                    "AirAppMarket",
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

        if (source.SourceKind == AirAppPackageSourceKind.WorkspaceLocal)
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
        AirAppMarketAirAppEntry plugin,
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
                "AirAppMarket",
                $"Package verification failed. AirAppId='{plugin.Id}'; Version='{plugin.Version}'; DownloadPath='{attemptPath}'; ExpectedSize='{plugin.PackageSizeBytes}'; ActualSize='{actualSize}'.");
            return new AirAppMarketVerificationResult(
                false,
                $"Package verification failed. Expected size {plugin.PackageSizeBytes}, actual size {actualSize}.");
        }

        if (!string.IsNullOrWhiteSpace(plugin.Sha256) &&
            !string.Equals(actualHash, plugin.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            AppLogger.Error(
                "AirAppMarket",
                $"Package hash verification failed. AirAppId='{plugin.Id}'; Version='{plugin.Version}'; DownloadPath='{attemptPath}'; ExpectedHash='{plugin.Sha256}'; ActualHash='{actualHash}'.");
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

    private static string ResolveDownloadsDirectory(string dataDirectory)
    {
        var preferred = Path.Combine(dataDirectory, "downloads");
        if (AirAppInstallTargetAccess.CanWriteDirectory(preferred))
        {
            return preferred;
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var fallbackRoot = string.IsNullOrWhiteSpace(localAppData)
            ? Path.GetTempPath()
            : Path.Combine(localAppData, "LanMountainDesktop");
        return Path.Combine(fallbackRoot, "AirAppMarket", "downloads");
    }

    private async Task<DownloadPackageResult> DownloadPackageAsync(
        AirAppMarketAirAppEntry plugin,
        AirAppMarketAirAppPackageSourceEntry source,
        CancellationToken cancellationToken)
    {
        var packagePath = Path.Combine(
            _downloadsDirectory,
            $"{SanitizeFileName(plugin.Id)}-{SanitizeFileName(plugin.Version)}-{SanitizeFileName(source.SourceKind.ToString())}-{Guid.NewGuid():N}.laapp");

        try
        {
            var resolvedDownloadUrl = await _releaseResolverService.ResolveDownloadUrlAsync(plugin, source, cancellationToken).ConfigureAwait(false);
            AppLogger.Info(
                "AirAppMarket",
                $"Downloading package for deferred plugin install. AirAppId='{plugin.Id}'; Source='{resolvedDownloadUrl}'.");

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

    private static AirAppManifest ReadManifestFromPackage(string packagePath)
    {
        using var archive = System.IO.Compression.ZipFile.OpenRead(packagePath);
        var entries = archive.Entries
            .Where(entry => string.Equals(entry.Name, AirAppSdkInfo.ManifestFileName, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (entries.Length == 0)
        {
            throw new InvalidOperationException($"AirApp package '{packagePath}' does not contain '{AirAppSdkInfo.ManifestFileName}'.");
        }

        if (entries.Length > 1)
        {
            throw new InvalidOperationException($"AirApp package '{packagePath}' contains multiple '{AirAppSdkInfo.ManifestFileName}' files.");
        }

        using var stream = entries[0].Open();
        return AirAppManifest.Load(stream, $"{packagePath}!/{entries[0].FullName}");
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

internal static class AirAppMarketCompatibility
{
    public static string? Validate(
        AirAppMarketAirAppEntry plugin,
        Version? hostVersion,
        string? hostApiVersion)
    {
        ArgumentNullException.ThrowIfNull(plugin);

        if (hostVersion is not null && !string.IsNullOrWhiteSpace(plugin.MinHostVersion))
        {
            if (!AirAppMarketIndexDocument.TryParseVersion(plugin.MinHostVersion, out var minHostVersion) ||
                minHostVersion is null)
            {
                return $"AirApp '{plugin.Id}' declares invalid minimum host version '{plugin.MinHostVersion}'.";
            }

            if (hostVersion < minHostVersion)
            {
                return $"AirApp '{plugin.Id}' requires host version {plugin.MinHostVersion} or newer. Current host version is {hostVersion}.";
            }
        }

        if (string.IsNullOrWhiteSpace(plugin.ApiVersion))
        {
            return null;
        }

        if (!AirAppMarketIndexDocument.TryParseVersion(plugin.ApiVersion, out var pluginApiVersion) ||
            pluginApiVersion is null)
        {
            return $"AirApp '{plugin.Id}' declares invalid API version '{plugin.ApiVersion}'.";
        }

        if (string.IsNullOrWhiteSpace(hostApiVersion) ||
            !AirAppMarketIndexDocument.TryParseVersion(hostApiVersion, out var hostApiVersionParsed) ||
            hostApiVersionParsed is null)
        {
            AppLogger.Warn(
                "AirAppMarket",
                $"Host API version '{hostApiVersion ?? string.Empty}' could not be parsed. Skipping API version check.");
            return null;
        }

        return pluginApiVersion.Major != hostApiVersionParsed.Major
            ? $"AirApp '{plugin.Id}' uses incompatible API version {plugin.ApiVersion}. Host API version is {hostApiVersion}. Major version must match."
            : null;
    }
}
