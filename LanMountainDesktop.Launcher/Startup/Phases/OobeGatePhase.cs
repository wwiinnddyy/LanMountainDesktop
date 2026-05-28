using Avalonia.Threading;

namespace LanMountainDesktop.Launcher.Startup;

internal sealed class OobeGatePhase : ILaunchPhase
{
    public string Name => nameof(OobeGatePhase);

    public async Task<LaunchPhaseResult> ExecuteAsync(LaunchContext context, CancellationToken cancellationToken = default)
    {
        if (context.OobeDecision.ShouldShowOobe)
        {
            await Dispatcher.UIThread.InvokeAsync(() => context.SplashWindow.Hide());
            foreach (var step in context.OobeSteps)
            {
                await step.RunAsync(cancellationToken).ConfigureAwait(false);
            }

            await Dispatcher.UIThread.InvokeAsync(() => context.SplashWindow.Show());
        }

        return new LaunchPhaseResult(LaunchPhaseStatus.Continue);
    }
}
