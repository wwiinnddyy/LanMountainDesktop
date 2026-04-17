using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
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
    private const string DeltaManifestFileName = "files.json";
    private const string DeltaSignatureFileName = "files.json.sig";
    private const string DeltaArchiveFileName = "update.zip";

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

    /// <summary>
    /// Checks whether a GitHub Release contains delta update assets (files.json, files.json.sig, update.zip).
    /// Also supports versioned filenames like files-{version}.json, delta-{old}-to-{new}.zip
    /// </summary>
    public static bool IsDeltaUpdateAvailable(GitHubReleaseInfo release)
    {
        if (release is null || release.Assets is null || release.Assets.Count == 0)
        {
            return false;
        }

        var assetNames = release.Assets.Select(a => a.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        
        // Check for exact matches first (preferred)
        var hasExactManifest = assetNames.Contains(DeltaManifestFileName);
        var hasExactSignature = assetNames.Contains(DeltaSignatureFileName);
        var hasExactArchive = assetNames.Contains(DeltaArchiveFileName);
        
        if (hasExactManifest && hasExactSignature && hasExactArchive)
        {
            return true;
        }
        
        // Check for versioned filenames (e.g., files-1.0.0.json, delta-0.9.9-to-1.0.0.zip)
        var hasVersionedManifest = assetNames.Any(n => n.StartsWith("files-", StringComparison.OrdinalIgnoreCase) 
            && n.EndsWith(".json", StringComparison.OrdinalIgnoreCase));
        var hasVersionedSignature = assetNames.Any(n => n.StartsWith("files-", StringComparison.OrdinalIgnoreCase) 
            && n.EndsWith(".sig", StringComparison.OrdinalIgnoreCase));
        var hasVersionedArchive = assetNames.Any(n => n.StartsWith("delta-", StringComparison.OrdinalIgnoreCase) 
            && n.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
        
        return hasVersionedManifest && hasVersionedSignature && hasVersionedArchive;
    }
    
    /// <summary>
    /// Finds the best matching delta asset name from the release assets.
    /// Prefers exact matches, falls back to versioned filenames.
    /// </summary>
    private static string? FindDeltaAssetName(GitHubReleaseInfo release, string baseName)
    {
        if (release?.Assets is null)
        {
            return null;
        }
        
        // Try exact match first
        var exactMatch = release.Assets.FirstOrDefault(a => 
            string.Equals(a.Name, baseName, StringComparison.OrdinalIgnoreCase));
        if (exactMatch != null)
        {
            return exactMatch.Name;
        }
        
        // Fall back to pattern matching
        return baseName.ToLowerInvariant() switch
        {
            "files.json" => release.Assets
                .Where(a => a.Name.StartsWith("files-", StringComparison.OrdinalIgnoreCase) 
                    && a.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(a => a.Name.Length)
                .FirstOrDefault()?.Name,
            "files.json.sig" => release.Assets
                .Where(a => a.Name.StartsWith("files-", StringComparison.OrdinalIgnoreCase) 
                    && a.Name.EndsWith(".sig", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(a => a.Name.Length)
                .FirstOrDefault()?.Name,
            "update.zip" => release.Assets
                .Where(a => a.Name.StartsWith("delta-", StringComparison.OrdinalIgnoreCase) 
                    && a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(a => a.Name.Length)
                .FirstOrDefault()?.Name,
            _ => null
        };
    }

    /// <summary>
    /// Downloads the delta update package (files.json, files.json.sig, update.zip) from a GitHub Release
    /// and places them in the Launcher's incoming directory for the Launcher to apply on next startup.
    /// </summary>
    public async Task<UpdateDownloadResult> DownloadDeltaUpdateAsync(
        UpdateCheckResult checkResult,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(checkResult);

        if (!checkResult.Success || !checkResult.IsUpdateAvailable || checkResult.Release is null)
        {
            return new UpdateDownloadResult(false, null, "No update available for delta download.");
        }

        if (!IsDeltaUpdateAvailable(checkResult.Release))
        {
            return new UpdateDownloadResult(false, null, "Release does not contain delta update assets.");
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

        // Find the actual asset names (support both exact and versioned filenames)
        var manifestAssetName = FindDeltaAssetName(checkResult.Release, DeltaManifestFileName);
        var signatureAssetName = FindDeltaAssetName(checkResult.Release, DeltaSignatureFileName);
        var archiveAssetName = FindDeltaAssetName(checkResult.Release, DeltaArchiveFileName);
        
        if (manifestAssetName is null || signatureAssetName is null || archiveAssetName is null)
        {
            return new UpdateDownloadResult(false, null, "One or more delta assets not found in release.");
        }
        
        // Build asset map with actual names from release
        var assetMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [DeltaManifestFileName] = manifestAssetName,
            [DeltaSignatureFileName] = signatureAssetName,
            [DeltaArchiveFileName] = archiveAssetName
        };
        
        var requiredAssets = new Dictionary<string, GitHubReleaseAsset>(StringComparer.OrdinalIgnoreCase)
        {
            [DeltaManifestFileName] = null!,
            [DeltaSignatureFileName] = null!,
            [DeltaArchiveFileName] = null!
        };

        foreach (var asset in checkResult.Release.Assets)
        {
            // Match by actual asset name
            foreach (var (key, actualName) in assetMap)
            {
                if (string.Equals(asset.Name, actualName, StringComparison.OrdinalIgnoreCase))
                {
                    requiredAssets[key] = asset;
                    break;
                }
            }
        }

        if (requiredAssets.Any(kvp => kvp.Value is null))
        {
            return new UpdateDownloadResult(false, null, "One or more delta assets not found in release.");
        }

        var totalAssets = requiredAssets.Count;
        var completedAssets = 0;

        foreach (var (name, asset) in requiredAssets)
        {
            var destinationPath = Path.Combine(incomingDir, name);

            // Skip if already downloaded and file exists
            if (File.Exists(destinationPath))
            {
                var existingHash = await GitHubReleaseUpdateService.ComputeFileSha256Async(destinationPath, cancellationToken);
                if (asset.Sha256 is not null && string.Equals(existingHash, asset.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    AppLogger.Info("UpdateWorkflow", $"Delta asset {name} already downloaded with matching hash, skipping.");
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
                foreach (var file in requiredAssets.Keys)
                {
                    try { File.Delete(Path.Combine(incomingDir, file)); } catch { }
                }
                return new UpdateDownloadResult(false, null, $"Failed to download delta asset {name}: {result.ErrorMessage}");
            }

            completedAssets++;
            progress?.Report((double)completedAssets / totalAssets);
        }

        // Save state indicating a delta update is pending
        SaveState(state with
        {
            PendingUpdateInstallerPath = Path.Combine(incomingDir, DeltaManifestFileName),
            PendingUpdateVersion = checkResult.LatestVersionText,
            PendingUpdatePublishedAtUtcMs = checkResult.Release.PublishedAt == DateTimeOffset.MinValue
                ? null
                : checkResult.Release.PublishedAt.ToUnixTimeMilliseconds(),
            LastUpdateCheckUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            PendingUpdateSha256 = null
        });

        AppLogger.Info("UpdateWorkflow", $"Delta update package downloaded to {incomingDir}. Will be applied by Launcher on next startup.");

        return new UpdateDownloadResult(true, Path.Combine(incomingDir, DeltaManifestFileName), null);
    }

    /// <summary>
    /// Checks whether the pending update is a delta update (files.json in incoming dir) vs a full installer.
    /// </summary>
    public bool IsPendingDeltaUpdate()
    {
        var state = _settingsFacade.Update.Get();
        var pendingPath = state.PendingUpdateInstallerPath?.Trim();
        if (string.IsNullOrWhiteSpace(pendingPath))
        {
            return false;
        }

        // Delta updates are identified by the manifest file path
        return pendingPath.EndsWith(DeltaManifestFileName, StringComparison.OrdinalIgnoreCase)
            || pendingPath.Contains(IncomingDirectoryName, StringComparison.OrdinalIgnoreCase);
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

        if (!checkResult.Success || !checkResult.IsUpdateAvailable || checkResult.Release is null || checkResult.PreferredAsset is null)
        {
            return new UpdateDownloadResult(false, null, "No compatible update asset is available.");
        }

        var state = _settingsFacade.Update.Get();
        var existingPending = GetPendingUpdate(state);
        if (existingPending is not null &&
            string.Equals(existingPending.VersionText, checkResult.LatestVersionText, StringComparison.OrdinalIgnoreCase) &&
            File.Exists(existingPending.InstallerPath))
        {
            var verifyResult = await VerifyPendingUpdateAsync();
            if (verifyResult.Success)
            {
                return new UpdateDownloadResult(true, existingPending.InstallerPath, null, verifyResult.HashMatched, verifyResult.ExpectedHash, verifyResult.ActualHash);
            }

            AppLogger.Warn("UpdateWorkflow", $"Existing installer hash verification failed, will redownload. Expected: {verifyResult.ExpectedHash}, Actual: {verifyResult.ActualHash}");
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
                PendingUpdatePublishedAtUtcMs = checkResult.Release.PublishedAt == DateTimeOffset.MinValue
                    ? null
                    : checkResult.Release.PublishedAt.ToUnixTimeMilliseconds(),
                LastUpdateCheckUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                PendingUpdateSha256 = result.ActualHash
            });
        }

        return result;
    }

    public async Task<UpdateDownloadResult> RedownloadReleaseAsync(
        UpdateCheckResult checkResult,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(checkResult);

        if (!checkResult.Success || !checkResult.IsUpdateAvailable || checkResult.Release is null || checkResult.PreferredAsset is null)
        {
            return new UpdateDownloadResult(false, null, "No compatible update asset is available.");
        }

        var state = _settingsFacade.Update.Get();
        var existingPending = GetPendingUpdate(state);

        if (existingPending is not null && File.Exists(existingPending.InstallerPath))
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
        }

        ClearPendingUpdate();

        Directory.CreateDirectory(_updatesDirectory);
        var fileName = SanitizeFileName(checkResult.PreferredAsset.Name);
        var destinationPath = Path.Combine(_updatesDirectory, fileName);

        state = _settingsFacade.Update.Get();

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
                PendingUpdatePublishedAtUtcMs = checkResult.Release.PublishedAt == DateTimeOffset.MinValue
                    ? null
                    : checkResult.Release.PublishedAt.ToUnixTimeMilliseconds(),
                LastUpdateCheckUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                PendingUpdateSha256 = result.ActualHash
            });
        }

        return result;
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
            return new UpdateVerifyResult(false, false, null, null, "Installer file does not exist.");
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
            if (!result.Success || !result.IsUpdateAvailable || result.Release is null)
            {
                return;
            }

            var normalizedMode = UpdateSettingsValues.NormalizeMode(state.UpdateMode);
            
            // For "Silent Download" and "Silent Install" modes, automatically download the update
            if (string.Equals(normalizedMode, UpdateSettingsValues.ModeDownloadThenConfirm, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalizedMode, UpdateSettingsValues.ModeSilentOnExit, StringComparison.OrdinalIgnoreCase))
            {
                // Prefer delta update if available (smaller download, faster)
                if (IsDeltaUpdateAvailable(result.Release))
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
