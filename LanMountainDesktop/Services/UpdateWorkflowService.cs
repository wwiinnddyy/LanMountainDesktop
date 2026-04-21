using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LanMountainDesktop.PluginSdk;
using LanMountainDesktop.Services.Settings;

namespace LanMountainDesktop.Services;

public sealed record UpdatePendingInfo(
    string InstallerPath,
    string VersionText,
    DateTimeOffset? PublishedAt,
    string? Sha256 = null);

public sealed record UpdateVerifyResult(
    bool Success,
    bool HashMatched,
    string? ExpectedHash,
    string? ActualHash,
    string? ErrorMessage);

public sealed record UpdateInstallerLaunchResult(
    bool Success,
    bool UserCancelledElevation,
    string? ErrorMessage);

internal static class HostUpdateWorkflowServiceProvider
{
    private static readonly object Gate = new();
    private static UpdateWorkflowService? _instance;

    public static UpdateWorkflowService GetOrCreate()
    {
        lock (Gate)
        {
            return _instance ??= new UpdateWorkflowService(HostSettingsFacadeProvider.GetOrCreate());
        }
    }
}

public sealed class UpdateWorkflowService
{
    private readonly ISettingsFacadeService _settingsFacade;
    private readonly string _updatesDirectory;

    private const string LauncherDirectoryName = ".launcher";
    private const string UpdateDirectoryName = "update";
    private const string IncomingDirectoryName = "incoming";
    private const string IncomingObjectsDirectoryName = "objects";
    private const string SignedFileMapName = "files.json";
    private const string SignedFileMapSignatureName = "files.json.sig";
    private const string UpdateArchiveName = "update.zip";
    private const string PlondsFileMapName = "plonds-filemap.json";
    private const string PlondsFileMapSignatureName = "plonds-filemap.sig";
    private const string PlondsUpdateStateName = "plonds-update.json";
    private const string PlondsUpdateArchiveName = "plonds-update.zip";

    private static readonly HttpClient PlondsHttpClient = new()
    {
        Timeout = TimeSpan.FromMinutes(5)
    };

    private static readonly ResumableDownloadService PlondsDownloadService = new(PlondsHttpClient);
    private const int MaxPlondsOuterRetryAttempts = 3;

