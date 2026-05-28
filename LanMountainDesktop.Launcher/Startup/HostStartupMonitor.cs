using System.Diagnostics;
using LanMountainDesktop.Launcher.Models;
using LanMountainDesktop.Launcher.Resources;
using LanMountainDesktop.Launcher.Views;
using LanMountainDesktop.Shared.Contracts.Launcher;
using LanMountainDesktop.Shared.IPC;
using LanMountainDesktop.Shared.IPC.Abstractions.Services;

namespace LanMountainDesktop.Launcher.Startup;

internal sealed class HostStartupMonitor
{
    private static readonly string SoftTimeoutStatusMessage = Strings.Coordinator_SlowDeviceMessage;
    private static readonly string SoftTimeoutDetailsMessage = Strings.Coordinator_RunningHostMessage;

    internal sealed record Request(
        Process HostProcess,
        LanMountainDesktopIpcClient IpcClient,
        StartupSuccessTracker SuccessTracker,
        StartupAttemptRegistry AttemptRegistry,
        StartupAttemptRecord? TrackedAttempt,
        bool AttachedToExistingAttempt,
        Dictionary<string, string> LaunchDetails,
        TaskCompletionSource<StartupSuccessState> SuccessTcs,
        TaskCompletionSource<string> ActivationFailedTcs,
        ISplashStageReporter Reporter,
        LoadingDetailsWindow? LoadingDetailsWindow,
        LoadingStateMessage LoadingState,
        StartupStage LastStage,
        string LastStageMessage,
        bool IpcConnected,
        string ActivationFailureReason,
        bool SoftTimeoutShown,
        Action<bool?, bool, bool> PublishCoordinatorStatus,
        Func<bool, bool, Dictionary<string, string>> ComposeLaunchDetails);

    internal sealed record Outcome(
        bool Success,
        string Code,
        string Message,
        bool RecoveryActivationAttempted,
        Dictionary<string, string> Details);

