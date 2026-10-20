using System.Text.Json;
using LanMountainDesktop.Services.Plonds;
using LanMountainDesktop.Services.Update;
using LanMountainDesktop.Shared.Contracts.Update;
using LanMountainDesktop.Shared.IO;
using LanMountainDesktop.Shared.Contracts.Deployment;

namespace LanMountainDesktop.Services.Settings;

internal sealed partial class UpdateSettingsService
{
    private readonly SemaphoreSlim _plondsOperationGate = new(1, 1);
    private CancellationTokenSource? _plondsOperationCts;
    private int _plondsOperationVersion;
    private bool _plondsDisposed;
    private readonly bool _ownsPlondsService;
    private bool _plondsMaintenanceScheduled;
    private const string PreparedStateFileName = "prepared-package.json";

    private async Task<T> RunPlondsOperationAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        var version = _plondsOperationVersion;
        await _plondsOperationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        try
        {
            ObjectDisposedException.ThrowIf(_plondsDisposed, this);
            if (version != _plondsOperationVersion) linked.Cancel();
            linked.Token.ThrowIfCancellationRequested();
            _plondsOperationCts = linked;
            return await operation(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            if (typeof(T) == typeof(LanMountainDesktop.Services.Update.DownloadResult))
                return (T)(object)new LanMountainDesktop.Services.Update.DownloadResult(false, null, "Update transfer stopped.", false);
            if (typeof(T) == typeof(UpdateCheckReport))
                return (T)(object)new UpdateCheckReport(false, null, null, null, null, null, null, null, null, "Update check stopped.");
            throw;
        }
        finally
        {
            if (ReferenceEquals(_plondsOperationCts, linked)) _plondsOperationCts = null;
            _plondsOperationGate.Release();
        }
    }

    private async Task StopPlondsOperationAsync(UpdatePhase phase)
    {
        Interlocked.Increment(ref _plondsOperationVersion);
        _plondsOperationCts?.Cancel();
        await _plondsOperationGate.WaitAsync().ConfigureAwait(false);
        try { TransitionPlonds(phase); }
        finally { _plondsOperationGate.Release(); }
    }

    private void ClearPlondsPending()
    {
        _pendingPlondsLatest = null;
        _pendingPlondsCleanInstallCandidate = null;
        _pendingPlondsInstallerManifest = null;
        _pendingPlondsPackage = null;
        var state = Get();
        Save(state with { PendingUpdateInstallerPath = null, PendingUpdateVersion = null, PendingUpdateSha256 = null, PendingUpdatePublishedAtUtcMs = null });
    }

