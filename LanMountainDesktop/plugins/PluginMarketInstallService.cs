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
    private readonly LauncherClient _launcherClient = new();
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

        var isUpgrade = IsPluginInstalled(plugin.Id);
        if (isUpgrade)
        {
            return await InstallUpgradeAsync(plugin, sources, cancellationToken).ConfigureAwait(false);
        }

        return await InstallNewAsync(plugin, sources, cancellationToken).ConfigureAwait(false);
    }

    private async Task<AirAppMarketInstallResult> InstallNewAsync(
        AirAppMarketPluginEntry plugin,
        IReadOnlyList<AirAppMarketPluginPackageSourceEntry> sources,
        CancellationToken cancellationToken)
    {
        if (OperatingSystem.IsWindows())
        {
            var launcherPath = LauncherPathResolver.ResolveLauncherExecutablePath();
            if (string.IsNullOrWhiteSpace(launcherPath) || !File.Exists(launcherPath))
            {
                return new AirAppMarketInstallResult(
                    false,
                    null,
                    "Launcher executable was not found. Expected it to be located in the application root directory (sibling to the app-* deployment folder).");
            }
        }

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

    private async Task<AirAppMarketInstallResult> InstallUpgradeAsync(
        AirAppMarketPluginEntry plugin,
        IReadOnlyList<AirAppMarketPluginPackageSourceEntry> sources,
        CancellationToken cancellationToken)
    {
        AppLogger.Info("PluginMarket", $"Detected upgrade scenario. Downloading package for deferred upgrade. PluginId='{plugin.Id}'.");

        foreach (var source in sources)
        {
            var downloadResult = await DownloadPackageAsync(plugin, source, cancellationToken).ConfigureAwait(false);
            if (downloadResult.Success && !string.IsNullOrWhiteSpace(downloadResult.PackagePath))
            {
                _pendingUpgradeService.AddPendingUpgrade(plugin.Id, downloadResult.PackagePath, plugin.Version);

                AppLogger.Info(
                    "PluginMarket",
                    $"Upgrade staged for next restart. PluginId='{plugin.Id}'; Version='{plugin.Version}'; PackagePath='{downloadResult.PackagePath}'.");

                var manifest = ReadManifestFromPackage(downloadResult.PackagePath);
                return new AirAppMarketInstallResult(true, manifest, null, RestartRequired: true);
            }
        }

        return new AirAppMarketInstallResult(
            false,
            null,
            $"Failed to download upgrade package for plugin '{plugin.Id}' from all available sources.");
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
                var helperResult = await _launcherClient.InstallPackageAsync(
                    attemptPath,
                    _runtime.PluginsDirectory,
                    cancellationToken).ConfigureAwait(false);
                if (!helperResult.Success || string.IsNullOrWhiteSpace(helperResult.InstalledPackagePath))
                {
                    var helperMessage = helperResult.ErrorMessage ?? "Launcher plugin install failed.";
                    AppLogger.Error(
                        "PluginMarket",
                        $"Windows launcher install failed for plugin '{plugin.Id}' from source '{source.SourceKind}'. " +
                        $"Code='{helperResult.Code}'; Message='{helperMessage}'.");
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
                $"Downloading upgrade package for '{plugin.Id}' from '{resolvedDownloadUrl}'.");

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

    private sealed record DownloadPackageResult(
        bool Success,
        string? PackagePath,
        string? ErrorMessage);
}
