using LanMountainDesktop.Shared.Contracts.Launcher;

namespace LanMountainDesktop.Launcher.Startup;

internal sealed class ExistingHostProbePhase : ILaunchPhase
{
    public string Name => nameof(ExistingHostProbePhase);

    public async Task<LaunchPhaseResult> ExecuteAsync(LaunchContext context, CancellationToken cancellationToken = default)
    {
        if (!HostActivationPolicy.ShouldProbeExistingHostBeforeLaunch(context.CommandContext))
        {
            return new LaunchPhaseResult(LaunchPhaseStatus.Continue);
        }

        var multiInstanceBehavior = ExistingHostProbe.LoadMultiInstanceLaunchBehavior(context.DataLocationResolver);
        var existingShellStatus = await ExistingHostProbe.TryGetExistingHostStatusAsync(
            context.IpcClient,
            StartupTimeoutPolicy.ExistingHostProbeTimeout).ConfigureAwait(false);

        if (!HostActivationPolicy.IsExistingHostReadyForLauncherDecision(existingShellStatus))
        {
            return new LaunchPhaseResult(LaunchPhaseStatus.Continue);
        }

        context.IpcConnected = true;
        context.ShellStatus = existingShellStatus;
        var decisionResult = await ExistingHostProbe.ApplyExistingHostBehaviorAsync(
            context.IpcClient,
            multiInstanceBehavior,
            existingShellStatus!).ConfigureAwait(false);
        context.ShellStatus = decisionResult.ActivationResult?.Status ?? existingShellStatus;
        var recoverableActivationFailure = decisionResult.ActivationResult is not null &&
                                           HostActivationPolicy.IsRecoverableActivationFailure(decisionResult.ActivationResult);
        context.LastStage = decisionResult.Success || recoverableActivationFailure
            ? StartupStage.ActivationRedirected
            : StartupStage.ActivationFailed;
        context.LastStageMessage = decisionResult.Message;
        if (decisionResult.Success || recoverableActivationFailure)
        {
            context.StartupAttemptRegistry.MarkOwnedSucceeded(context.LastStage, context.LastStageMessage);
        }
        else
        {
            context.StartupAttemptRegistry.MarkOwnedFailed(context.LastStage, context.LastStageMessage);
        }

        context.PublishCoordinatorStatus(true, true, decisionResult.Success);
        context.WindowsClosingByOrchestrator = true;
        await LaunchUiPresenter.CloseWindowsAsync(context.SplashWindow, context.LoadingDetailsWindow).ConfigureAwait(false);
        return new LaunchPhaseResult(
            LaunchPhaseStatus.Completed,
            LaunchResultBuilder.Build(
                decisionResult.Success,
                "launch",
                decisionResult.Code,
                decisionResult.Message,
                LaunchResultBuilder.MergeDetails(
                    context.LauncherContextDetails,
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["publicIpcConnected"] = "true",
                        ["multiInstanceBehavior"] = multiInstanceBehavior.ToString(),
                        ["existingHostPid"] = context.ShellStatus?.ProcessId.ToString() ?? string.Empty,
                        ["existingShellState"] = context.ShellStatus?.ShellState ?? string.Empty,
                        ["existingTrayState"] = context.ShellStatus?.Tray.State ?? string.Empty,
                        ["existingTaskbarUsable"] = context.ShellStatus?.Taskbar.IsUsable.ToString() ?? string.Empty,
                        ["activationAccepted"] = decisionResult.ActivationResult?.Accepted.ToString() ?? string.Empty
                    })));
    }
}
