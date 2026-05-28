using LanMountainDesktop.Launcher.Models;
using LanMountainDesktop.Shared.Contracts.Launcher;

namespace LanMountainDesktop.Launcher.Startup;

internal sealed class LaunchHostPhase : ILaunchPhase
{
    private static readonly string SoftTimeoutStatusMessage = Resources.Strings.Coordinator_SlowDeviceMessage;
    private static readonly string SoftTimeoutDetailsMessage = Resources.Strings.Coordinator_RunningHostMessage;

    private readonly HostLaunchService _hostLaunchService = new();

    public string Name => nameof(LaunchHostPhase);

    public async Task<LaunchPhaseResult> ExecuteAsync(LaunchContext context, CancellationToken cancellationToken = default)
    {
        context.Reporter.Report("launch", "Launching desktop...");
        var startupSuccessTracker = context.SuccessTracker;
        var attachableAttempt = context.StartupAttemptRegistry.TryGetAttachableAttempt(
            context.CommandContext.LaunchSource,
            startupSuccessTracker.PolicyKey);

        HostLaunchOutcome launchOutcome;
        if (attachableAttempt is not null &&
            context.StartupAttemptRegistry.AdoptAttempt(attachableAttempt.AttemptId) &&
            LaunchResultBuilder.TryGetLiveProcess(attachableAttempt.HostPid, out var attachedProcess))
        {
            context.TrackedAttempt = attachableAttempt;
            context.AttachedToExistingAttempt = true;
            context.IpcConnected = attachableAttempt.IpcConnected;
            context.LastStage = attachableAttempt.LastObservedStage;
            context.LastStageMessage = string.IsNullOrWhiteSpace(attachableAttempt.LastObservedMessage)
                ? "Attached to the existing startup attempt."
                : attachableAttempt.LastObservedMessage;
            context.Reporter.Report(
                LaunchUiPresenter.MapStartupStageToSplashStage(context.LastStage),
                context.LastStageMessage);
            context.PublishCoordinatorStatus(true, false, false);

            if (startupSuccessTracker.TryResolve(context.LastStage, out var attachedSuccessState))
            {
                context.WindowsClosingByOrchestrator = true;
                context.StartupAttemptRegistry.MarkOwnedSucceeded(attachedSuccessState.Stage, attachedSuccessState.Message);
                await LaunchUiPresenter.CloseWindowsAsync(context.SplashWindow, context.LoadingDetailsWindow).ConfigureAwait(false);
                return new LaunchPhaseResult(
                    LaunchPhaseStatus.Completed,
                    LaunchResultBuilder.Build(
                        true,
                        "launch",
                        attachedSuccessState.Code,
                        attachedSuccessState.Message,
                        LaunchResultBuilder.MergeDetails(
                            context.LauncherContextDetails,
                            LaunchAttemptDetails.Build(
                                context.TrackedAttempt,
                                context.AttachedToExistingAttempt,
                                context.IpcConnected,
                                hostProcessAlive: true,
                                context.LastStage,
                                context.LastStageMessage,
                                context.ActivationFailureReason,
                                softTimeoutShown: false,
                                recoveryActivationAttempted: false))));
            }

            if (attachableAttempt.State is StartupAttemptState.SoftTimeout or StartupAttemptState.DetachedWaiting)
            {
                context.SoftTimeoutShown = true;
                context.Reporter.Report("delayed", SoftTimeoutStatusMessage);
                context.LoadingState = HostStartupMonitor.BuildDelayedLoadingState(
                    context.LoadingState,
                    SoftTimeoutStatusMessage,
                    SoftTimeoutDetailsMessage,
                    context.TrackedAttempt!.StartedAtUtc);
                context.LoadingDetailsWindow?.UpdateLoadingState(context.LoadingState);
            }

            launchOutcome = HostLaunchOutcome.FromProcess(
                attachedProcess!,
                LaunchResultBuilder.Build(
                    true,
                    "launchHost",
                    "attached_attempt",
                    "Attached to an existing startup attempt.",
                    LaunchAttemptDetails.Build(
                        context.TrackedAttempt,
                        context.AttachedToExistingAttempt,
                        context.IpcConnected,
                        hostProcessAlive: true,
                        context.LastStage,
                        context.LastStageMessage,
                        context.ActivationFailureReason,
                        context.SoftTimeoutShown,
                        recoveryActivationAttempted: false)),
                LaunchAttemptDetails.Build(
                    context.TrackedAttempt,
                    context.AttachedToExistingAttempt,
                    context.IpcConnected,
                    hostProcessAlive: true,
                    context.LastStage,
                    context.LastStageMessage,
                    context.ActivationFailureReason,
                    context.SoftTimeoutShown,
                    recoveryActivationAttempted: false));
        }
        else
        {
            launchOutcome = await _hostLaunchService.LaunchAsync(context).ConfigureAwait(false);
        }

        context.LaunchOutcome = launchOutcome;

        if (!launchOutcome.Result.Success)
        {
            return new LaunchPhaseResult(
                LaunchPhaseStatus.Completed,
                LaunchResultBuilder.WithAdditionalDetails(launchOutcome.Result, context.LauncherContextDetails));
        }

        if (launchOutcome.ImmediateResult is not null)
        {
            context.WindowsClosingByOrchestrator = true;
            await LaunchUiPresenter.CloseWindowsAsync(context.SplashWindow, context.LoadingDetailsWindow).ConfigureAwait(false);
            return new LaunchPhaseResult(
                LaunchPhaseStatus.Completed,
                LaunchResultBuilder.WithAdditionalDetails(launchOutcome.ImmediateResult, context.LauncherContextDetails));
        }

        if (launchOutcome.Process is null)
        {
            return new LaunchPhaseResult(
                LaunchPhaseStatus.Completed,
                LaunchResultBuilder.Build(
                    success: false,
                    stage: "launch",
                    code: "host_start_failed",
                    message: "Host launch did not create a process.",
                    details: LaunchResultBuilder.MergeDetails(
                        context.LauncherContextDetails,
                        LaunchResultBuilder.MergeDetails(
                            launchOutcome.Details,
                            LaunchAttemptDetails.Build(
                                context.TrackedAttempt,
                                context.AttachedToExistingAttempt,
                                context.IpcConnected,
                                hostProcessAlive: false,
                                context.LastStage,
                                context.LastStageMessage,
                                context.ActivationFailureReason,
                                context.SoftTimeoutShown,
                                recoveryActivationAttempted: false)))));
        }

        if (!context.AttachedToExistingAttempt)
        {
            var reservedAttempt = context.StartupAttemptRegistry.GetOwnedAttempt();
            context.TrackedAttempt = reservedAttempt is { ReservedBeforeHostStart: true }
                ? context.StartupAttemptRegistry.AssignOwnedHostProcess(
                    launchOutcome.Process.Id,
                    context.LastStage,
                    context.LastStageMessage)
                : context.StartupAttemptRegistry.StartOwnedAttempt(
                    launchOutcome.Process.Id,
                    context.CommandContext.LaunchSource,
                    startupSuccessTracker.PolicyKey,
                    context.LastStage,
                    context.LastStageMessage);
            context.PublishCoordinatorStatus(true, false, false);
        }

        return new LaunchPhaseResult(LaunchPhaseStatus.Continue);
    }
}
