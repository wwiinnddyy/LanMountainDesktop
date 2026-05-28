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
                await step.RunAsync(cancellationToken).ConfigureAwait(false);
            }

            await LaunchUiPresenter.ShowSplashAsync(context.SplashWindow).ConfigureAwait(false);
        }

        return new LaunchPhaseResult(LaunchPhaseStatus.Continue);
    }
}
