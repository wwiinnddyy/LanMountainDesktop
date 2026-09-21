using System;
using LanMountainDesktop.Shared.Data;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Security.Cryptography;
using LanMountainDesktop.AirApps;
using LanMountainDesktop.AirAppSdk;
using LanMountainDesktop.Services;
using LanMountainDesktop.Shared.IO;

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
        AirAppMarketAirAppEntry airApp,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(airApp);

        Directory.CreateDirectory(_downloadsDirectory);
        var sources = airApp.GetPackageSourcesInInstallOrder();
        if (sources.Count == 0)
        {
            return new AirAppMarketInstallResult(
                false,
                null,
                "AirApp does not declare any package sources.");
        }

        AppLogger.Info(
            "AirAppMarket",
            $"Starting install. AirAppId='{airApp.Id}'; Version='{airApp.Version}'; Sources='{string.Join(", ", sources.Select(source => source.SourceKind.ToString()))}'.");

        var compatibilityError = ValidateCompatibility(airApp);
        if (!string.IsNullOrWhiteSpace(compatibilityError))
        {
            AppLogger.Warn("AirAppMarket", $"Compatibility check failed. AirAppId='{airApp.Id}'; Error='{compatibilityError}'.");
            return new AirAppMarketInstallResult(false, null, compatibilityError);
        }

        return await StageInstallOrUpgradeAsync(
            airApp,
            sources,
            IsAirAppInstalled(airApp.Id),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<AirAppMarketInstallResult> StageInstallOrUpgradeAsync(
        AirAppMarketAirAppEntry airApp,
        IReadOnlyList<AirAppMarketAirAppPackageSourceEntry> sources,
        bool isUpgrade,
        CancellationToken cancellationToken)
    {
        var canWriteAirAppsDirectory = AirAppInstallTargetAccess.CanWriteDirectory(_runtime.AirAppsDirectory);
        AppLogger.Info(
            "AirAppMarket",
            $"Detected {(isUpgrade ? "upgrade" : "new install")} scenario. Downloading package for {(canWriteAirAppsDirectory ? "deferred" : "elevated")} install. AirAppId='{airApp.Id}'; AirAppsDirectory='{_runtime.AirAppsDirectory}'; CanWriteAirAppsDirectory={canWriteAirAppsDirectory}.");

        var sourceErrors = new List<string>();
        foreach (var source in sources)
        {
            var downloadResult = await DownloadPackageAsync(airApp, source, cancellationToken).ConfigureAwait(false);
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
                var manifest = AirAppPackageReader.ReadManifest(downloadResult.PackagePath);
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
                        $"AirApp package installed through elevated installer. AirAppId='{manifest.Id}'; Version='{manifest.Version ?? airApp.Version}'; PackagePath='{downloadResult.PackagePath}'; IsUpgrade={isUpgrade}.");

                    return new AirAppMarketInstallResult(true, manifest, null, RestartRequired: true);
                }

                _pendingUpgradeService.AddPendingInstallOrUpgrade(
                    manifest.Id,
                    downloadResult.PackagePath,
                    manifest.Version ?? airApp.Version);

                AppLogger.Info(
                    "AirAppMarket",
                    $"AirApp package queued for next restart. AirAppId='{manifest.Id}'; Version='{manifest.Version ?? airApp.Version}'; PackagePath='{downloadResult.PackagePath}'; IsUpgrade={isUpgrade}.");

                return new AirAppMarketInstallResult(true, manifest, null, RestartRequired: true);
            }
            catch (Exception ex)
            {
                TryDeleteFile(downloadResult.PackagePath);
                sourceErrors.Add($"{source.SourceKind}: {ex.Message}");
            }
        }

        var combinedMessage = sourceErrors.Count == 0
            ? $"Failed to stage airApp '{airApp.Id}' from all available package sources."
            : $"Failed to stage airApp '{airApp.Id}' from all available package sources. {string.Join(" ", sourceErrors)}";
        return new AirAppMarketInstallResult(false, null, combinedMessage);
    }

    private bool IsAirAppInstalled(string airAppId)
    {
        return _runtime.Catalog.Any(entry =>
            string.Equals(entry.Manifest.Id, airAppId, StringComparison.OrdinalIgnoreCase));
    }

    private string? ValidateCompatibility(AirAppMarketAirAppEntry airApp) =>
        AirAppMarketCompatibility.Validate(airApp, _hostVersion, AirAppSdkInfo.ApiVersion);

    private async Task<AirAppMarketAcquisitionResult> AcquirePackageAsync(
        AirAppMarketAirAppEntry airApp,
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
                    $"Copying workspace package for '{airApp.Id}' from '{localPackagePath}' to '{attemptPath}'.");
            }

            var localCopyResult = await _downloadService.DownloadAsync(
                localPackagePath,
                attemptPath,
                new DownloadOptions(ExpectedSizeBytes: airApp.PackageSizeBytes > 0 ? airApp.PackageSizeBytes : null),
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
            new DownloadOptions(ExpectedSizeBytes: airApp.PackageSizeBytes > 0 ? airApp.PackageSizeBytes : null),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!downloadResult.Success)
        {
            return new AirAppMarketAcquisitionResult(false, downloadResult.ErrorMessage);
        }

        return new AirAppMarketAcquisitionResult(true, null);
    }

    private async Task<AirAppMarketVerificationResult> VerifyPackageAsync(
        AirAppMarketAirAppEntry airApp,
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

        if (airApp.PackageSizeBytes > 0 && actualSize != airApp.PackageSizeBytes)
        {
            AppLogger.Error(
                "AirAppMarket",
                $"Package verification failed. AirAppId='{airApp.Id}'; Version='{airApp.Version}'; DownloadPath='{attemptPath}'; ExpectedSize='{airApp.PackageSizeBytes}'; ActualSize='{actualSize}'.");
            return new AirAppMarketVerificationResult(
                false,
                $"Package verification failed. Expected size {airApp.PackageSizeBytes}, actual size {actualSize}.");
        }

        if (!string.IsNullOrWhiteSpace(airApp.Sha256) &&
            !string.Equals(actualHash, airApp.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            AppLogger.Error(
                "AirAppMarket",
                $"Package hash verification failed. AirAppId='{airApp.Id}'; Version='{airApp.Version}'; DownloadPath='{attemptPath}'; ExpectedHash='{airApp.Sha256}'; ActualHash='{actualHash}'.");
            return new AirAppMarketVerificationResult(
                false,
                $"Package verification failed. Expected SHA-256 {airApp.Sha256}, actual {actualHash}.");
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
            : Path.Combine(localAppData, UserDataRoot.FolderName);
        return Path.Combine(fallbackRoot, "AirAppMarket", "downloads");
    }

    private async Task<DownloadPackageResult> DownloadPackageAsync(
        AirAppMarketAirAppEntry airApp,
        AirAppMarketAirAppPackageSourceEntry source,
        CancellationToken cancellationToken)
    {
        var packagePath = Path.Combine(
            _downloadsDirectory,
            $"{SanitizeFileName(airApp.Id)}-{SanitizeFileName(airApp.Version)}-{SanitizeFileName(source.SourceKind.ToString())}-{Guid.NewGuid():N}.laapp");

        try
        {
            var resolvedDownloadUrl = await _releaseResolverService.ResolveDownloadUrlAsync(airApp, source, cancellationToken).ConfigureAwait(false);
            AppLogger.Info(
                "AirAppMarket",
                $"Downloading package for deferred airApp install. AirAppId='{airApp.Id}'; Source='{resolvedDownloadUrl}'.");

            var acquireResult = await AcquirePackageAsync(airApp, source, resolvedDownloadUrl, packagePath, cancellationToken).ConfigureAwait(false);
            if (!acquireResult.Success)
            {
                TryDeleteFile(packagePath);
                return new DownloadPackageResult(false, null, acquireResult.ErrorMessage);
            }

            var verificationResult = await VerifyPackageAsync(airApp, packagePath, cancellationToken).ConfigureAwait(false);
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
        AirAppMarketAirAppEntry airApp,
        Version? hostVersion,
        string? hostApiVersion)
    {
        ArgumentNullException.ThrowIfNull(airApp);

        if (hostVersion is not null && !string.IsNullOrWhiteSpace(airApp.MinHostVersion))
        {
            if (!AirAppMarketIndexDocument.TryParseVersion(airApp.MinHostVersion, out var minHostVersion) ||
                minHostVersion is null)
            {
                return $"AirApp '{airApp.Id}' declares invalid minimum host version '{airApp.MinHostVersion}'.";
            }

            if (hostVersion < minHostVersion)
            {
                return $"AirApp '{airApp.Id}' requires host version {airApp.MinHostVersion} or newer. Current host version is {hostVersion}.";
            }
        }

        if (string.IsNullOrWhiteSpace(airApp.ApiVersion))
        {
            return null;
        }

        if (!AirAppMarketIndexDocument.TryParseVersion(airApp.ApiVersion, out var airAppApiVersion) ||
            airAppApiVersion is null)
        {
            return $"AirApp '{airApp.Id}' declares invalid API version '{airApp.ApiVersion}'.";
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

        return airAppApiVersion.Major != hostApiVersionParsed.Major
            ? $"AirApp '{airApp.Id}' uses incompatible API version {airApp.ApiVersion}. Host API version is {hostApiVersion}. Major version must match."
            : null;
    }
}