    public async Task<Outcome> MonitorUntilCompleteAsync(Request request)
    {
        var ipcConnected = request.IpcConnected;
        var softTimeoutShown = request.SoftTimeoutShown;
        var lastStage = request.LastStage;
        var lastStageMessage = request.LastStageMessage;
        var activationFailureReason = request.ActivationFailureReason;
        var loadingState = request.LoadingState;
        PublicShellStatus? shellStatus = null;
        var trackedAttempt = request.TrackedAttempt;

        async Task<StartupSuccessState?> RefreshShellStatusAsync(string waitingMessage)
        {
            if (!request.IpcClient.IsConnected)
            {
                return null;
            }

            ipcConnected = true;
            request.AttemptRegistry.MarkOwnedIpcConnected();
            shellStatus = await TryGetPublicShellStatusAsync(request.IpcClient).ConfigureAwait(false);
            StartupDiagnostics.TraceShellStatus("refresh", shellStatus, lastStage);
            if (request.SuccessTracker.TryResolve(shellStatus, out var successState))
            {
                return successState;
            }

            if (shellStatus is not null && !shellStatus.MainWindowOpened && !shellStatus.DesktopVisible)
            {
                request.AttemptRegistry.MarkOwnedWaitingForShell(waitingMessage);
            }

            request.PublishCoordinatorStatus(true, false, false);
            return null;
        }

        var connected = await PublicIpcConnection.TryConnectWithBackoffAsync(
            request.IpcClient,
            [
                StartupTimeoutPolicy.InitialIpcConnectTimeout,
                TimeSpan.FromMilliseconds(3000),
                TimeSpan.FromMilliseconds(5000)
            ]).ConfigureAwait(false);
        if (!connected)
        {
            Logger.Info("Host public IPC is not ready yet. Launcher will keep monitoring the host process and retry.");
        }
        else
        {
            var shellSuccess = await RefreshShellStatusAsync("Host public IPC is ready; waiting for desktop shell.")
                .ConfigureAwait(false);
            if (shellSuccess is not null)
            {
                request.SuccessTcs.TrySetResult(shellSuccess);
            }
        }

        var processExitTask = request.HostProcess.WaitForExitAsync();
        var startedAt = trackedAttempt?.StartedAtUtc ?? DateTimeOffset.UtcNow;
        var softTimeoutAt = startedAt + StartupTimeoutPolicy.SoftTimeout;
        var hardTimeoutAt = startedAt + StartupTimeoutPolicy.HardTimeout;
        var nextReconnectAttemptAt = DateTimeOffset.UtcNow + StartupTimeoutPolicy.IpcReconnectInterval;
        var nextShellStatusPollAt = DateTimeOffset.UtcNow + StartupTimeoutPolicy.ShellStatusPollInterval;
        var ipcReconnectAttemptIndex = 0;
        var activationRetryAttempted = false;

        while (true)
        {
            if (request.SuccessTcs.Task.IsCompleted)
            {
                var successState = await request.SuccessTcs.Task.ConfigureAwait(false);
                request.AttemptRegistry.MarkOwnedSucceeded(successState.Stage, successState.Message);
                request.PublishCoordinatorStatus(!request.HostProcess.HasExited, true, true);
                return new Outcome(
                    true,
                    successState.Code,
                    successState.Message,
                    false,
                    request.ComposeLaunchDetails(!request.HostProcess.HasExited, false));
            }

            if (request.ActivationFailedTcs.Task.IsCompleted && !activationRetryAttempted)
            {
                activationRetryAttempted = true;
                activationFailureReason = await request.ActivationFailedTcs.Task.ConfigureAwait(false);
                Logger.Warn($"Activation failure received before startup success. Reason='{activationFailureReason}'.");
                var activationRecovery = await TryRecoverActivationThroughExistingHostAsync(
                    request.IpcClient,
                    request.SuccessTracker,
                    TimeSpan.FromSeconds(1)).ConfigureAwait(false);
                if (activationRecovery is not null)
                {
                    request.AttemptRegistry.MarkOwnedSucceeded(activationRecovery.Stage, activationRecovery.Message);
                    request.PublishCoordinatorStatus(!request.HostProcess.HasExited, true, true);
                    return new Outcome(
                        true,
                        activationRecovery.Code,
                        activationRecovery.Message,
                        true,
                        request.ComposeLaunchDetails(!request.HostProcess.HasExited, true));
                }

                Logger.Info("Activation failure did not recover through public IPC yet. Launcher will keep monitoring the current host attempt.");
            }

            if (processExitTask.IsCompleted)
            {
                var exitCode = request.HostProcess.ExitCode;
                Logger.Warn($"Host exited before startup success criteria were met. ExitCode={exitCode}.");

                if (HostActivationPolicy.IsSuccessfulActivationExitCode(exitCode))
                {
                    request.AttemptRegistry.MarkOwnedSucceeded(StartupStage.ActivationRedirected, "Host redirected activation to the existing desktop instance.");
                    request.PublishCoordinatorStatus(false, true, true);
                    return new Outcome(
                        true,
                        "activation_redirected",
                        "Host redirected activation to the existing desktop instance.",
                        false,
                        MergeExitCodeDetails(request.ComposeLaunchDetails(false, false), exitCode));
                }

                if (!activationRetryAttempted && HostActivationPolicy.IsFailedActivationExitCode(exitCode))
                {
                    activationRetryAttempted = true;
                    var activationRecovery = await TryRecoverActivationThroughExistingHostAsync(
                        request.IpcClient,
                        request.SuccessTracker,
                        TimeSpan.FromSeconds(2)).ConfigureAwait(false);
                    if (activationRecovery is not null)
                    {
                        request.AttemptRegistry.MarkOwnedSucceeded(activationRecovery.Stage, activationRecovery.Message);
                        request.PublishCoordinatorStatus(true, true, true);
                        return new Outcome(
                            true,
                            activationRecovery.Code,
                            activationRecovery.Message,
                            true,
                            MergeExitCodeDetails(request.ComposeLaunchDetails(true, true), exitCode));
                    }

                    Logger.Info("Activation exit code did not recover through public IPC. Launcher will report the activation failure without launching another host.");
                }

                request.AttemptRegistry.MarkOwnedFailed(lastStage, activationFailureReason);
                request.PublishCoordinatorStatus(false, true, false);
                return new Outcome(
                    false,
                    HostActivationPolicy.IsFailedActivationExitCode(exitCode) ? "activation_failed" : "host_exited_early",
                    HostActivationPolicy.IsFailedActivationExitCode(exitCode)
                        ? $"Host activation handshake failed before the required startup state was reported. ExitCode={exitCode}."
                        : $"Host exited before the required startup state was reported. ExitCode={exitCode}.",
                    false,
                    MergeExitCodeDetails(request.ComposeLaunchDetails(false, false), exitCode));
            }

            var now = DateTimeOffset.UtcNow;
            if (ipcConnected &&
                !request.HostProcess.HasExited &&
                now >= nextShellStatusPollAt)
            {
                var shellSuccess = await RefreshShellStatusAsync("Host public IPC is ready; waiting for desktop shell.")
                    .ConfigureAwait(false);
                if (shellSuccess is not null)
                {
                    request.SuccessTcs.TrySetResult(shellSuccess);
                    continue;
                }

                nextShellStatusPollAt = DateTimeOffset.UtcNow + StartupTimeoutPolicy.ShellStatusPollInterval;
            }

            if (!ipcConnected &&
                !request.HostProcess.HasExited &&
                now >= nextReconnectAttemptAt)
            {
                var reconnectTimeout = StartupTimeoutPolicy.IpcReconnectAttemptTimeouts[
                    Math.Min(ipcReconnectAttemptIndex, StartupTimeoutPolicy.IpcReconnectAttemptTimeouts.Length - 1)];
                ipcReconnectAttemptIndex++;
                connected = await PublicIpcConnection.TryConnectAsync(request.IpcClient, reconnectTimeout).ConfigureAwait(false);
                if (connected)
                {
                    ipcConnected = true;
                    var shellSuccess = await RefreshShellStatusAsync("Host public IPC reconnected; waiting for desktop shell.")
                        .ConfigureAwait(false);
                    if (shellSuccess is not null)
                    {
                        request.SuccessTcs.TrySetResult(shellSuccess);
                        continue;
                    }
                }

                nextReconnectAttemptAt = DateTimeOffset.UtcNow + StartupTimeoutPolicy.IpcReconnectInterval;
            }

            if (!softTimeoutShown &&
                now >= softTimeoutAt &&
                (!request.HostProcess.HasExited || ipcConnected))
            {
                softTimeoutShown = true;
                request.AttemptRegistry.MarkOwnedSoftTimeout(SoftTimeoutStatusMessage);
                request.Reporter.Report("delayed", SoftTimeoutStatusMessage);
                loadingState = BuildDelayedLoadingState(
                    loadingState,
                    SoftTimeoutStatusMessage,
                    SoftTimeoutDetailsMessage,
                    trackedAttempt?.StartedAtUtc ?? startedAt);
                request.LoadingDetailsWindow?.UpdateLoadingState(loadingState);
                request.PublishCoordinatorStatus(!request.HostProcess.HasExited, false, false);
            }

            if (now >= hardTimeoutAt)
            {
                break;
            }

            var nextCheckpointAt = hardTimeoutAt;
            if (!softTimeoutShown && softTimeoutAt < nextCheckpointAt)
            {
                nextCheckpointAt = softTimeoutAt;
            }

            var delay = nextCheckpointAt - now;
            if (delay > TimeSpan.FromSeconds(1))
            {
                delay = TimeSpan.FromSeconds(1);
            }
            else if (delay < TimeSpan.FromMilliseconds(100))
            {
                delay = TimeSpan.FromMilliseconds(100);
            }

            await Task.WhenAny(
                request.SuccessTcs.Task,
                request.ActivationFailedTcs.Task,
                processExitTask,
                Task.Delay(delay)).ConfigureAwait(false);
        }

        var recoveryActivationAttempted = false;
        if (!connected && !request.HostProcess.HasExited)
        {
            connected = await PublicIpcConnection.TryConnectAsync(request.IpcClient, TimeSpan.FromSeconds(3)).ConfigureAwait(false);
            if (connected)
            {
                var shellSuccess = await RefreshShellStatusAsync("Host public IPC is ready; waiting for desktop shell.")
                    .ConfigureAwait(false);
                if (shellSuccess is not null)
                {
                    request.AttemptRegistry.MarkOwnedSucceeded(shellSuccess.Stage, shellSuccess.Message);
                    request.PublishCoordinatorStatus(true, true, true);
                    return new Outcome(
                        true,
                        shellSuccess.Code,
                        shellSuccess.Message,
                        false,
                        request.ComposeLaunchDetails(true, false));
                }
            }
        }

        if (connected && !request.HostProcess.HasExited)
        {
            recoveryActivationAttempted = true;
            var recoveryOutcome = await TryRecoverWithPublicActivationAsync(
                request.IpcClient,
                request.HostProcess,
                request.SuccessTcs.Task,
                request.SuccessTracker).ConfigureAwait(false);
            if (recoveryOutcome is not null)
            {
                request.AttemptRegistry.MarkOwnedSucceeded(recoveryOutcome.Stage, recoveryOutcome.Message);
                request.PublishCoordinatorStatus(!request.HostProcess.HasExited, true, true);
                return new Outcome(
                    true,
                    recoveryOutcome.Code,
                    recoveryOutcome.Message,
                    true,
                    request.ComposeLaunchDetails(!request.HostProcess.HasExited, true));
            }
        }

        if (connected && !request.HostProcess.HasExited)
        {
            request.AttemptRegistry.MarkOwnedWaitingForShell("Host process is still running after the launcher wait window.");
            shellStatus = await TryGetPublicShellStatusAsync(request.IpcClient).ConfigureAwait(false);
            if (request.SuccessTracker.TryResolve(shellStatus, out var finalShellSuccess))
            {
                request.AttemptRegistry.MarkOwnedSucceeded(finalShellSuccess.Stage, finalShellSuccess.Message);
                request.PublishCoordinatorStatus(true, true, true);
                return new Outcome(
                    true,
                    finalShellSuccess.Code,
                    finalShellSuccess.Message,
                    recoveryActivationAttempted,
                    request.ComposeLaunchDetails(true, recoveryActivationAttempted));
            }

            request.PublishCoordinatorStatus(true, true, false);
            return new Outcome(
                false,
                "shell_not_ready",
                "Host public IPC is connected, but the desktop shell did not create or show the main window in time.",
                recoveryActivationAttempted,
                request.ComposeLaunchDetails(true, recoveryActivationAttempted));
        }

        if (!connected && !request.HostProcess.HasExited)
        {
            request.AttemptRegistry.MarkOwnedWaitingForShell("Host process is still running, but public IPC is not ready yet.");
            request.PublishCoordinatorStatus(true, false, true);
            return new Outcome(
                true,
                "startup_pending",
                "Host process is still running; Launcher will not start another process while public IPC finishes startup.",
                recoveryActivationAttempted,
                request.ComposeLaunchDetails(true, recoveryActivationAttempted));
        }

        request.AttemptRegistry.MarkOwnedFailed(lastStage, activationFailureReason);
        request.PublishCoordinatorStatus(!request.HostProcess.HasExited, true, false);
        return new Outcome(
            false,
            "desktop_not_visible",
            $"Host process started, but it never reached the required startup state within {StartupTimeoutPolicy.HardTimeout.TotalSeconds:0} seconds.",
            recoveryActivationAttempted,
            request.ComposeLaunchDetails(!request.HostProcess.HasExited, recoveryActivationAttempted));
    }

