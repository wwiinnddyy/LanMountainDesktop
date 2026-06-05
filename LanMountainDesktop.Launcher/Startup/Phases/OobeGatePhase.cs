using LanMountainDesktop.Launcher.Shell;

namespace LanMountainDesktop.Launcher.Startup;

internal sealed class OobeGatePhase : ILaunchPhase
{
    public string Name => nameof(OobeGatePhase);

    public async Task<LaunchPhaseResult> ExecuteAsync(LaunchContext context, CancellationToken cancellationToken = default)
    {
        if (context.OobeDecision.ShouldShowOobe)
        {
            await LaunchUiPresenter.HideSplashAsync(context.SplashWindow).ConfigureAwait(false);
            foreach (var step in context.OobeSteps)
            {
                var stepResult = await step.RunAsync(cancellationToken).ConfigureAwait(false);
                if (!stepResult.ContinueLaunch)
                {
                    context.WindowsClosingByOrchestrator = true;
                    await LaunchUiPresenter.CloseWindowsAsync(context.SplashWindow, context.LoadingDetailsWindow).ConfigureAwait(false);
                    return new LaunchPhaseResult(
                        LaunchPhaseStatus.Completed,
                        stepResult.Result ?? LaunchResultBuilder.BuildFailure(
                            "oobe",
                            "oobe_cancelled",
                            "OOBE did not complete."));
                }
            }

            await LaunchUiPresenter.ShowSplashAsync(context.SplashWindow).ConfigureAwait(false);
        }

        return new LaunchPhaseResult(LaunchPhaseStatus.Continue);
    }
}