    private static void SavePreparedState(PlondsPreparedPackage package)
    {
        if (!File.Exists(package.ManifestPath)) return;
        var path = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(package.ManifestPath))!, PreparedStateFileName);
        AtomicFileWriter.WriteText(path, JsonSerializer.Serialize(package), "UpdateState");
    }

    private void RestorePendingPlonds()
    {
        var state = Get();
        if (string.IsNullOrWhiteSpace(state.PendingUpdateInstallerPath) || !File.Exists(state.PendingUpdateInstallerPath)) return;
        if (state.PendingUpdateInstallerPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            var receiptPath = Path.Combine(Path.GetDirectoryName(state.PendingUpdateInstallerPath)!, DeploymentMaintenance.ReceiptFileName);
            if (File.Exists(receiptPath))
            {
                var receipt = JsonSerializer.Deserialize(File.ReadAllText(receiptPath), DeploymentMaintenanceJsonContext.Default.DeploymentMaintenanceReceipt);
                if (receipt?.Status == "completed" && receipt.Version == state.PendingUpdateVersion) { ClearPlondsPending(); _plondsPhase = UpdatePhase.Completed; return; }
                if (receipt?.Status == "failed") { _plondsPhase = UpdatePhase.Failed; return; }
                if (receipt?.Status is "waiting_for_exit" or "installing")
                { _plondsMaintenanceScheduled = true; _plondsPhase = UpdatePhase.Installing; return; }
            }
            var savedManifest = Path.Combine(Path.GetDirectoryName(state.PendingUpdateInstallerPath)!, "native-installer-state.json");
            if (File.Exists(savedManifest))
            {
                _pendingPlondsInstallerManifest = JsonSerializer.Deserialize<UpdateManifest>(File.ReadAllText(savedManifest));
                _plondsPhase = UpdatePhase.Downloaded;
            }
            return;
        }
        if (!state.PendingUpdateInstallerPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) return;
        try
        {
            var manifestPath = Path.GetFullPath(state.PendingUpdateInstallerPath);
            var directory = Path.GetDirectoryName(manifestPath)!;
            var receiptPath = Path.Combine(directory, DeploymentMaintenance.ReceiptFileName);
            if (File.Exists(receiptPath))
            {
                var receipt = JsonSerializer.Deserialize(File.ReadAllText(receiptPath), DeploymentMaintenanceJsonContext.Default.DeploymentMaintenanceReceipt);
                if (receipt?.Status == "completed" && receipt.Version == state.PendingUpdateVersion)
                {
                    ClearPlondsPending();
                    _plondsPhase = UpdatePhase.Completed;
                    return;
                }
                if (receipt?.Status is "waiting_for_exit" or "installing")
                {
                    _plondsMaintenanceScheduled = true;
                    _plondsPhase = UpdatePhase.Installing;
                    return;
                }
                if (receipt?.Status == "failed") _plondsPhase = UpdatePhase.Failed;
            }
            var preparedPath = Path.Combine(directory, PreparedStateFileName);
            var manifest = JsonSerializer.Deserialize<PlondsClientManifest>(File.ReadAllText(manifestPath), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (manifest is null || !PlondsManifestSelector.TryParseVersion(manifest.CurrentVersion, out var version)) return;
            if (PlondsManifestSelector.SelectHighestVersionCandidates(
                    [new(new("cached-package", "s3", manifestPath), manifest)], PlatformIdentifiers.CurrentPlatformId, Get().UpdateChannel).Count == 0) return;
            var package = File.Exists(preparedPath)
                ? JsonSerializer.Deserialize<PlondsPreparedPackage>(File.ReadAllText(preparedPath))
                : new PlondsPreparedPackage(version, Directory.Exists(Path.Combine(directory, "Files")) ? PlondsPackageMode.Full : PlondsPackageMode.Delta,
                    manifestPath, Path.Combine(directory, PlondsWireFormat.DeltaPackageFileName), Path.Combine(directory, "changed"),
                    Path.Combine(directory, PlondsWireFormat.FullPackageFileName), Path.Combine(directory, "Files"));
            if (package is null || package.Version != version) return;
            var extracted = package.Mode == PlondsPackageMode.Full ? package.FilesDirectory : package.ChangedDirectory;
            var archive = package.Mode == PlondsPackageMode.Full ? package.FilesZipPath : package.ChangedZipPath;
            if (extracted is null || archive is null || !PathContainment.IsSameOrChild(directory, extracted) ||
                !PathContainment.IsSameOrChild(directory, archive) || !Directory.Exists(extracted) || !File.Exists(archive)) return;
            _pendingPlondsPackage = package;
            var current = TryParseVersion(LanMountainDesktop.Shared.Contracts.Launcher.AppVersionProvider.ResolveForCurrentProcess().Version, out var parsed)
                ? parsed : new Version(0, 0, 0);
            var sourceUrl = manifest.Downloads?.S3?.ManifestUrl ?? PlondsClientServiceFactory.CreateBuiltInSources(Get().UpdateChannel)[0].ManifestUrl;
            _pendingPlondsLatest = PlondsLatestResult.Available(current, version, [new(new("cached-package", "s3", sourceUrl), manifest)]);
            _pendingPlondsCleanInstallCandidate = manifest.RequiresCleanInstall ? _pendingPlondsLatest.Candidates[0] : null;
            if (_plondsPhase != UpdatePhase.Failed) _plondsPhase = UpdatePhase.Downloaded;
        }
        catch (Exception ex) when (ex is IOException or JsonException or ArgumentException)
        {
            AppLogger.Warn("UpdateWorkflow", $"Pending update could not be restored: {ex.Message}");
        }
    }

    private Task RollbackPlondsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        TransitionPlonds(UpdatePhase.RollingBack);
        var root = UpdatePaths.ResolveLauncherRoot(AppContext.BaseDirectory);
        var latest = new UpdateSnapshotStore(new PlondsApplyPaths(root)).LoadLatest();
        if (latest is not null && File.Exists(latest.Value.Path + ".root-backup"))
        {
            var scheduled = new PlondsMaintenanceGateway().Schedule(null, root, latest.Value.Path);
            if (!scheduled.Success)
            {
                TransitionPlonds(UpdatePhase.Failed);
                throw new InvalidOperationException(scheduled.ErrorMessage);
            }
            _plondsMaintenanceScheduled = true;
            App.CurrentHostApplicationLifecycle?.TryExit(new LanMountainDesktop.AirAppSdk.HostApplicationLifecycleRequest("update.rollback", "Restore the previous deployment and bootstrap files."));
            return Task.CompletedTask;
        }
        var result = new UpdateRollbackGateway().RollbackLatest(root);
        TransitionPlonds(result.Success ? UpdatePhase.RolledBack : UpdatePhase.Failed);
        if (!result.Success) throw new InvalidOperationException(result.ErrorMessage ?? result.Message);
        ClearPlondsPending();
        return Task.CompletedTask;
    }
}
