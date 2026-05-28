using System.Diagnostics;
using Avalonia.Threading;
using LanMountainDesktop.Launcher.Models;
using LanMountainDesktop.Launcher.Resources;
using LanMountainDesktop.Launcher.Startup;
using LanMountainDesktop.Launcher.Views;
using LanMountainDesktop.Shared.Contracts.Launcher;
using LanMountainDesktop.Shared.IPC;
using LanMountainDesktop.Shared.IPC.Abstractions.Services;

namespace LanMountainDesktop.Launcher.Services;

internal sealed partial class LauncherFlowCoordinator
{
    private MultiInstanceLaunchBehavior LoadMultiInstanceLaunchBehavior()
    {
        try
        {
            var settingsPath = HostAppSettingsOobeMerger.GetSettingsFilePath(_dataLocationResolver.ResolveDataRoot());
            return HostAppSettingsOobeMerger.LoadMultiInstanceLaunchBehavior(settingsPath);
        }
        catch (Exception ex)
        {
            Logger.Warn($"Failed to load multi-instance launch behavior. Falling back to default. {ex.Message}");
            return MultiInstanceLaunchBehavior.NotifyAndOpenDesktop;
        }
    }

    private static async Task<PublicShellStatus?> TryGetExistingHostStatusAsync(
        LanMountainDesktopIpcClient ipcClient,
        TimeSpan timeout)
    {
        try
        {
            var connected = ipcClient.IsConnected ||
                            await PublicIpcConnection.TryConnectAsync(ipcClient, timeout).ConfigureAwait(false);
            if (!connected)
            {
                return null;
            }

            var shellProxy = ipcClient.CreateProxy<IPublicShellControlService>();
            var status = await shellProxy.GetShellStatusAsync().ConfigureAwait(false);
            StartupDiagnostics.TraceShellStatus("existing_host_probe", status);
            return status;
        }
        catch (Exception ex)
        {
            Logger.Info($"Existing host status probe did not complete: {ex.Message}");
            return null;
        }
    }

    private static async Task<ExistingHostBehaviorResult> ApplyExistingHostBehaviorAsync(
        LanMountainDesktopIpcClient ipcClient,
        MultiInstanceLaunchBehavior behavior,
        PublicShellStatus status)
    {
        try
        {
            var shellProxy = ipcClient.CreateProxy<IPublicShellControlService>();
            return behavior switch
            {
                MultiInstanceLaunchBehavior.OpenDesktopSilently => await ActivateExistingHostForBehaviorAsync(
                    shellProxy,
                    showLauncherNotice: false,
                    successCode: "existing_host_activated",
                    successMessage: "Launcher activated the existing desktop instance.",
                    failureCode: "existing_host_activation_failed").ConfigureAwait(false),

                MultiInstanceLaunchBehavior.NotifyAndOpenDesktop => await ActivateExistingHostForBehaviorAsync(
                    shellProxy,
                    showLauncherNotice: true,
                    successCode: "existing_host_activated_with_notice",
                    successMessage: "Launcher activated the existing desktop instance and showed the repeated-launch notice.",
                    failureCode: "existing_host_activation_failed").ConfigureAwait(false),

                MultiInstanceLaunchBehavior.PromptOnly => await ShowPromptOnlyExistingHostAsync(
                    shellProxy,
                    status).ConfigureAwait(false),

                MultiInstanceLaunchBehavior.RestartApp => await RestartExistingHostAsync(shellProxy).ConfigureAwait(false),

                _ => await ActivateExistingHostForBehaviorAsync(
                    shellProxy,
                    showLauncherNotice: true,
                    successCode: "existing_host_activated_with_notice",
                    successMessage: "Launcher activated the existing desktop instance and showed the repeated-launch notice.",
                    failureCode: "existing_host_activation_failed").ConfigureAwait(false)
            };
        }
        catch (Exception ex)
        {
            Logger.Warn($"Failed to apply multi-instance behavior '{behavior}': {ex.Message}");
            return new ExistingHostBehaviorResult(
                false,
                "multi_instance_behavior_failed",
                $"Failed to apply multi-instance behavior '{behavior}': {ex.Message}",
                null);
        }
    }

    private static async Task<ExistingHostBehaviorResult> ActivateExistingHostForBehaviorAsync(
        IPublicShellControlService shellProxy,
        bool showLauncherNotice,
        string successCode,
        string successMessage,
        string failureCode)
    {
        var activation = await shellProxy.ActivateMainWindowWithStatusAsync().ConfigureAwait(false);
        var success = activation.Accepted || HostActivationPolicy.IsRecoverableActivationFailure(activation);
        if (showLauncherNotice && success)
        {
            var promptResult = await ShowMultiInstancePromptAsync(activation.Status).ConfigureAwait(false);
            if (promptResult == MultiInstancePromptResult.OpenDesktop)
            {
                activation = await shellProxy.ActivateMainWindowWithStatusAsync().ConfigureAwait(false);
            }
        }

        return new ExistingHostBehaviorResult(
            success,
            activation.Accepted ? successCode : success ? "existing_host_startup_pending" : failureCode,
            activation.Accepted ? successMessage : activation.Message,
            activation);
    }

