using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using LanMountainDesktop.Shared.Contracts.Update;

namespace LanMountainDesktop.Services.Update;

public sealed record InstallResult(bool Success, string? ErrorMessage, bool UserCancelledElevation);

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

            if (payloadKind is UpdatePayloadKind.DeltaPlonds or UpdatePayloadKind.DeltaLegacy)
            {
                var launched = LaunchLauncherForApplyUpdate(launcherRoot);
                if (!launched)
                {
                    return new InstallResult(false, "Failed to launch Launcher for delta update application.", false);
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

            var installerPath = FindPendingInstaller(launcherRoot);
            if (installerPath is null)
            {
                return new InstallResult(false, "No pending installer found.", false);
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

    private static string? FindPendingInstaller(string launcherRoot)
    {
        var incomingDir = UpdatePaths.GetIncomingDirectory(launcherRoot);
        if (!Directory.Exists(incomingDir))
        {
            return null;
        }

        var executables = Directory.GetFiles(incomingDir, "*.exe");
        return executables.Length > 0 ? executables[0] : null;
    }
}
