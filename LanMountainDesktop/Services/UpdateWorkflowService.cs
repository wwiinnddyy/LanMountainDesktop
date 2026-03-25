using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
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

    public UpdateWorkflowService(ISettingsFacadeService settingsFacade)
    {
        _settingsFacade = settingsFacade ?? throw new ArgumentNullException(nameof(settingsFacade));
        _updatesDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LanMountainDesktop",
            "Updates");
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
            if (!result.Success || !result.IsUpdateAvailable || result.PreferredAsset is null)
            {
                return;
            }

            var normalizedMode = UpdateSettingsValues.NormalizeMode(state.UpdateMode);
            
            // For "Silent Download" and "Silent Install" modes, automatically download the update
            if (string.Equals(normalizedMode, UpdateSettingsValues.ModeDownloadThenConfirm, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalizedMode, UpdateSettingsValues.ModeSilentOnExit, StringComparison.OrdinalIgnoreCase))
            {
                await DownloadReleaseAsync(result, cancellationToken: cancellationToken);
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

        var result = LaunchPendingInstaller(silent: true, exitApplicationAfterLaunch: false);
        if (!result.Success && !string.IsNullOrWhiteSpace(result.ErrorMessage))
        {
            AppLogger.Warn("UpdateWorkflow", $"Silent update on exit failed: {result.ErrorMessage}");
        }

        return result.Success;
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