    public UpdateWorkflowService(ISettingsFacadeService settingsFacade)
    {
        _settingsFacade = settingsFacade ?? throw new ArgumentNullException(nameof(settingsFacade));
        _updatesDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LanMountainDesktop",
            "Updates");
    }

    /// <summary>
    /// Gets the path to the Launcher's incoming update directory where delta packages should be placed.
    /// </summary>
    public static string GetLauncherIncomingDirectory()
    {
        // The app runs from app-{version}/ subdirectory; Launcher root is one level up.
        var appBaseDir = AppContext.BaseDirectory;
        var launcherRoot = Path.GetDirectoryName(appBaseDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(launcherRoot))
        {
            launcherRoot = appBaseDir;
        }
        return Path.Combine(launcherRoot, LauncherDirectoryName, UpdateDirectoryName, IncomingDirectoryName);
    }

    public static string GetLauncherIncomingObjectsDirectory()
    {
        return Path.Combine(GetLauncherIncomingDirectory(), IncomingObjectsDirectoryName);
    }

    /// <summary>
    /// Checks whether a GitHub Release contains signed file-map assets needed for incremental updates.
    /// </summary>
    public static bool IsDeltaUpdateAvailable(GitHubReleaseInfo release)
    {
        if (release is null || release.Assets is null || release.Assets.Count == 0)
        {
            return false;
        }

        return TryResolveDeltaAssets(release.Assets, out _, out _, out _);
    }

    public static bool IsDeltaUpdateAvailable(UpdateCheckResult checkResult)
    {
        if (checkResult.PlondsPayload is not null)
        {
            return true;
        }

        return checkResult.Release is not null && IsDeltaUpdateAvailable(checkResult.Release);
    }

    /// <summary>
    /// Downloads signed file-map assets to the Launcher's incoming directory.
    /// </summary>
    public async Task<UpdateDownloadResult> DownloadDeltaUpdateAsync(
        UpdateCheckResult checkResult,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(checkResult);

        if (!checkResult.Success || !checkResult.IsUpdateAvailable)
        {
            return new UpdateDownloadResult(false, null, "No update available for delta download.");
        }

        if (checkResult.PlondsPayload is null && checkResult.Release is null)
        {
            return new UpdateDownloadResult(false, null, "No update payload is available for delta download.");
        }

        if (checkResult.PlondsPayload is not null)
        {
            return await DownloadPlondsDeltaUpdateAsync(checkResult, progress, cancellationToken);
        }

        var release = checkResult.Release;
        if (release is null ||
            !TryResolveDeltaAssets(release.Assets, out var manifestAsset, out var signatureAsset, out var archiveAsset))
        {
            return new UpdateDownloadResult(false, null, "Release does not contain compatible signed file-map assets.");
        }

        var incomingDir = GetLauncherIncomingDirectory();

        try
        {
            Directory.CreateDirectory(incomingDir);
        }
        catch (Exception ex)
        {
            return new UpdateDownloadResult(false, null, $"Failed to create incoming directory: {ex.Message}");
        }

        var state = _settingsFacade.Update.Get();
        var downloadSource = state.UpdateDownloadSource;
        var downloadThreads = state.UpdateDownloadThreads;

        var requiredAssets = new List<(GitHubReleaseAsset Asset, string DestinationFileName)>
        {
            (manifestAsset, SignedFileMapName),
            (signatureAsset, SignedFileMapSignatureName),
            (archiveAsset, UpdateArchiveName)
        };

        var totalAssets = requiredAssets.Count;
        var completedAssets = 0;

        foreach (var (asset, destinationFileName) in requiredAssets)
        {
            var destinationPath = Path.Combine(incomingDir, destinationFileName);

            // Skip if already downloaded and file exists
            if (File.Exists(destinationPath))
            {
                var existingHash = await GitHubReleaseUpdateService.ComputeFileSha256Async(destinationPath, cancellationToken);
                if (asset.Sha256 is not null && string.Equals(existingHash, asset.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    AppLogger.Info("UpdateWorkflow", $"Update asset {asset.Name} already downloaded with matching hash, skipping.");
                    completedAssets++;
                    progress?.Report((double)completedAssets / totalAssets);
                    continue;
                }
            }

            var assetProgress = progress is null ? null : new Progress<double>(p =>
            {
                var overallProgress = ((double)completedAssets + p) / totalAssets;
                progress.Report(overallProgress);
            });

            var result = await _settingsFacade.Update.DownloadAssetAsync(
                asset,
                destinationPath,
                downloadSource,
                downloadThreads,
                assetProgress,
                cancellationToken);

            if (!result.Success)
            {
                // Clean up partially downloaded files
                foreach (var file in requiredAssets.Select(a => a.DestinationFileName))
                {
                    try { File.Delete(Path.Combine(incomingDir, file)); } catch { }
                }
                return new UpdateDownloadResult(false, null, $"Failed to download update asset {asset.Name}: {result.ErrorMessage}");
            }

            completedAssets++;
            progress?.Report((double)completedAssets / totalAssets);
        }

        // Save state indicating a signed file-map update is pending.
        SaveState(state with
        {
            PendingUpdateInstallerPath = Path.Combine(incomingDir, SignedFileMapName),
            PendingUpdateVersion = checkResult.LatestVersionText,
            PendingUpdatePublishedAtUtcMs = checkResult.Release?.PublishedAt is DateTimeOffset publishedAt && publishedAt != DateTimeOffset.MinValue
                ? publishedAt.ToUnixTimeMilliseconds()
                : null,
            LastUpdateCheckUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            PendingUpdateSha256 = null
        });

        AppLogger.Info("UpdateWorkflow", $"Signed file-map update payload downloaded to {incomingDir}. Will be applied by Launcher on next startup.");

        return new UpdateDownloadResult(true, Path.Combine(incomingDir, SignedFileMapName), null);
    }

    private async Task<UpdateDownloadResult> DownloadPlondsDeltaUpdateAsync(
        UpdateCheckResult checkResult,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var payload = checkResult.PlondsPayload;
        if (payload is null)
        {
            return await HandlePlondsDeltaFailureAsync(
                checkResult,
                "payload-parse",
                "PLONDS payload is missing.",
                progress,
                cancellationToken);
        }

        var incomingDir = GetLauncherIncomingDirectory();
        var objectsDir = GetLauncherIncomingObjectsDirectory();

        try
        {
            Directory.CreateDirectory(incomingDir);
            Directory.CreateDirectory(objectsDir);
        }
        catch (Exception ex)
        {
            return await HandlePlondsDeltaFailureAsync(
                checkResult,
                "payload-parse",
                $"Failed to create incoming directory: {ex.Message}",
                progress,
                cancellationToken);
        }

        try
        {
            var state = _settingsFacade.Update.Get();
            var downloadThreads = Math.Max(1, state.UpdateDownloadThreads);
            var fileMapPath = Path.Combine(incomingDir, PlondsFileMapName);
            var signaturePath = Path.Combine(incomingDir, PlondsFileMapSignatureName);
            var updateStatePath = Path.Combine(incomingDir, PlondsUpdateStateName);

            var fileMapJson = await EnsurePlondsTextResourceAsync(
                payload.FileMapJson,
                payload.FileMapJsonUrl,
                fileMapPath,
                "file map",
                "filemap-download",
                cancellationToken);

            var fileMapSignature = await EnsurePlondsTextResourceAsync(
                payload.FileMapSignature,
                payload.FileMapSignatureUrl,
                signaturePath,
                "file map signature",
                "filemap-download",
                cancellationToken);

            IReadOnlyList<PlondsDownloadedObjectInfo> objectResults;
            if (!string.IsNullOrWhiteSpace(payload.UpdateArchiveUrl))
            {
                progress?.Report(2d / 3d);
                objectResults = await EnsurePlondsArchiveObjectsAsync(
                    payload,
                    incomingDir,
                    objectsDir,
                    state.UpdateDownloadSource,
                    downloadThreads,
                    progress,
                    cancellationToken);
            }
            else
            {
                IReadOnlyList<PlondsDownloadEntry> downloadEntries;
                try
                {
                    downloadEntries = ParsePlondsDownloadEntries(fileMapJson);
                }
                catch (JsonException ex)
                {
                    throw new PlondsDownloadException("payload-parse", $"PLONDS file map JSON is invalid: {ex.Message}", ex);
                }

                if (downloadEntries.Count == 0)
                {
                    throw new PlondsDownloadException("payload-parse", "PLONDS file map does not contain downloadable objects.");
                }

                var expectedObjectCount = downloadEntries.Count;
                var completedItems = 2;
                progress?.Report(expectedObjectCount == 0 ? 1d : (double)completedItems / (expectedObjectCount + 2));

                var downloadResults = new List<PlondsDownloadedObjectInfo>(expectedObjectCount);
                var objectTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var totalSteps = expectedObjectCount + 2;

                foreach (var entry in downloadEntries)
                {
                    if (!objectTargets.Add(entry.ObjectHashHex))
                    {
                        completedItems++;
                        progress?.Report((double)completedItems / totalSteps);
                        continue;
                    }

                    var objectInfo = await EnsurePlondsObjectAsync(
                        entry,
                        objectsDir,
                        downloadThreads,
                        cancellationToken);

                    downloadResults.Add(objectInfo);
                    completedItems++;
                    progress?.Report((double)completedItems / totalSteps);
                }

                objectResults = downloadResults;
            }

            var updateState = new PlondsUpdateState(
                checkResult.LatestVersionText,
                payload.DistributionId,
                payload.ChannelId,
                payload.SubChannel,
                fileMapPath,
                signaturePath,
                objectsDir,
                DateTimeOffset.UtcNow,
                fileMapJson,
                fileMapSignature,
                objectResults);

            await File.WriteAllTextAsync(updateStatePath, JsonSerializer.Serialize(updateState, UpdateJsonOptions), cancellationToken);

            SaveState(state with
            {
                PendingUpdateInstallerPath = updateStatePath,
                PendingUpdateVersion = checkResult.LatestVersionText,
                PendingUpdatePublishedAtUtcMs = checkResult.Release?.PublishedAt is DateTimeOffset publishedAt && publishedAt != DateTimeOffset.MinValue
                    ? publishedAt.ToUnixTimeMilliseconds()
                    : null,
                LastUpdateCheckUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                PendingUpdateSha256 = null
            });

            progress?.Report(1d);
            AppLogger.Info("UpdateWorkflow", $"PLONDS update payload downloaded to {incomingDir}. Will be applied by Launcher on next startup.");
            return new UpdateDownloadResult(true, updateStatePath, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var stage = ex is PlondsDownloadException plondsException
                ? plondsException.Stage
                : "payload-parse";
            var message = ex is PlondsDownloadException
                ? ex.Message
                : $"PLONDS incremental payload failed unexpectedly: {ex.Message}";

            AppLogger.Warn("UpdateWorkflow", $"Failed to download PLONDS incremental payload at stage '{stage}'.", ex);
            return await HandlePlondsDeltaFailureAsync(
                checkResult,
                stage,
                message,
                progress,
                cancellationToken);
        }
    }

    private static readonly JsonSerializerOptions UpdateJsonOptions = new()
    {
        WriteIndented = true
    };

    /// <summary>
    /// Checks whether the pending update is managed by Launcher incoming payload.
    /// </summary>
    public bool IsPendingDeltaUpdate()
    {
        var state = _settingsFacade.Update.Get();
        var pendingPath = state.PendingUpdateInstallerPath?.Trim();
        if (string.IsNullOrWhiteSpace(pendingPath))
        {
            return false;
        }

        // Incoming payload updates are identified by the local manifest or incoming directory path.
        return pendingPath.EndsWith(SignedFileMapName, StringComparison.OrdinalIgnoreCase)
            || pendingPath.EndsWith(PlondsUpdateStateName, StringComparison.OrdinalIgnoreCase)
            || pendingPath.EndsWith(PlondsFileMapName, StringComparison.OrdinalIgnoreCase)
            || pendingPath.EndsWith(PlondsFileMapSignatureName, StringComparison.OrdinalIgnoreCase)
            || pendingPath.Contains(IncomingDirectoryName, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<UpdateDownloadResult> DownloadFullInstallerAsync(
        UpdateCheckResult checkResult,
        IProgress<double>? progress,
        CancellationToken cancellationToken,
        bool forceRedownload)
    {
        if (!checkResult.Success || !checkResult.IsUpdateAvailable || checkResult.Release is null || checkResult.PreferredAsset is null)
        {
            return new UpdateDownloadResult(false, null, "No compatible update asset is available.");
        }

        var state = _settingsFacade.Update.Get();
        var existingPending = GetPendingUpdate(state);

        if (!forceRedownload &&
            existingPending is not null &&
            string.Equals(existingPending.VersionText, checkResult.LatestVersionText, StringComparison.OrdinalIgnoreCase) &&
            File.Exists(existingPending.InstallerPath))
        {
            var verifyResult = await VerifyPendingUpdateAsync();
            if (verifyResult.Success)
            {
                return new UpdateDownloadResult(
                    true,
                    existingPending.InstallerPath,
                    null,
                    verifyResult.HashMatched,
                    verifyResult.ExpectedHash,
                    verifyResult.ActualHash);
            }

            AppLogger.Warn(
                "UpdateWorkflow",
                $"Existing installer hash verification failed, will redownload. Expected: {verifyResult.ExpectedHash}, Actual: {verifyResult.ActualHash}");
        }

        if (forceRedownload && existingPending is not null && File.Exists(existingPending.InstallerPath))
        {
            try
            {
                File.Delete(existingPending.InstallerPath);
                AppLogger.Info("UpdateWorkflow", $"Deleted existing installer for redownload: {existingPending.InstallerPath}");
            }
            catch (Exception ex)
            {
                AppLogger.Warn("UpdateWorkflow", $"Failed to delete existing installer: {existingPending.InstallerPath}", ex);
            }

            ClearPendingUpdate();
            state = _settingsFacade.Update.Get();
        }

        Directory.CreateDirectory(_updatesDirectory);
        var fileName = SanitizeFileName(checkResult.PreferredAsset.Name);
        var destinationPath = Path.Combine(_updatesDirectory, fileName);

        var result = await _settingsFacade.Update.DownloadAssetAsync(
            checkResult.PreferredAsset,
            destinationPath,
            state.UpdateDownloadSource,
            state.UpdateDownloadThreads,
            progress,
            cancellationToken);

        if (result.Success)
        {
            SaveState(state with
            {
                PendingUpdateInstallerPath = result.FilePath ?? destinationPath,
                PendingUpdateVersion = checkResult.LatestVersionText,
                PendingUpdatePublishedAtUtcMs = checkResult.Release?.PublishedAt is DateTimeOffset publishedAt && publishedAt != DateTimeOffset.MinValue
                    ? publishedAt.ToUnixTimeMilliseconds()
                    : null,
                LastUpdateCheckUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                PendingUpdateSha256 = result.ActualHash
            });
        }

        return result;
    }

    private async Task<UpdateDownloadResult> HandlePlondsDeltaFailureAsync(
        UpdateCheckResult checkResult,
        string stage,
        string errorMessage,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        var normalizedMessage = string.IsNullOrWhiteSpace(errorMessage)
            ? $"PLONDS {stage} failed."
            : $"PLONDS {stage} failed: {errorMessage}";

        if (checkResult.Release is null || checkResult.PreferredAsset is null)
        {
            return new UpdateDownloadResult(false, null, normalizedMessage);
        }

        AppLogger.Warn(
            "UpdateWorkflow",
            $"PLONDS delta download failed at stage '{stage}'. Falling back to full installer download. Details: {errorMessage}");

        var fallbackResult = await DownloadFullInstallerAsync(
            checkResult,
            progress,
            cancellationToken,
            forceRedownload: false);

        if (fallbackResult.Success)
        {
            return fallbackResult;
        }

        var combinedMessage = string.IsNullOrWhiteSpace(fallbackResult.ErrorMessage)
            ? normalizedMessage
            : $"{normalizedMessage} Full installer fallback failed: {fallbackResult.ErrorMessage}";

        return new UpdateDownloadResult(false, null, combinedMessage);
    }

    private static string GetPlondsObjectDestinationPath(string objectsDirectory, string objectHashHex)
    {
        var normalizedHash = objectHashHex.Trim().ToLowerInvariant();
        var shard = normalizedHash.Length >= 2 ? normalizedHash[..2] : normalizedHash;
        return Path.Combine(objectsDirectory, shard, normalizedHash);
    }

    private static async Task<string> EnsurePlondsTextResourceAsync(
        string? inlineContent,
        string? sourceUrl,
        string destinationPath,
        string resourceName,
        string stage,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(inlineContent))
        {
            await File.WriteAllTextAsync(destinationPath, inlineContent, cancellationToken);
            return inlineContent;
        }

        if (string.IsNullOrWhiteSpace(sourceUrl))
        {
            throw new PlondsDownloadException(stage, $"PLONDS payload does not contain a {resourceName} source.");
        }

        Exception? lastError = null;
        for (var attempt = 1; attempt <= MaxPlondsOuterRetryAttempts; attempt++)
        {
            var downloadResult = await PlondsDownloadService.DownloadAsync(
                sourceUrl,
                destinationPath,
                cancellationToken: cancellationToken);

            if (downloadResult.Success)
            {
                try
                {
                    return await File.ReadAllTextAsync(destinationPath, cancellationToken);
                }
                catch (Exception ex) when (attempt < MaxPlondsOuterRetryAttempts)
                {
                    lastError = ex;
                }
            }
            else
            {
                lastError = new InvalidOperationException(downloadResult.ErrorMessage ?? $"Failed to download PLONDS {resourceName}.");
            }

            if (attempt < MaxPlondsOuterRetryAttempts)
            {
                AppLogger.Warn(
                    "UpdateWorkflow",
                    $"PLONDS {resourceName} download attempt {attempt}/{MaxPlondsOuterRetryAttempts} failed. Retrying same URL.");
                await Task.Delay(GetPlondsRetryDelay(attempt), cancellationToken);
            }
        }

        throw new PlondsDownloadException(
            stage,
            $"Failed to download PLONDS {resourceName} from {sourceUrl}.",
            lastError);
    }

    private static async Task<PlondsDownloadedObjectInfo> EnsurePlondsObjectAsync(
        PlondsDownloadEntry entry,
        string objectsDirectory,
        int downloadThreads,
        CancellationToken cancellationToken)
    {
        var destinationPath = GetPlondsObjectDestinationPath(objectsDirectory, entry.ObjectHashHex);
        var destinationDirectory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrWhiteSpace(destinationDirectory))
        {
            Directory.CreateDirectory(destinationDirectory);
        }

        var existingHash = await ComputeFileSha256HexAsync(destinationPath, cancellationToken);
        if (string.Equals(existingHash, entry.ObjectHashHex, StringComparison.OrdinalIgnoreCase))
        {
            return new PlondsDownloadedObjectInfo(entry.ComponentId, entry.RelativePath, entry.DownloadUrl, entry.ObjectHashHex, destinationPath);
        }

        if (!string.IsNullOrWhiteSpace(existingHash))
        {
            DeleteFileIfExists(destinationPath);
        }

        var downloadOptions = new DownloadOptions(MaxParallelSegments: downloadThreads);
        var allowForcedRedownload = true;
        Exception? lastError = null;

        for (var attempt = 1; attempt <= MaxPlondsOuterRetryAttempts; attempt++)
        {
            var downloadResult = await PlondsDownloadService.DownloadAsync(
                entry.DownloadUrl,
                destinationPath,
                downloadOptions,
                null,
                cancellationToken);

            if (!downloadResult.Success)
            {
                lastError = new InvalidOperationException(downloadResult.ErrorMessage ?? $"Failed to download PLONDS object {entry.RelativePath}.");
                if (attempt < MaxPlondsOuterRetryAttempts)
                {
                    AppLogger.Warn(
                        "UpdateWorkflow",
                        $"PLONDS object download attempt {attempt}/{MaxPlondsOuterRetryAttempts} failed for {entry.RelativePath}. Retrying.");
                    await Task.Delay(GetPlondsRetryDelay(attempt), cancellationToken);
                    continue;
                }

                throw new PlondsDownloadException(
                    "object-download",
                    $"Failed to download PLONDS object {entry.RelativePath}.",
                    lastError);
            }

            var actualHash = await ComputeFileSha256HexAsync(destinationPath, cancellationToken);
            if (!string.IsNullOrWhiteSpace(actualHash) &&
                string.Equals(actualHash, entry.ObjectHashHex, StringComparison.OrdinalIgnoreCase))
            {
                return new PlondsDownloadedObjectInfo(entry.ComponentId, entry.RelativePath, entry.DownloadUrl, entry.ObjectHashHex, destinationPath);
            }

            DeleteFileIfExists(destinationPath);
            var mismatchMessage = $"PLONDS object hash mismatch for {entry.RelativePath}. Expected: {entry.ObjectHashHex}, Actual: {actualHash ?? "<missing>"}";
            lastError = new InvalidOperationException(mismatchMessage);

            if (allowForcedRedownload)
            {
                allowForcedRedownload = false;
                AppLogger.Warn(
                    "UpdateWorkflow",
                    $"{mismatchMessage}. Removing the bad object and forcing one clean re-download.");
                await Task.Delay(GetPlondsRetryDelay(attempt), cancellationToken);
                continue;
            }

            throw new PlondsDownloadException("object-verify", mismatchMessage, lastError);
        }

        throw new PlondsDownloadException(
            "object-download",
            $"Failed to download PLONDS object {entry.RelativePath}.",
            lastError);
    }

    private async Task<IReadOnlyList<PlondsDownloadedObjectInfo>> EnsurePlondsArchiveObjectsAsync(
        PlondsUpdatePayload payload,
        string incomingDirectory,
        string objectsDirectory,
        string downloadSource,
        int downloadThreads,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(payload.UpdateArchiveUrl))
        {
            throw new PlondsDownloadException("payload-parse", "PLONDS payload does not contain an update archive URL.");
        }

        var archiveAsset = new GitHubReleaseAsset(
            Name: Path.GetFileName(payload.UpdateArchiveUrl) ?? PlondsUpdateArchiveName,
            BrowserDownloadUrl: payload.UpdateArchiveUrl,
            SizeBytes: payload.UpdateArchiveSizeBytes ?? 0,
            Sha256: payload.UpdateArchiveSha256);
        var archivePath = Path.Combine(incomingDirectory, PlondsUpdateArchiveName);
        var archiveProgress = progress is null
            ? null
            : new Progress<double>(p => progress.Report((2d + p) / 3d));

        var downloadResult = await _settingsFacade.Update.DownloadAssetAsync(
            archiveAsset,
            archivePath,
            downloadSource,
            downloadThreads,
            archiveProgress,
            cancellationToken);

        if (!downloadResult.Success)
        {
            downloadResult = await _settingsFacade.Update.RedownloadAssetAsync(
                archiveAsset,
                archivePath,
                downloadSource,
                downloadThreads,
                archiveProgress,
                cancellationToken);
        }

        if (!downloadResult.Success)
        {
            throw new PlondsDownloadException(
                "object-download",
                $"Failed to download PLONDS update archive: {downloadResult.ErrorMessage}");
        }

        try
        {
            if (Directory.Exists(objectsDirectory))
            {
                Directory.Delete(objectsDirectory, recursive: true);
            }

            Directory.CreateDirectory(objectsDirectory);
            ZipFile.ExtractToDirectory(archivePath, objectsDirectory, overwriteFiles: true);
        }
        catch (Exception ex)
        {
            throw new PlondsDownloadException(
                "payload-parse",
                $"Failed to extract PLONDS update archive: {ex.Message}",
                ex);
        }
        finally
        {
            DeleteFileIfExists(archivePath);
        }

        var objectResults = Directory.EnumerateFiles(objectsDirectory, "*", SearchOption.AllDirectories)
            .Select(path => new PlondsDownloadedObjectInfo(
                ComponentId: "app",
                RelativePath: Path.GetRelativePath(objectsDirectory, path).Replace('\\', '/'),
                SourceUrl: payload.UpdateArchiveUrl,
                ObjectHashHex: Path.GetFileName(path),
                LocalPath: path))
            .ToArray();

        progress?.Report(1d);
        return objectResults;
    }

    private static IReadOnlyList<PlondsDownloadEntry> ParsePlondsDownloadEntries(string fileMapJson)
    {
        var entries = new List<PlondsDownloadEntry>();
        if (string.IsNullOrWhiteSpace(fileMapJson))
        {
            return entries;
        }

        using var document = JsonDocument.Parse(fileMapJson);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return entries;
        }

        if (!TryGetPropertyIgnoreCase(root, "components", out var componentsNode))
        {
            return entries;
        }

        if (componentsNode.ValueKind == JsonValueKind.Object)
        {
            foreach (var component in componentsNode.EnumerateObject())
            {
                if (component.Value.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                if (!TryGetPropertyIgnoreCase(component.Value, "files", out var filesNode))
                {
                    continue;
                }

                AppendDownloadEntries(entries, component.Name, filesNode);
            }
        }
        else if (componentsNode.ValueKind == JsonValueKind.Array)
        {
            foreach (var component in componentsNode.EnumerateArray())
            {
                if (component.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var componentId = ReadStringIgnoreCase(component, "id")
                                  ?? ReadStringIgnoreCase(component, "name")
                                  ?? "app";
                if (!TryGetPropertyIgnoreCase(component, "files", out var filesNode))
                {
                    continue;
                }

                AppendDownloadEntries(entries, componentId, filesNode);
            }
        }

        return entries;
    }

    private static void AppendDownloadEntries(ICollection<PlondsDownloadEntry> entries, string componentId, JsonElement filesNode)
    {
        if (filesNode.ValueKind == JsonValueKind.Object)
        {
            foreach (var fileEntry in filesNode.EnumerateObject())
            {
                if (fileEntry.Value.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                if (TryCreateDownloadEntry(componentId, fileEntry.Name, fileEntry.Value, out var entry))
                {
                    entries.Add(entry);
                }
            }

            return;
        }

        if (filesNode.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var fileEntry in filesNode.EnumerateArray())
        {
            if (fileEntry.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var relativePath = ReadStringIgnoreCase(fileEntry, "path");
            if (TryCreateDownloadEntry(componentId, relativePath, fileEntry, out var entry))
            {
                entries.Add(entry);
            }
        }
    }

    private static bool TryCreateDownloadEntry(
        string componentId,
        string? relativePath,
        JsonElement fileNode,
        out PlondsDownloadEntry entry)
    {
        entry = default!;

        var normalizedPath = string.IsNullOrWhiteSpace(relativePath)
            ? null
            : relativePath.Trim();
        var downloadUrl = ReadStringIgnoreCase(fileNode, "objecturl")
                          ?? ReadStringIgnoreCase(fileNode, "downloadurl")
                          ?? ReadStringIgnoreCase(fileNode, "archivedownloadurl")
                          ?? ReadStringIgnoreCase(fileNode, "url");
        var hashHex = ReadStringIgnoreCase(fileNode, "sha256")
                      ?? ReadStringIgnoreCase(fileNode, "filesha256")
                      ?? ReadStringIgnoreCase(fileNode, "contenthash");

        if ((string.IsNullOrWhiteSpace(hashHex) || string.IsNullOrWhiteSpace(downloadUrl)) &&
            TryGetPropertyIgnoreCase(fileNode, "hash", out var hashNode) &&
            hashNode.ValueKind == JsonValueKind.Object)
        {
            var algorithm = ReadStringIgnoreCase(hashNode, "algorithm");
            if (string.IsNullOrWhiteSpace(algorithm) ||
                algorithm.Contains("sha256", StringComparison.OrdinalIgnoreCase))
            {
                hashHex ??= ReadStringIgnoreCase(hashNode, "value");
            }
        }

        if (string.IsNullOrWhiteSpace(normalizedPath) ||
            string.IsNullOrWhiteSpace(downloadUrl) ||
            string.IsNullOrWhiteSpace(hashHex))
        {
            return false;
        }

        entry = new PlondsDownloadEntry(
            componentId,
            normalizedPath,
            downloadUrl,
            NormalizeHashText(hashHex));
        return true;
    }

    private static async Task<string?> ComputeFileSha256HexAsync(string filePath, CancellationToken cancellationToken)
    {
        if (!File.Exists(filePath))
        {
            return null;
        }

        await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var hashBytes = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    private static string NormalizeHashText(string hash)
    {
        var normalized = hash.Trim();
        var separator = normalized.IndexOf(':');
        if (separator >= 0 && separator < normalized.Length - 1)
        {
            normalized = normalized[(separator + 1)..];
        }

        return normalized.Replace("-", string.Empty).Trim().ToLowerInvariant();
    }

    private static void DeleteFileIfExists(string path)
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
            // Best effort cleanup only. The caller still verifies the resulting payload before it is applied.
        }
    }

    private static TimeSpan GetPlondsRetryDelay(int attempt)
    {
        return attempt switch
        {
            1 => TimeSpan.FromMilliseconds(350),
            2 => TimeSpan.FromMilliseconds(900),
            _ => TimeSpan.FromMilliseconds(1500)
        };
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement node, string propertyName, out JsonElement value)
    {
        if (node.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in node.EnumerateObject())
            {
                if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    private static string? ReadStringIgnoreCase(JsonElement node, string propertyName)
    {
        return TryGetPropertyIgnoreCase(node, propertyName, out var value)
            ? value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : value.ToString()
            : null;
    }

    private static byte[]? ReadByteArrayIgnoreCase(JsonElement node, string propertyName)
    {
        if (!TryGetPropertyIgnoreCase(node, propertyName, out var value))
        {
            return null;
        }

        return ReadByteArray(value);
    }

    private static byte[]? ReadByteArray(JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.String:
            {
                var text = value.GetString()?.Trim();
                if (string.IsNullOrWhiteSpace(text))
                {
                    return null;
                }

                if (IsHexString(text))
                {
                    try
                    {
                        return Convert.FromHexString(text);
                    }
                    catch
                    {
                        // fall through to base64
                    }
                }

                try
                {
                    return Convert.FromBase64String(text);
                }
                catch
                {
                    return null;
                }
            }
            case JsonValueKind.Array:
            {
                var bytes = new List<byte>();
                foreach (var item in value.EnumerateArray())
                {
                    if (!item.TryGetInt32(out var number) || number is < byte.MinValue or > byte.MaxValue)
                    {
                        return null;
                    }

                    bytes.Add((byte)number);
                }

                return bytes.ToArray();
            }
            default:
                return null;
        }
    }

    private static bool IsHexString(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length % 2 != 0)
        {
            return false;
        }

        foreach (var ch in value)
        {
            if (!Uri.IsHexDigit(ch))
            {
                return false;
            }
        }

        return true;
    }

    private sealed record PlondsDownloadEntry(
        string ComponentId,
        string RelativePath,
        string DownloadUrl,
        string ObjectHashHex);

    private sealed class PlondsDownloadException : Exception
    {
        public PlondsDownloadException(string stage, string message, Exception? innerException = null)
            : base(message, innerException)
        {
            Stage = stage;
        }

        public string Stage { get; }
    }

    private sealed record PlondsDownloadedObjectInfo(
        string ComponentId,
        string RelativePath,
        string SourceUrl,
        string ObjectHashHex,
        string LocalPath);

    private sealed record PlondsUpdateState(
        string VersionText,
        string DistributionId,
        string ChannelId,
        string SubChannel,
        string FileMapPath,
        string FileMapSignaturePath,
        string ObjectsDirectory,
        DateTimeOffset DownloadedAtUtc,
        string FileMapJson,
        string FileMapSignature,
        IReadOnlyList<PlondsDownloadedObjectInfo> Objects);

    private static bool TryResolveDeltaAssets(
        IReadOnlyList<GitHubReleaseAsset> assets,
        out GitHubReleaseAsset manifestAsset,
        out GitHubReleaseAsset signatureAsset,
        out GitHubReleaseAsset archiveAsset)
    {
        manifestAsset = default!;
        signatureAsset = default!;
        archiveAsset = default!;

        if (assets is null || assets.Count == 0)
        {
            return false;
        }

        var platformSuffix = GetPlatformAssetSuffix();
        var platformManifest = $"files-{platformSuffix}.json";
        var platformSignature = $"files-{platformSuffix}.json.sig";
        var platformArchive = $"update-{platformSuffix}.zip";

        var manifestCandidate = FindAsset(assets, platformManifest) ?? FindAsset(assets, SignedFileMapName);
        var signatureCandidate = FindAsset(assets, platformSignature) ?? FindAsset(assets, SignedFileMapSignatureName);
        var archiveCandidate = FindAsset(assets, platformArchive) ?? FindAsset(assets, UpdateArchiveName);
        if (manifestCandidate is null || signatureCandidate is null || archiveCandidate is null)
        {
            return false;
        }

        manifestAsset = manifestCandidate;
        signatureAsset = signatureCandidate;
        archiveAsset = archiveCandidate;
        return true;
    }

    private static GitHubReleaseAsset? FindAsset(IReadOnlyList<GitHubReleaseAsset> assets, string name)
    {
        return assets.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    private static string GetPlatformAssetSuffix()
    {
        var os = OperatingSystem.IsWindows()
            ? "windows"
            : OperatingSystem.IsLinux()
                ? "linux"
                : OperatingSystem.IsMacOS()
                    ? "macos"
                    : "unknown";

        var arch = RuntimeInformation.OSArchitecture switch
        {
            Architecture.X86 => "x86",
            Architecture.Arm => "arm",
            Architecture.Arm64 => "arm64",
            _ => "x64"
        };

        return $"{os}-{arch}";
    }

    public UpdatePendingInfo? GetPendingUpdate()
    {
        var state = _settingsFacade.Update.Get();
        return GetPendingUpdate(state);
    }

    public async Task<UpdateCheckResult> CheckForUpdatesAsync(
        Version currentVersion,
        bool isForce = false,
        CancellationToken cancellationToken = default)
    {
        var state = _settingsFacade.Update.Get();
        var includePrerelease = string.Equals(
            UpdateSettingsValues.NormalizeChannel(state.UpdateChannel, state.IncludePrereleaseUpdates),
            UpdateSettingsValues.ChannelPreview,
            StringComparison.OrdinalIgnoreCase);

        var result = isForce
            ? await _settingsFacade.Update.ForceCheckForUpdatesAsync(
                currentVersion,
                includePrerelease,
                cancellationToken)
            : await _settingsFacade.Update.CheckForUpdatesAsync(
                currentVersion,
                includePrerelease,
                cancellationToken);

        SaveState(state with
        {
            LastUpdateCheckUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        });

        return result;
    }

    public async Task<UpdateCheckResult> ForceCheckForUpdatesAsync(
        Version currentVersion,
        CancellationToken cancellationToken = default)
    {
        return await CheckForUpdatesAsync(currentVersion, true, cancellationToken);
    }

    public async Task<UpdateDownloadResult> DownloadReleaseAsync(
        UpdateCheckResult checkResult,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(checkResult);

        if (checkResult.PlondsPayload is not null)
        {
            return await DownloadDeltaUpdateAsync(checkResult, progress, cancellationToken);
        }

        return await DownloadFullInstallerAsync(
            checkResult,
            progress,
            cancellationToken,
            forceRedownload: false);
    }

    public async Task<UpdateDownloadResult> RedownloadReleaseAsync(
        UpdateCheckResult checkResult,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(checkResult);

        if (checkResult.PlondsPayload is not null)
        {
            ClearPendingUpdate();
            return await DownloadDeltaUpdateAsync(checkResult, progress, cancellationToken);
        }

        return await DownloadFullInstallerAsync(
            checkResult,
            progress,
            cancellationToken,
            forceRedownload: true);
    }

    public async Task<UpdateVerifyResult> VerifyPendingUpdateAsync()
    {
        var state = _settingsFacade.Update.Get();
        var pending = GetPendingUpdate(state);

        if (pending is null)
        {
            return new UpdateVerifyResult(false, false, null, null, "No pending update available.");
        }

        if (!File.Exists(pending.InstallerPath))
        {
            if (IsPendingDeltaUpdate())
            {
                var pdcUpdatePath = pending.InstallerPath;
                var pdcFileMapPath = Path.Combine(Path.GetDirectoryName(pdcUpdatePath) ?? string.Empty, PlondsFileMapName);
                var pdcSignaturePath = Path.Combine(Path.GetDirectoryName(pdcUpdatePath) ?? string.Empty, PlondsFileMapSignatureName);
                if (File.Exists(pdcUpdatePath) && File.Exists(pdcFileMapPath) && File.Exists(pdcSignaturePath))
                {
                    return new UpdateVerifyResult(true, true, null, null, null);
                }

                return new UpdateVerifyResult(false, false, null, null, "PLONDS update payload is incomplete.");
            }

            return new UpdateVerifyResult(false, false, null, null, "Installer file does not exist.");
        }

        if (IsPendingDeltaUpdate())
        {
            return new UpdateVerifyResult(true, true, null, null, null);
        }

        var expectedHash = pending.Sha256;
        var actualHash = await GitHubReleaseUpdateService.ComputeFileSha256Async(pending.InstallerPath);

        if (string.IsNullOrEmpty(expectedHash))
        {
            return new UpdateVerifyResult(true, true, null, actualHash, null);
        }

        var hashMatched = string.Equals(
            expectedHash?.Trim().ToLowerInvariant(),
            actualHash?.Trim().ToLowerInvariant(),
            StringComparison.OrdinalIgnoreCase);

        return new UpdateVerifyResult(
            hashMatched,
            hashMatched,
            expectedHash,
            actualHash,
            hashMatched ? null : $"Hash mismatch. Expected: {expectedHash}, Actual: {actualHash}");
    }

    public async Task AutoCheckIfEnabledAsync(
        Version currentVersion,
        CancellationToken cancellationToken = default)
    {
        var state = _settingsFacade.Update.Get();

        try
        {
            // Always check for updates on startup (removed AutoCheckUpdates check)
            var result = await CheckForUpdatesAsync(currentVersion, isForce: false, cancellationToken);
            if (!result.Success || !result.IsUpdateAvailable || (result.Release is null && result.PlondsPayload is null))
            {
                return;
            }

            var normalizedMode = UpdateSettingsValues.NormalizeMode(state.UpdateMode);
            
            // For "Silent Download" and "Silent Install" modes, automatically download the update
            if (string.Equals(normalizedMode, UpdateSettingsValues.ModeDownloadThenConfirm, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalizedMode, UpdateSettingsValues.ModeSilentOnExit, StringComparison.OrdinalIgnoreCase))
            {
                // Prefer delta update if available (smaller download, faster)
                if (IsDeltaUpdateAvailable(result))
                {
                    AppLogger.Info("UpdateWorkflow", "Delta update available, downloading incremental package.");
                    await DownloadDeltaUpdateAsync(result, cancellationToken: cancellationToken);
                }
                else if (result.PreferredAsset is not null)
                {
                    await DownloadReleaseAsync(result, cancellationToken: cancellationToken);
                }
            }
            // For "Manual" mode, just check but don't download
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            AppLogger.Warn("UpdateWorkflow", "Automatic update check failed.", ex);
        }
    }

    public UpdateInstallerLaunchResult LaunchPendingInstallerNow()
    {
        if (IsPendingDeltaUpdate())
        {
            var launchResult = LaunchLauncherForApplyUpdate();
            return launchResult
                ? new UpdateInstallerLaunchResult(true, false, null)
                : new UpdateInstallerLaunchResult(false, false, "Failed to launch updater for incremental update.");
        }

        return LaunchPendingInstaller(silent: false, exitApplicationAfterLaunch: true);
    }

    public bool TryApplyPendingUpdateOnExit()
    {
        var state = _settingsFacade.Update.Get();
        if (!string.Equals(
                UpdateSettingsValues.NormalizeMode(state.UpdateMode),
                UpdateSettingsValues.ModeSilentOnExit,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // For delta updates, launch the Launcher with apply-update command so it can
        // apply the update immediately with a progress UI, matching the full installer experience.
        if (IsPendingDeltaUpdate())
        {
            AppLogger.Info("UpdateWorkflow", "Delta update pending. Launching Launcher to apply update with progress UI.");
            var launchResult = LaunchLauncherForApplyUpdate();
            if (launchResult)
            {
                ClearPendingUpdate();
            }
            return launchResult;
        }

        var result = LaunchPendingInstaller(silent: true, exitApplicationAfterLaunch: false);
        if (!result.Success && !string.IsNullOrWhiteSpace(result.ErrorMessage))
        {
            AppLogger.Warn("UpdateWorkflow", $"Silent update on exit failed: {result.ErrorMessage}");
        }

        return result.Success;
    }

    /// <summary>
    /// Launches the Launcher process with the apply-update command to apply a pending delta update
    /// with a progress UI, providing an experience similar to a full installer.
    /// </summary>
    public bool LaunchLauncherForApplyUpdate()
    {
        try
        {
            var launcherExeName = OperatingSystem.IsWindows()
                ? "LanMountainDesktop.Launcher.exe"
                : "LanMountainDesktop.Launcher";

            // The Launcher is in the parent directory of the app's base directory
            // (app runs from app-{version}/ subdirectory, Launcher is at root)
            var appBaseDir = AppContext.BaseDirectory;
            var launcherRoot = Path.GetDirectoryName(appBaseDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.IsNullOrWhiteSpace(launcherRoot))
            {
                launcherRoot = appBaseDir;
            }

            var launcherPath = Path.Combine(launcherRoot, launcherExeName);
            if (!File.Exists(launcherPath))
            {
                AppLogger.Warn("UpdateWorkflow", $"Launcher executable not found at '{launcherPath}'. Falling back to next-startup apply.");
                return false;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = launcherPath,
                Arguments = $"apply-update --app-root \"{launcherRoot}\"",
                UseShellExecute = false,
                WorkingDirectory = launcherRoot
            };

            Process.Start(startInfo);
            AppLogger.Info("UpdateWorkflow", $"Launched Launcher for apply-update: {launcherPath}");
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Warn("UpdateWorkflow", $"Failed to launch Launcher for apply-update: {ex.Message}");
            return false;
        }
    }

    public void ClearPendingUpdate()
    {
        var state = _settingsFacade.Update.Get();
        SaveState(state with
        {
            PendingUpdateInstallerPath = null,
            PendingUpdateVersion = null,
            PendingUpdatePublishedAtUtcMs = null,
            PendingUpdateSha256 = null
        });
    }

    private UpdateInstallerLaunchResult LaunchPendingInstaller(bool silent, bool exitApplicationAfterLaunch)
    {
        var state = _settingsFacade.Update.Get();
        var pending = GetPendingUpdate(state);
        if (pending is null)
        {
            return new UpdateInstallerLaunchResult(false, false, "No pending installer is available.");
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = pending.InstallerPath,
                WorkingDirectory = Path.GetDirectoryName(pending.InstallerPath) ?? _updatesDirectory,
                UseShellExecute = true,
                Verb = OperatingSystem.IsWindows() ? "runas" : string.Empty,
                Arguments = silent ? "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART" : string.Empty
            };

            Process.Start(startInfo);
            ClearPendingUpdate();

            if (exitApplicationAfterLaunch)
            {
                App.CurrentHostApplicationLifecycle?.TryExit(new HostApplicationLifecycleRequest(
                    Source: "Update",
                    Reason: silent
                        ? "Silent installer launched."
                        : "Installer launched from update page."));
            }

            return new UpdateInstallerLaunchResult(true, false, null);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return new UpdateInstallerLaunchResult(false, true, ex.Message);
        }
        catch (Exception ex)
        {
            return new UpdateInstallerLaunchResult(false, false, ex.Message);
        }
    }

    private UpdatePendingInfo? GetPendingUpdate(UpdateSettingsState state)
    {
        var installerPath = state.PendingUpdateInstallerPath?.Trim();
        if (string.IsNullOrWhiteSpace(installerPath))
        {
            return null;
        }

        if (!File.Exists(installerPath))
        {
            ClearPendingUpdate();
            return null;
        }

        DateTimeOffset? publishedAt = state.PendingUpdatePublishedAtUtcMs is > 0
            ? DateTimeOffset.FromUnixTimeMilliseconds(state.PendingUpdatePublishedAtUtcMs.Value)
            : null;

        return new UpdatePendingInfo(
            installerPath,
            string.IsNullOrWhiteSpace(state.PendingUpdateVersion) ? Path.GetFileNameWithoutExtension(installerPath) : state.PendingUpdateVersion,
            publishedAt,
            state.PendingUpdateSha256);
    }

    private void SaveState(UpdateSettingsState state)
    {
        _settingsFacade.Update.Save(state);
    }

    private static string SanitizeFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return FormattableString.Invariant($"LanMountainDesktop-update-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}.exe");
        }

        var invalid = Path.GetInvalidFileNameChars();
        Span<char> buffer = stackalloc char[fileName.Length];
        var index = 0;
        foreach (var ch in fileName)
        {
            buffer[index++] = Array.IndexOf(invalid, ch) >= 0 ? '_' : ch;
        }

        return new string(buffer[..index]);
    }
}
