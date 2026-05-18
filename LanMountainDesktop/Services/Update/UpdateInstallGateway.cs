using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using LanMountainDesktop.Shared.Contracts.Update;

namespace LanMountainDesktop.Services.Update;

public sealed record InstallResult(bool Success, string? ErrorMessage, bool UserCancelledElevation, string? ErrorCode = null);

internal sealed class UpdateInstallGateway
{
    public async Task<InstallResult> InstallAsync(
        UpdatePayloadKind payloadKind,
        string launcherRoot,
        IProgress<InstallProgressReport>? progress,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        try
        {
            progress?.Report(new InstallProgressReport(
                InstallStage.VerifySignature,
                "Verifying payload...",
                0,
                null,
                0,
                0));

            if (!VerifyDeploymentLock(payloadKind, launcherRoot, out var lockErrorCode, out var lockError))
            {
                return new InstallResult(false, lockError, false, lockErrorCode);
            }

            if (payloadKind is UpdatePayloadKind.DeltaPlonds or UpdatePayloadKind.DeltaLegacy)
            {
                var launched = LaunchLauncherForApplyUpdate(launcherRoot);
                if (!launched)
                {
                    return new InstallResult(false, "Failed to launch Launcher for delta update application.", false, "apply_failed");
                }

                progress?.Report(new InstallProgressReport(
                    InstallStage.ActivateDeployment,
                    "Launcher launched for apply-update.",
                    100,
                    null,
                    0,
                    0));

                return new InstallResult(true, null, false);
            }

            var installerPath = FindPendingInstaller(launcherRoot, payloadKind, ct);
            if (installerPath is null)
            {
                return new InstallResult(false, "No pending installer found.", false, "staging_incomplete");
            }

            var installerLaunched = LaunchFullInstaller(installerPath);
            if (!installerLaunched.Success)
            {
                return installerLaunched;
            }

            progress?.Report(new InstallProgressReport(
                InstallStage.ActivateDeployment,
                "Full installer launched.",
                100,
                null,
                0,
                0));

            return new InstallResult(true, null, false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            AppLogger.Warn("UpdateInstallGateway", $"Install failed: {ex.Message}");
            return new InstallResult(false, ex.Message, false);
        }
    }

    private static bool VerifyDeploymentLock(UpdatePayloadKind payloadKind, string launcherRoot, out string? errorCode, out string? error)
    {
        errorCode = null;
        error = null;
        var deploymentLock = DeploymentLockService.ReadLock(launcherRoot);
        if (deploymentLock is null)
        {
            errorCode = "lock_conflict";
            error = "Deployment lock is missing. Please redownload the update.";
            return false;
        }

        if (deploymentLock.SchemaVersion != 1)
        {
            errorCode = "lock_conflict";
            error = "Deployment lock schema is unsupported. Please redownload the update.";
            return false;
        }

        var expectedKind = payloadKind is UpdatePayloadKind.DeltaLegacy or UpdatePayloadKind.DeltaPlonds ? "delta" : "full";
        if (!string.Equals(deploymentLock.Kind, expectedKind, StringComparison.OrdinalIgnoreCase))
        {
            errorCode = "lock_conflict";
            error = "Deployment lock payload type mismatch. Please redownload the update.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(deploymentLock.PayloadPath) || !File.Exists(deploymentLock.PayloadPath))
        {
            errorCode = "staging_incomplete";
            error = "Deployment lock payload path is missing. Please redownload the update.";
            return false;
        }

        return true;
    }

    private bool LaunchLauncherForApplyUpdate(string launcherRoot)
    {
        try
        {
            var launcherPath = LauncherPathResolver.ResolveLauncherExecutablePath();
            if (string.IsNullOrWhiteSpace(launcherPath) || !File.Exists(launcherPath))
            {
                AppLogger.Warn("UpdateInstallGateway", "Launcher executable not found. Falling back to next-startup apply.");
                return false;
            }

            var resolvedLauncherRoot = Path.GetDirectoryName(launcherPath)!;

            var startInfo = new ProcessStartInfo
            {
                FileName = launcherPath,
                Arguments = $"apply-update --app-root \"{resolvedLauncherRoot}\" --launch-source apply-update",
                UseShellExecute = false,
                WorkingDirectory = resolvedLauncherRoot
            };

            Process.Start(startInfo);
            AppLogger.Info("UpdateInstallGateway", $"Launched Launcher for apply-update: {launcherPath}");
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Warn("UpdateInstallGateway", $"Failed to launch Launcher for apply-update: {ex.Message}");
            return false;
        }
    }

    private InstallResult LaunchFullInstaller(string installerPath)
    {
        try
        {
            AppLogger.Info("UpdateInstallGateway", "Launching full installer with elevation.");
            var workingDir = Path.GetDirectoryName(installerPath) ?? Path.GetDirectoryName(installerPath)!;

            var startInfo = new ProcessStartInfo
            {
                FileName = installerPath,
                WorkingDirectory = workingDir,
                UseShellExecute = true,
                Verb = OperatingSystem.IsWindows() ? "runas" : string.Empty,
                Arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART"
            };

            Process.Start(startInfo);
            return new InstallResult(true, null, false);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return new InstallResult(false, ex.Message, true);
        }
        catch (Exception ex)
        {
            AppLogger.Warn("UpdateInstallGateway", $"Failed to launch full installer: {ex.Message}");
            return new InstallResult(false, ex.Message, false);
        }
    }

    private static string? FindPendingInstaller(string launcherRoot, UpdatePayloadKind payloadKind, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var incomingDir = UpdatePaths.GetIncomingDirectory(launcherRoot);
        if (!Directory.Exists(incomingDir))
        {
            return null;
        }

        var executables = new DirectoryInfo(incomingDir)
            .EnumerateFiles("*.exe", SearchOption.TopDirectoryOnly)
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .ThenBy(file => file.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (executables.Length == 0)
        {
            return null;
        }

        return executables[0].FullName;
    }
}
