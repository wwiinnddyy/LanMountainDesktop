namespace LanMountainDesktop.Services.Update;

internal sealed class RollbackStrategy(
    AppDeploymentLocator deploymentLocator,
    UpdateSnapshotStore snapshotStore,
    DeploymentActivator deploymentActivator)
{
    public ApplyUpdateResult RollbackLatest()
    {
        var latest = snapshotStore.LoadLatest();
        if (latest is null)
        {
            return ApplyUpdateResults.Failed("update.rollback", "no_snapshot", "No snapshot found.");
        }

        var (snapshotPath, snapshot) = latest.Value;
        if (string.IsNullOrWhiteSpace(snapshot.SourceDirectory))
        {
            return ApplyUpdateResults.Failed("update.rollback", "invalid_snapshot", "Invalid snapshot metadata.");
        }

        if (!Directory.Exists(snapshot.SourceDirectory))
        {
            return ApplyUpdateResults.Failed("update.rollback", "source_missing", $"Rollback source deployment is missing: {snapshot.SourceDirectory}");
        }

        var currentDeployment = deploymentLocator.FindCurrentDeploymentDirectory();
        if (string.IsNullOrWhiteSpace(currentDeployment))
        {
            return ApplyUpdateResults.Failed("update.rollback", "no_current_deployment", "Current deployment not found.");
        }

        deploymentActivator.Activate(currentDeployment, snapshot.SourceDirectory);
        snapshot.Status = "manual_rollback";
        snapshotStore.Save(snapshotPath, snapshot);

        return new ApplyUpdateResult
        {
            Success = true,
            Stage = "update.rollback",
            Code = "ok",
            Message = $"Rolled back to {snapshot.SourceVersion}.",
            RolledBackTo = snapshot.SourceVersion
        };
    }
}
