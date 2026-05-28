namespace LanMountainDesktop.Launcher.Startup;

internal sealed class ApplyPendingUpdatePhase : ILaunchPhase
{
    public string Name => nameof(ApplyPendingUpdatePhase);

    public async Task<LaunchPhaseResult> ExecuteAsync(LaunchContext context, CancellationToken cancellationToken = default)
    {
        context.Reporter.Report("update", "Checking updates...");
        var updateResult = await context.UpdateEngine.ApplyPendingUpdateAsync().ConfigureAwait(false);
        if (!updateResult.Success)
        {
            Logger.Warn($"Update apply failed, will try to launch existing version. Error='{updateResult.Message}'.");
            context.Reporter.Report("update", "Update failed, launching existing version...");
            try
            {
                context.UpdateEngine.CleanupIncomingArtifacts();
            }
            catch (Exception ex)
            {
                Logger.Warn($"Failed to cleanup update artifacts after failed update: {ex.Message}");
            }
        }

        return new LaunchPhaseResult(LaunchPhaseStatus.Continue);
    }
}