    internal static async Task<StartupSuccessState?> TryRecoverActivationThroughExistingHostAsync(
        LanMountainDesktopIpcClient ipcClient,
        StartupSuccessTracker startupSuccessTracker,
        TimeSpan timeout)
    {
        var activation = await TryActivateExistingHostWithStatusAsync(ipcClient, timeout).ConfigureAwait(false);
        if (activation is null)
        {
            return null;
        }

        if (startupSuccessTracker.TryResolve(activation.Status, out var shellSuccess))
        {
            return shellSuccess;
        }

        if (activation.Accepted)
        {
            return startupSuccessTracker.BuildRecoverySuccessState();
        }

        return HostActivationPolicy.IsRecoverableActivationFailure(activation)
            ? new StartupSuccessState(
                StartupStage.Ready,
                "startup_pending",
                activation.Message)
            : null;
    }

    internal static async Task<PublicShellStatus?> TryGetPublicShellStatusAsync(LanMountainDesktopIpcClient ipcClient)
    {
        try
        {
            var shellProxy = ipcClient.CreateProxy<IPublicShellControlService>();
            return await shellProxy.GetShellStatusAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.Warn($"Failed to query public shell status: {ex.Message}");
            return null;
        }
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

    private static async Task<StartupSuccessState?> TryRecoverWithPublicActivationAsync(
        LanMountainDesktopIpcClient ipcClient,
        Process hostProcess,
        Task<StartupSuccessState> successTask,
        StartupSuccessTracker startupSuccessTracker)
    {
        try
        {
            var shellProxy = ipcClient.CreateProxy<IPublicShellControlService>();
            var activation = await shellProxy.ActivateMainWindowWithStatusAsync().ConfigureAwait(false);
            StartupDiagnostics.TraceShellStatus("recovery_activation", activation.Status);
            if (startupSuccessTracker.TryResolve(activation.Status, out var shellSuccess))
            {
                return shellSuccess;
            }

            var completedTask = await Task.WhenAny(successTask, Task.Delay(TimeSpan.FromSeconds(5))).ConfigureAwait(false);
            if (completedTask == successTask)
            {
                return await successTask.ConfigureAwait(false);
            }

            if (!hostProcess.HasExited && (activation.Accepted || HostActivationPolicy.IsRecoverableActivationFailure(activation)))
            {
                return startupSuccessTracker.BuildRecoverySuccessState();
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"Public activation recovery failed: {ex.Message}");
        }

        return null;
    }

    internal static LoadingStateMessage BuildDelayedLoadingState(
        LoadingStateMessage loadingState,
        string summaryMessage,
        string detailMessage,
        DateTimeOffset startedAtUtc)
    {
        var delayedItems = loadingState.ActiveItems
            .Where(item => !string.Equals(item.Id, "launcher-soft-timeout", StringComparison.OrdinalIgnoreCase))
            .ToList();

        delayedItems.Insert(0, new LoadingItem
        {
            Id = "launcher-soft-timeout",
            Type = LoadingItemType.System,
            Name = "Startup still in progress",
            Description = detailMessage,
            State = LoadingState.Delayed,
            ProgressPercent = Math.Max(loadingState.OverallProgressPercent, 1),
            Message = detailMessage,
            StartTime = startedAtUtc
        });

        return loadingState with
        {
            ActiveItems = delayedItems,
            Message = summaryMessage,
            Timestamp = DateTimeOffset.UtcNow,
            TotalCount = Math.Max(loadingState.TotalCount, delayedItems.Count)
        };
    }

    private static Dictionary<string, string> MergeExitCodeDetails(Dictionary<string, string> details, int exitCode)
    {
        details["exitCode"] = exitCode.ToString();
        return details;
    }
}
