using LanMountainDesktop.Launcher.Models;

namespace LanMountainDesktop.Launcher.Update;

internal sealed class UpdateEngineFacade : IUpdateEngine
{
    private readonly UpdateEnginePaths _paths;
    private readonly PendingUpdateDetector _pendingUpdateDetector;
    private readonly LegacyUpdateApplier _legacyUpdateApplier;
    private readonly PlondsUpdateApplier _plondsUpdateApplier;
    private readonly RollbackStrategy _rollbackStrategy;
    private readonly DeploymentActivator _deploymentActivator;
    private readonly IncomingArtifactsCleaner _incomingArtifactsCleaner;

    public UpdateEngineFacade(DeploymentLocator deploymentLocator, IUpdateProgressReporter? progressReporter = null)
    {
        var reporter = progressReporter ?? new NullUpdateProgressReporter();
        _paths = new UpdateEnginePaths(deploymentLocator.GetAppRoot());
        var signatureVerifier = new UpdateSignatureVerifier(_paths);
        var snapshotStore = new UpdateSnapshotStore(_paths);
        var checkpointStore = new InstallCheckpointStore(_paths);
        _deploymentActivator = new DeploymentActivator(deploymentLocator);
        _incomingArtifactsCleaner = new IncomingArtifactsCleaner(_paths);
        _pendingUpdateDetector = new PendingUpdateDetector(deploymentLocator, _paths, signatureVerifier);
        _legacyUpdateApplier = new LegacyUpdateApplier(
            deploymentLocator,
            _paths,
            signatureVerifier,
            reporter,
            snapshotStore,
            checkpointStore,
            _deploymentActivator,
            _incomingArtifactsCleaner);
        _plondsUpdateApplier = new PlondsUpdateApplier(
            deploymentLocator,
            _paths,
            signatureVerifier,
            reporter,
            snapshotStore,
            checkpointStore,
            _deploymentActivator,
            _incomingArtifactsCleaner,
            new PlondsPayloadResolver(_paths));
        _rollbackStrategy = new RollbackStrategy(deploymentLocator, snapshotStore, _deploymentActivator);
    }

    public LauncherResult CheckPendingUpdate() => _pendingUpdateDetector.CheckPendingUpdate();

    public Task<LauncherResult> DownloadAsync(string manifestUrl, string signatureUrl, string archiveUrl, CancellationToken cancellationToken)
    {
        _ = manifestUrl;
        _ = signatureUrl;
        _ = archiveUrl;
        _ = cancellationToken;

        return Task.FromResult(new LauncherResult
        {
            Success = false,
            Stage = "update.download",
            Code = "host_managed_only",
            Message = "Launcher no longer performs network downloads. Host must download update payload into incoming directory first."
        });
    }

    public async Task<LauncherResult> ApplyPendingUpdateAsync()
    {
        Directory.CreateDirectory(_paths.IncomingRoot);
        Directory.CreateDirectory(_paths.SnapshotsRoot);

        var stateValidation = _pendingUpdateDetector.ValidateIncomingState();
        if (!stateValidation.Success || stateValidation.Code == "noop")
        {
            return stateValidation;
        }

        try
        {
            File.WriteAllText(_paths.ApplyLockPath, DateTimeOffset.UtcNow.ToString("O"));
        }
        catch (Exception ex)
        {
            return UpdateEngineResults.Failed("update.apply", "lock_conflict", $"Failed to acquire apply lock: {ex.Message}");
        }

        try
        {
            if (_paths.HasPlondsPayload)
            {
                return await _plondsUpdateApplier.ApplyAsync().ConfigureAwait(false);
            }

            return await _legacyUpdateApplier.ApplyAsync().ConfigureAwait(false);
        }
        finally
        {
            TryDeleteApplyLock();
        }
    }

    public LauncherResult RollbackLatest() => _rollbackStrategy.RollbackLatest();

    public void CleanupDestroyedDeployments() => _deploymentActivator.RetainDeploymentsForRollback();

    public void CleanupIncomingArtifacts() => _incomingArtifactsCleaner.Cleanup();

    private void TryDeleteApplyLock()
    {
        try
        {
            if (File.Exists(_paths.ApplyLockPath))
            {
                File.Delete(_paths.ApplyLockPath);
            }
        }
        catch
        {
        }
    }
}
