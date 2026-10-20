using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using LanMountainDesktop.Shared.Contracts.Deployment;
using LanMountainDesktop.Shared.Contracts.Update;
using LanMountainDesktop.Shared.IO;

namespace LanDesktopPLONDS.Installer.Services;

internal static class DeploymentMaintenanceRunner
{
    internal static async Task<int> RunAsync(string requestPath, CancellationToken cancellationToken)
    {
        DeploymentMaintenanceRequest? request = null;
        MaintenanceRestartContext? restartContext = null;
        try
        {
            request = JsonSerializer.Deserialize(File.ReadAllText(requestPath), DeploymentMaintenanceJsonContext.Default.DeploymentMaintenanceRequest)
                ?? throw new InvalidDataException("Maintenance request is empty.");
            var root = Path.GetFullPath(request.InstallRoot);
            InstallerElevation.EnsureCanInstall(root);
            if (request.ArchivePath is not null)
            {
                await using var archiveStream = File.OpenRead(request.ArchivePath);
                var hash = Convert.ToHexString(await SHA256.HashDataAsync(archiveStream, cancellationToken).ConfigureAwait(false));
                if (!string.Equals(hash, request.ArchiveSha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Maintenance package hash mismatch.");
            }
            WriteReceipt(request, "waiting_for_exit", null);
            try
            {
                using var parent = Process.GetProcessById(request.ParentProcessId);
                if (parent.StartTime.ToUniversalTime().Ticks == request.ParentStartTimeUtcTicks)
                {
                    restartContext = MaintenanceRestartContext.Capture(parent);
                    await parent.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
                }
            }
            catch (ArgumentException) { }
            RunningProcessGuard.EnsureNoRunningProcesses(root);
            WriteReceipt(request, "installing", null);

            if (request.RollbackSnapshotPath is not null)
                Rollback(root, request.RollbackSnapshotPath, cancellationToken);
            else
            {
                var manifest = JsonSerializer.Deserialize(File.ReadAllText(request.ManifestPath!), InstallerJsonContext.Default.InstallerPlondsManifest)
                    ?? throw new InvalidDataException("Maintenance manifest is empty.");
                if (manifest.CurrentVersion != request.Version)
                    throw new InvalidDataException("Maintenance version does not match its manifest.");
                var extract = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(requestPath))!, "maintenance-files-" + Guid.NewGuid().ToString("N"));
                ZipFile.ExtractToDirectory(request.ArchivePath!, extract);
                var package = new PreparedFilesPackage(manifest.CurrentVersion, "maintenance", request.ArchivePath!, extract, manifest);
                await RuntimePrerequisite.EnsureAsync(package, cancellationToken).ConfigureAwait(false);
                await new FilesPackageInstaller().InstallAsync(package, root, null, cancellationToken).ConfigureAwait(false);
            }
            WriteReceipt(request, "completed", null);
            if (request.Restart)
            {
                try
                {
                    var launcher = Path.Combine(root, DeploymentLayout.GetLauncherExecutableName());
                    if (restartContext is not null) restartContext.Restart(launcher, root);
                    else if (!InstallerElevation.IsRunningElevated())
                        Process.Start(new ProcessStartInfo(launcher) { WorkingDirectory = root, UseShellExecute = true });
                }
                catch (Exception ex) { InstallerStartupDiagnostics.Log($"Deployment completed; restart failed: {ex.Message}"); }
            }
            return 0;
        }
        catch (Exception ex)
        {
            if (request is not null) WriteReceipt(request, "failed", ex.Message);
            InstallerStartupDiagnostics.Log($"Maintenance failed: {ex}");
            return 1;
        }
        finally { restartContext?.Dispose(); }
    }

    private static void WriteReceipt(DeploymentMaintenanceRequest request, string status, string? error)
    {
        AtomicFileWriter.WriteText(request.ReceiptPath, JsonSerializer.Serialize(
            new DeploymentMaintenanceReceipt(status, request.Version, error), DeploymentMaintenanceJsonContext.Default.DeploymentMaintenanceReceipt),
            "UpdateMaintenance");
    }

    private static void Rollback(string root, string snapshotPath, CancellationToken cancellationToken)
    {
        if (!PathContainment.IsSameOrChild(UpdatePaths.GetSnapshotsDirectory(root), snapshotPath))
            throw new InvalidDataException("Rollback snapshot is outside the snapshot directory.");
        var snapshot = JsonSerializer.Deserialize(File.ReadAllText(snapshotPath), DeploymentCommitJsonContext.Default.SnapshotMetadata)
            ?? throw new InvalidDataException("Rollback snapshot is empty.");
        if (!PathContainment.IsSameOrChild(root, snapshot.SourceDirectory) ||
            !File.Exists(Path.Combine(snapshot.SourceDirectory, DeploymentLayout.GetHostExecutableName())))
            throw new InvalidDataException("Rollback deployment is unavailable.");
        var restorePath = snapshotPath + ".root-backup";
        DeploymentRootFileTransaction? transaction = null;
        if (File.Exists(restorePath))
        {
            var restore = JsonSerializer.Deserialize(File.ReadAllText(restorePath), RootFileRestoreJsonContext.Default.RootFileRestoreState)
                ?? throw new InvalidDataException("Rollback root state is empty.");
            transaction = DeploymentRootFileTransaction.BeginRestore(root, restore, cancellationToken);
        }
        using (transaction)
        {
            DeploymentCommit.Activate(root, snapshot.SourceDirectory, transaction?.BackupDirectory);
            transaction?.Commit();
        }
    }
}
