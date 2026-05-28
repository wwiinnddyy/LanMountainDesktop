namespace LanMountainDesktop.Launcher.Startup;

internal sealed class MonitorStartupPhase : ILaunchPhase
{
    public string Name => nameof(MonitorStartupPhase);

    public async Task<LaunchPhaseResult> ExecuteAsync(LaunchContext context, CancellationToken cancellationToken = default)
    {
        var launchOutcome = context.LaunchOutcome
            ?? throw new InvalidOperationException("LaunchHostPhase must run before MonitorStartupPhase.");

        if (launchOutcome.Process is null)
        {
            return new LaunchPhaseResult(
                LaunchPhaseStatus.Completed,
                LaunchResultBuilder.BuildFailure("launch", "host_start_failed", "Host process is missing."));
        }

        Dictionary<string, string> ComposeLaunchDetails(bool hostProcessAlive, bool recoveryActivationAttempted = false) =>
            LaunchResultBuilder.MergeDetails(
                context.LauncherContextDetails,
                LaunchResultBuilder.MergeDetails(
                    launchOutcome.Details,
                    LaunchAttemptDetails.Build(
                        context.TrackedAttempt,
                        context.AttachedToExistingAttempt,
                        context.IpcConnected,
                        hostProcessAlive,
                        context.LastStage,
                        context.LastStageMessage,
                        context.ActivationFailureReason,
                        context.SoftTimeoutShown,
                        recoveryActivationAttempted)));

        var monitor = new HostStartupMonitor();
        var monitorOutcome = await monitor.MonitorUntilCompleteAsync(new HostStartupMonitor.Request(
            launchOutcome.Process,
            context.IpcClient,
            context.SuccessTracker,
            context.StartupAttemptRegistry,
            context.TrackedAttempt,
            context.AttachedToExistingAttempt,
            context.LauncherContextDetails,
            context.SuccessTcs,
            context.ActivationFailedTcs,
            context.Reporter,
            context.LoadingDetailsWindow,
            context.LoadingState,
            context.LastStage,
            context.LastStageMessage,
            context.IpcConnected,
            context.ActivationFailureReason,
            context.SoftTimeoutShown,
            context.PublishCoordinatorStatus,
            ComposeLaunchDetails)).ConfigureAwait(false);

        context.WindowsClosingByOrchestrator = true;
        await LaunchUiPresenter.CloseWindowsAsync(context.SplashWindow, context.LoadingDetailsWindow).ConfigureAwait(false);
        return new LaunchPhaseResult(
            LaunchPhaseStatus.Completed,
            LaunchResultBuilder.Build(
                monitorOutcome.Success,
                "launch",
                monitorOutcome.Code,
                monitorOutcome.Message,
                monitorOutcome.Details));
    }
}
