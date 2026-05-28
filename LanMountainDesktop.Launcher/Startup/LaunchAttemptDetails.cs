using LanMountainDesktop.Launcher.Models;
using LanMountainDesktop.Shared.Contracts.Launcher;

namespace LanMountainDesktop.Launcher.Startup;

internal static class LaunchAttemptDetails
{
    public static Dictionary<string, string> Build(
        StartupAttemptRecord? trackedAttempt,
        bool attachedToExistingAttempt,
        bool ipcConnected,
        bool hostProcessAlive,
        StartupStage lastStage,
        string lastStageMessage,
        string? activationFailureReason,
        bool softTimeoutShown,
        bool recoveryActivationAttempted)
    {
        var details = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["hostProcessAlive"] = hostProcessAlive.ToString(),
            ["attachedToExistingAttempt"] = attachedToExistingAttempt.ToString(),
            ["ipcConnected"] = ipcConnected.ToString(),
            ["ipcStage"] = lastStage.ToString(),
            ["ipcMessage"] = lastStageMessage,
            ["activationFailureReason"] = activationFailureReason ?? string.Empty,
            ["softTimeoutShown"] = softTimeoutShown.ToString(),
            ["recoveryActivationAttempted"] = recoveryActivationAttempted.ToString()
        };

        if (trackedAttempt is not null)
        {
            details["startupAttemptId"] = trackedAttempt.AttemptId;
            details["startupAttemptState"] = trackedAttempt.State.ToString();
            details["startupAttemptStartedAtUtc"] = trackedAttempt.StartedAtUtc.ToString("O");
            details["startupAttemptUpdatedAtUtc"] = trackedAttempt.UpdatedAtUtc.ToString("O");
            details["startupAttemptHeartbeatAtUtc"] = trackedAttempt.HeartbeatAtUtc.ToString("O");
            details["successPolicy"] = trackedAttempt.SuccessPolicy;
            details["hostPid"] = trackedAttempt.HostPid.ToString();
            details["coordinatorPid"] = trackedAttempt.CoordinatorPid.ToString();
            details["coordinatorPipeName"] = trackedAttempt.CoordinatorPipeName;
            details["reservedBeforeHostStart"] = trackedAttempt.ReservedBeforeHostStart.ToString();
            details["publicIpcConnected"] = trackedAttempt.PublicIpcConnected.ToString();
            details["shellStatus"] = trackedAttempt.ShellStatus;
        }

        return details;
    }
}
