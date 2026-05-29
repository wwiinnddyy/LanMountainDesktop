using LanMountainDesktop.Shared.Contracts.Update;

namespace LanMountainDesktop.Services.Update;

internal sealed class UpdateRollbackGateway
{
    public ApplyUpdateResult RollbackLatest(string launcherRoot)
    {
        var paths = new PlondsApplyPaths(launcherRoot);
        var locator = new AppDeploymentLocator(launcherRoot);
        var snapshotStore = new UpdateSnapshotStore(paths);
        var activator = new DeploymentActivator(locator);
        var strategy = new RollbackStrategy(locator, snapshotStore, activator);
        return strategy.RollbackLatest();
    }
}
