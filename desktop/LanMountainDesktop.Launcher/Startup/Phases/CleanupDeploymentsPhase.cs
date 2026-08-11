namespace LanMountainDesktop.Launcher.Startup;

internal sealed class CleanupDeploymentsPhase : ILaunchPhase
{
    public string Name => nameof(CleanupDeploymentsPhase);

    public Task<LaunchPhaseResult> ExecuteAsync(LaunchContext context, CancellationToken cancellationToken = default)
    {
        context.DeploymentLocator.CleanupOldDeployments(minVersionsToKeep: 3);
        context.OobeDecision = context.OobeStateService.Evaluate(context.CommandContext);
        context.LauncherContextDetails = LaunchResultBuilder.BuildLauncherContextDetails(
            context.CommandContext,
            context.OobeDecision,
            context.DeploymentLocator.GetAppRoot());
        return Task.FromResult(new LaunchPhaseResult(LaunchPhaseStatus.Continue));
    }
}
