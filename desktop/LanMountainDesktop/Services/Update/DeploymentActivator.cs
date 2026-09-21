using LanMountainDesktop.Shared.Contracts.Update;
using LanMountainDesktop.Shared.Contracts.Deployment;

namespace LanMountainDesktop.Services.Update;

internal sealed class DeploymentActivator(AppDeploymentLocator deploymentLocator)
{
    public void Activate(string fromDeployment, string toDeployment)
    {
        var toCurrent = Path.Combine(toDeployment, DeploymentLayout.CurrentMarkerFileName);
        var fromCurrent = Path.Combine(fromDeployment, DeploymentLayout.CurrentMarkerFileName);
        var fromDestroy = Path.Combine(fromDeployment, DeploymentLayout.DestroyMarkerFileName);
        var toDestroy = Path.Combine(toDeployment, DeploymentLayout.DestroyMarkerFileName);
        var toPartial = Path.Combine(toDeployment, DeploymentLayout.PartialMarkerFileName);

        File.WriteAllText(toCurrent, string.Empty);
        if (File.Exists(toDestroy)) File.Delete(toDestroy);
        if (File.Exists(fromCurrent)) File.Delete(fromCurrent);

        File.WriteAllText(fromDestroy, string.Empty);
        if (File.Exists(toPartial)) File.Delete(toPartial);
    }

    public RollbackAttemptResult TryRollbackOnFailure(SnapshotMetadata snapshot)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(snapshot.TargetDirectory) && Directory.Exists(snapshot.TargetDirectory))
            {
                Directory.Delete(snapshot.TargetDirectory, true);
            }

            if (string.IsNullOrWhiteSpace(snapshot.SourceDirectory) || !Directory.Exists(snapshot.SourceDirectory))
            {
                return new RollbackAttemptResult(false, "Source deployment is missing.");
            }

            var destroyMarker = Path.Combine(snapshot.SourceDirectory, DeploymentLayout.DestroyMarkerFileName);
            if (File.Exists(destroyMarker)) File.Delete(destroyMarker);

            var currentMarker = Path.Combine(snapshot.SourceDirectory, DeploymentLayout.CurrentMarkerFileName);
            if (!File.Exists(currentMarker)) File.WriteAllText(currentMarker, string.Empty);

            return new RollbackAttemptResult(true, null);
        }
        catch (Exception ex)
        {
            return new RollbackAttemptResult(false, ex.Message);
        }
    }

    public void RetainDeploymentsForRollback() => deploymentLocator.CleanupOldDeployments(3);
}

internal sealed record RollbackAttemptResult(bool Success, string? ErrorMessage);
