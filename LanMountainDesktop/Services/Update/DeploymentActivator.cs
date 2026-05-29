namespace LanMountainDesktop.Services.Update;

internal sealed class DeploymentActivator(AppDeploymentLocator deploymentLocator)
{
    public void Activate(string fromDeployment, string toDeployment)
    {
        var toCurrent = Path.Combine(toDeployment, ".current");
        var fromCurrent = Path.Combine(fromDeployment, ".current");
        var fromDestroy = Path.Combine(fromDeployment, ".destroy");
        var toDestroy = Path.Combine(toDeployment, ".destroy");
        var toPartial = Path.Combine(toDeployment, ".partial");

        File.WriteAllText(toCurrent, string.Empty);
        if (File.Exists(toDestroy)) File.Delete(toDestroy);
        if (File.Exists(fromCurrent)) File.Delete(fromCurrent);

        File.WriteAllText(fromDestroy, string.Empty);
        if (File.Exists(toPartial)) File.Delete(toPartial);
    }

    public RollbackAttemptResult TryRollbackOnFailure(ApplySnapshotMetadata snapshot)
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

            var destroyMarker = Path.Combine(snapshot.SourceDirectory, ".destroy");
            if (File.Exists(destroyMarker)) File.Delete(destroyMarker);

            var currentMarker = Path.Combine(snapshot.SourceDirectory, ".current");
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