    private static async Task<ExistingHostBehaviorResult> RestartExistingHostAsync(
        IPublicShellControlService shellProxy)
    {
        var accepted = await shellProxy.RestartAsync().ConfigureAwait(false);
        return new ExistingHostBehaviorResult(
            accepted,
            accepted ? "existing_host_restart_requested" : "existing_host_restart_failed",
            accepted
                ? "Launcher requested the existing desktop instance to restart."
                : "Launcher could not request restart from the existing desktop instance.",
            null);
    }

    private static async Task<ExistingHostBehaviorResult> ShowPromptOnlyExistingHostAsync(
        IPublicShellControlService shellProxy,
        PublicShellStatus status)
    {
        var promptResult = await ShowMultiInstancePromptAsync(status).ConfigureAwait(false);

        if (promptResult == MultiInstancePromptResult.OpenDesktop)
        {
            return await ActivateExistingHostForBehaviorAsync(
                shellProxy,
                showLauncherNotice: false,
                successCode: "existing_host_activated_from_prompt",
                successMessage: "Launcher activated the existing desktop instance from the prompt.",
                failureCode: "existing_host_activation_failed").ConfigureAwait(false);
        }

        return new ExistingHostBehaviorResult(
            true,
            "existing_host_prompt_only",
            "Launcher showed the repeated-launch prompt and did not open the desktop automatically.",
            null);
    }

    private static async Task<PublicShellActivationResult?> TryActivateExistingHostWithStatusAsync(
        LanMountainDesktopIpcClient ipcClient,
        TimeSpan timeout)
    {
        try
        {
            var connected = ipcClient.IsConnected ||
                            await PublicIpcConnection.TryConnectAsync(ipcClient, timeout).ConfigureAwait(false);
            if (!connected)
            {
                return null;
            }

            var shellProxy = ipcClient.CreateProxy<IPublicShellControlService>();
            return await shellProxy.ActivateMainWindowWithStatusAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.Info($"Existing host activation probe did not complete: {ex.Message}");
            return null;
        }
    }

    private static async Task<PublicShellStatus?> TryGetPublicShellStatusAsync(
        LanMountainDesktopIpcClient ipcClient) =>
        await HostStartupMonitor.TryGetPublicShellStatusAsync(ipcClient).ConfigureAwait(false);

    private static async Task<StartupSuccessState?> TryRecoverActivationThroughExistingHostAsync(
        LanMountainDesktopIpcClient ipcClient,
        StartupSuccessTracker startupSuccessTracker,
        TimeSpan timeout) =>
        await HostStartupMonitor.TryRecoverActivationThroughExistingHostAsync(
            ipcClient,
            startupSuccessTracker,
            timeout).ConfigureAwait(false);

    private static Dictionary<string, string> BuildAttemptDetails(
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

    private static bool TryGetLiveProcess(int processId, out Process? process)
    {
        process = null;
        if (processId <= 0)
        {
            return false;
        }

        try
        {
            process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch
        {
            process?.Dispose();
            process = null;
            return false;
        }
    }

    private enum HostStartMode
    {
        ShellExecute,
        Direct
    }

    private sealed record HostStartAttempt(
        HostStartMode StartMode,
        bool ProcessCreated,
        Process? Process,
        bool ExitedEarly,
        int? ExitCode,
        string? FailureReason,
        string? PackageRoot,
        string? WorkingDirectory,
        string? Arguments)
    {
        public int? ProcessId => Process?.Id;

        public static HostStartAttempt Started(HostStartMode startMode, Process process, HostLaunchPlan plan) =>
            new(
                startMode,
                true,
                process,
                false,
                null,
                null,
                plan.PackageRoot,
                plan.WorkingDirectory,
                HostLaunchPlanBuilder.FormatArgumentsForLog(plan.Arguments));

        public static HostStartAttempt EarlyExit(HostStartMode startMode, Process process, int exitCode, HostLaunchPlan plan) =>
            new(
                startMode,
                true,
                process,
                true,
                exitCode,
                null,
                plan.PackageRoot,
                plan.WorkingDirectory,
                HostLaunchPlanBuilder.FormatArgumentsForLog(plan.Arguments));

        public static HostStartAttempt StartFailed(HostStartMode startMode, string failureReason, HostLaunchPlan? plan = null) =>
            new(
                startMode,
                false,
                null,
                false,
                null,
                failureReason,
                plan?.PackageRoot,
                plan?.WorkingDirectory,
                plan is null ? null : HostLaunchPlanBuilder.FormatArgumentsForLog(plan.Arguments));
    }

    private sealed record ExistingHostBehaviorResult(
        bool Success,
        string Code,
        string Message,
        PublicShellActivationResult? ActivationResult);

    private sealed record HostLaunchOutcome(
        LauncherResult Result,
        Process? Process,
        LauncherResult? ImmediateResult,
        Dictionary<string, string> Details)
    {
        public static HostLaunchOutcome FromResult(LauncherResult result) =>
            new(result, null, result.Success ? result : null, result.Details);

        public static HostLaunchOutcome FromImmediateResult(LauncherResult result) =>
            new(result, null, result, result.Details);

        public static HostLaunchOutcome FromProcess(Process process, LauncherResult result, Dictionary<string, string> details) =>
            new(result, process, null, details);
    }
}
