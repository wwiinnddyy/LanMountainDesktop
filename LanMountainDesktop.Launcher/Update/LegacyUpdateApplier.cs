using System.IO.Compression;
using System.Text.Json;
using LanMountainDesktop.Launcher.Models;
using ContractsUpdate = LanMountainDesktop.Shared.Contracts.Update;

namespace LanMountainDesktop.Launcher.Update;

internal sealed class LegacyUpdateApplier(
    DeploymentLocator deploymentLocator,
    UpdateEnginePaths paths,
    UpdateSignatureVerifier signatureVerifier,
    IUpdateProgressReporter progressReporter,
    UpdateSnapshotStore snapshotStore,
    InstallCheckpointStore checkpointStore,
    DeploymentActivator deploymentActivator,
    IncomingArtifactsCleaner incomingCleaner)
{
    public async Task<LauncherResult> ApplyAsync()
    {
        if (!File.Exists(paths.FileMapPath) || !File.Exists(paths.ArchivePath))
        {
            return new LauncherResult
            {
                Success = true,
                Stage = "update.apply",
                Code = "noop",
                Message = "No update payload found."
            };
        }

        progressReporter.ReportProgress(new ContractsUpdate.InstallProgressReport(ContractsUpdate.InstallStage.VerifySignature, "Verifying signature...", 0, null, 0, 0));
        var verifyResult = signatureVerifier.Verify(paths.FileMapPath, paths.SignaturePath, UpdateEnginePaths.SignatureFileName);
        if (!verifyResult.Success)
        {
            progressReporter.ReportComplete(new ContractsUpdate.InstallCompleteReport(false, null, null, verifyResult.Message, false));
            return UpdateEngineResults.Failed("update.apply", "signature_failed", verifyResult.Message);
        }

        var fileMapText = await File.ReadAllTextAsync(paths.FileMapPath).ConfigureAwait(false);
        var fileMap = JsonSerializer.Deserialize(fileMapText, AppJsonContext.Default.SignedFileMap);
        if (fileMap is null || fileMap.Files.Count == 0)
        {
            progressReporter.ReportComplete(new ContractsUpdate.InstallCompleteReport(false, null, null, "No update file entries were found.", false));
            return UpdateEngineResults.Failed("update.apply", "invalid_manifest", "No update file entries were found.");
        }

        var currentDeployment = deploymentLocator.FindCurrentDeploymentDirectory();
        var currentVersion = deploymentLocator.GetCurrentVersion();
        if (!string.IsNullOrWhiteSpace(fileMap.FromVersion) &&
            !string.Equals(fileMap.FromVersion, currentVersion, StringComparison.OrdinalIgnoreCase))
        {
            return UpdateEngineResults.Failed(
                "update.apply",
                "version_mismatch",
                $"Update requires source version {fileMap.FromVersion} but current is {currentVersion}.");
        }

        var targetVersion = string.IsNullOrWhiteSpace(fileMap.ToVersion) ? currentVersion : fileMap.ToVersion!;
        var existingCheckpoint = checkpointStore.Load();
        var canResume = existingCheckpoint is not null
                        && string.Equals(existingCheckpoint.SourceVersion, currentVersion, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(existingCheckpoint.TargetVersion, targetVersion, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(existingCheckpoint.SourceDirectory ?? string.Empty, currentDeployment ?? string.Empty, StringComparison.OrdinalIgnoreCase)
                        && Directory.Exists(existingCheckpoint.TargetDirectory)
                        && File.Exists(Path.Combine(existingCheckpoint.TargetDirectory, ".partial"));

        if (existingCheckpoint is not null && !canResume)
        {
            return UpdateEngineResults.Failed("update.apply", "resume_state_invalid", "Install checkpoint is stale or invalid. Please cancel and redownload update payload.");
        }

        var targetDeployment = canResume
            ? existingCheckpoint!.TargetDirectory
            : deploymentLocator.BuildNextDeploymentDirectory(targetVersion);
        var snapshot = BuildSnapshot(canResume, existingCheckpoint, currentVersion, targetVersion, currentDeployment, targetDeployment);
        var snapshotPath = snapshotStore.CreateSnapshotPath(snapshot.SnapshotId);
        var checkpoint = canResume
            ? existingCheckpoint!
            : BuildCheckpoint(snapshot, currentVersion, targetVersion, currentDeployment, targetDeployment);

        try
        {
            snapshotStore.Save(snapshotPath, snapshot);
            PrepareExtractRoot();
            ZipFile.ExtractToDirectory(paths.ArchivePath, paths.ExtractRoot, overwriteFiles: true);

            if (!canResume)
            {
                progressReporter.ReportProgress(new ContractsUpdate.InstallProgressReport(ContractsUpdate.InstallStage.CreateTarget, "Creating target deployment...", 20, null, 0, fileMap.Files.Count));
                Directory.CreateDirectory(targetDeployment);
                File.WriteAllText(Path.Combine(targetDeployment, ".partial"), string.Empty);
            }

            checkpointStore.Save(checkpoint);
            ApplyFiles(fileMap, currentDeployment!, targetDeployment, checkpoint);
            VerifyFiles(fileMap, targetDeployment, checkpoint);

            progressReporter.ReportProgress(new ContractsUpdate.InstallProgressReport(ContractsUpdate.InstallStage.ActivateDeployment, "Activating deployment...", 85, null, fileMap.Files.Count, fileMap.Files.Count));
            deploymentActivator.Activate(currentDeployment!, targetDeployment);

            snapshot.Status = "applied";
            snapshotStore.Save(snapshotPath, snapshot);
            incomingCleaner.Cleanup();
            deploymentActivator.RetainDeploymentsForRollback();

            progressReporter.ReportProgress(new ContractsUpdate.InstallProgressReport(ContractsUpdate.InstallStage.Completed, $"Updated to {targetVersion}.", 100, null, fileMap.Files.Count, fileMap.Files.Count));
            progressReporter.ReportComplete(new ContractsUpdate.InstallCompleteReport(true, currentVersion, targetVersion, null, false));

            return new LauncherResult
            {
                Success = true,
                Stage = "update.apply",
                Code = "ok",
                Message = $"Updated to {targetVersion}.",
                CurrentVersion = currentVersion,
                TargetVersion = targetVersion
            };
        }
        catch (Exception ex)
        {
            progressReporter.ReportProgress(new ContractsUpdate.InstallProgressReport(ContractsUpdate.InstallStage.RollingBack, "Rolling back...", 0, null, 0, 0));
            var rollbackResult = deploymentActivator.TryRollbackOnFailure(snapshot);
            snapshot.Status = rollbackResult.Success ? "rolled_back" : "rollback_failed";
            snapshotStore.Save(snapshotPath, snapshot);
            var errorMessage = rollbackResult.Success
                ? ex.Message
                : $"{ex.Message}; rollback failed: {rollbackResult.ErrorMessage}";
            progressReporter.ReportComplete(new ContractsUpdate.InstallCompleteReport(false, currentVersion, targetVersion, errorMessage, rollbackResult.Success));
            return new LauncherResult
            {
                Success = false,
                Stage = "update.apply",
                Code = rollbackResult.Success ? "apply_failed" : "rollback_failed",
                Message = rollbackResult.Success
                    ? "Failed to apply update. Rolled back to previous version."
                    : "Failed to apply update and rollback failed.",
                ErrorMessage = errorMessage,
                CurrentVersion = currentVersion,
                RolledBackTo = rollbackResult.Success ? currentVersion : null
            };
        }
        finally
        {
            checkpointStore.Delete();
            TryDeleteExtractRoot();
        }
    }

    private void ApplyFiles(SignedFileMap fileMap, string currentDeployment, string targetDeployment, InstallCheckpoint checkpoint)
    {
        progressReporter.ReportProgress(new ContractsUpdate.InstallProgressReport(ContractsUpdate.InstallStage.ApplyFiles, "Applying files...", 30, null, checkpoint.AppliedCount, fileMap.Files.Count));
        for (var fileIndex = checkpoint.AppliedCount; fileIndex < fileMap.Files.Count; fileIndex++)
        {
            var file = fileMap.Files[fileIndex];
            ApplyFileEntry(file, currentDeployment, targetDeployment);
            checkpoint.AppliedCount = fileIndex + 1;
            checkpointStore.Save(checkpoint);
            progressReporter.ReportProgress(new ContractsUpdate.InstallProgressReport(ContractsUpdate.InstallStage.ApplyFiles, "Applying files...", 30 + (checkpoint.AppliedCount * 30 / fileMap.Files.Count), file.Path, checkpoint.AppliedCount, fileMap.Files.Count));
        }
    }

    private void VerifyFiles(SignedFileMap fileMap, string targetDeployment, InstallCheckpoint checkpoint)
    {
        progressReporter.ReportProgress(new ContractsUpdate.InstallProgressReport(ContractsUpdate.InstallStage.VerifyHashes, "Verifying hashes...", 65, null, checkpoint.VerifiedCount, fileMap.Files.Count));
        for (var verifyIndex = checkpoint.VerifiedCount; verifyIndex < fileMap.Files.Count; verifyIndex++)
        {
            var file = fileMap.Files[verifyIndex];
            if (NeedsVerification(file))
            {
                var fullPath = Path.Combine(targetDeployment, file.Path);
                var actualHash = UpdateHash.ComputeSha256Hex(fullPath);
                if (!string.Equals(actualHash, file.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"File hash mismatch for '{file.Path}'.");
                }
            }

            checkpoint.VerifiedCount = verifyIndex + 1;
            checkpointStore.Save(checkpoint);
            progressReporter.ReportProgress(new ContractsUpdate.InstallProgressReport(ContractsUpdate.InstallStage.VerifyHashes, "Verifying hashes...", 65 + (checkpoint.VerifiedCount * 15 / fileMap.Files.Count), file.Path, checkpoint.VerifiedCount, fileMap.Files.Count));
        }
    }

    private void ApplyFileEntry(UpdateFileEntry file, string currentDeployment, string targetDeployment)
    {
        var normalizedPath = UpdatePathGuard.NormalizeRelativePath(file.Path);
        if (string.Equals(file.Action, "delete", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var targetPath = Path.Combine(targetDeployment, normalizedPath);
        UpdatePathGuard.EnsurePathWithinRoot(targetPath, targetDeployment);
        var targetDir = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrWhiteSpace(targetDir))
        {
            Directory.CreateDirectory(targetDir);
        }

        if (string.Equals(file.Action, "reuse", StringComparison.OrdinalIgnoreCase))
        {
            var sourcePath = Path.Combine(currentDeployment, normalizedPath);
            UpdatePathGuard.EnsurePathWithinRoot(sourcePath, currentDeployment);
            if (!File.Exists(sourcePath))
            {
                throw new FileNotFoundException($"Cannot reuse file '{file.Path}' because it was not found in current deployment.");
            }

            File.Copy(sourcePath, targetPath, overwrite: true);
            return;
        }

        var archiveRelative = string.IsNullOrWhiteSpace(file.ArchivePath) ? normalizedPath : UpdatePathGuard.NormalizeRelativePath(file.ArchivePath);
        var extractedPath = Path.Combine(paths.ExtractRoot, archiveRelative);
        UpdatePathGuard.EnsurePathWithinRoot(extractedPath, paths.ExtractRoot);
        if (!File.Exists(extractedPath))
        {
            throw new FileNotFoundException($"Archive file '{archiveRelative}' not found for '{file.Path}'.");
        }

        File.Copy(extractedPath, targetPath, overwrite: true);
    }

    private void PrepareExtractRoot()
    {
        if (Directory.Exists(paths.ExtractRoot))
        {
            Directory.Delete(paths.ExtractRoot, true);
        }

        Directory.CreateDirectory(paths.ExtractRoot);
    }

    private void TryDeleteExtractRoot()
    {
        try
        {
            if (Directory.Exists(paths.ExtractRoot))
            {
                Directory.Delete(paths.ExtractRoot, true);
            }
        }
        catch
        {
        }
    }

    private static SnapshotMetadata BuildSnapshot(
        bool canResume,
        InstallCheckpoint? existingCheckpoint,
        string currentVersion,
        string targetVersion,
        string? currentDeployment,
        string targetDeployment) =>
        new()
        {
            SnapshotId = canResume ? existingCheckpoint!.SnapshotId : Guid.NewGuid().ToString("N"),
            SourceVersion = currentVersion,
            TargetVersion = targetVersion,
            CreatedAt = DateTimeOffset.UtcNow,
            SourceDirectory = currentDeployment ?? string.Empty,
            TargetDirectory = targetDeployment,
            Status = "pending"
        };

    private static InstallCheckpoint BuildCheckpoint(
        SnapshotMetadata snapshot,
        string currentVersion,
        string targetVersion,
        string? currentDeployment,
        string targetDeployment) =>
        new()
        {
            SnapshotId = snapshot.SnapshotId,
            SourceVersion = currentVersion,
            TargetVersion = targetVersion,
            SourceDirectory = currentDeployment,
            TargetDirectory = targetDeployment,
            IsInitialDeployment = false
        };

    private static bool NeedsVerification(UpdateFileEntry file)
    {
        return !string.Equals(file.Action, "delete", StringComparison.OrdinalIgnoreCase) &&
               !string.IsNullOrWhiteSpace(file.Sha256);
    }
}
