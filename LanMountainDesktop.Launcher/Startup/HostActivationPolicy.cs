using LanMountainDesktop.Shared.Contracts.Launcher;
using LanMountainDesktop.Shared.IPC.Abstractions.Services;

namespace LanMountainDesktop.Launcher.Startup;

internal static class HostActivationPolicy
{
    internal static bool ShouldProbeExistingHostBeforeLaunch(CommandContext context)
    {
        if (!string.Equals(context.Command, "launch", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (context.IsPreviewCommand || context.IsMaintenanceCommand)
        {
            return false;
        }

        return !string.Equals(context.LaunchSource, "restart", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsExistingHostReadyForLauncherDecision(PublicShellStatus? status) =>
        status is { PublicIpcReady: true, ProcessId: > 0 };

    internal static bool IsRecoverableActivationFailure(PublicShellActivationResult activation)
    {
        if (activation.Accepted)
        {
            return false;
        }

        if (string.Equals(activation.Code, "shutdown_in_progress", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return activation.Status.PublicIpcReady &&
               (!activation.Status.MainWindowOpened ||
                !activation.Status.DesktopVisible ||
                string.Equals(activation.Code, "shell_not_ready", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(activation.Code, "startup_pending", StringComparison.OrdinalIgnoreCase));
    }

    internal static bool IsSuccessfulActivationExitCode(int exitCode) =>
        exitCode == HostExitCodes.SecondaryActivationSucceeded;

    internal static bool IsFailedActivationExitCode(int exitCode) =>
        exitCode is HostExitCodes.SecondaryActivationFailed or HostExitCodes.RestartLockNotAcquired;
}
