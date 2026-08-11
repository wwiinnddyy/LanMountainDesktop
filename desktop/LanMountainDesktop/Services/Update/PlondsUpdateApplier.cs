using System.Text.Json;
using LanMountainDesktop.Shared.Contracts.Update;

namespace LanMountainDesktop.Services.Update;

internal interface IUpdateProgressReporter
{
    void ReportProgress(InstallProgressReport report);
    void ReportComplete(InstallCompleteReport report);
}

internal sealed class InstallProgressBridge(IProgress<InstallProgressReport>? progress) : IUpdateProgressReporter
{
    private InstallCompleteReport? _complete;

    public InstallCompleteReport? CompleteReport => _complete;

    public void ReportProgress(InstallProgressReport report)
    {
        progress?.Report(report);
    }

    public void ReportComplete(InstallCompleteReport report)
    {
        _complete = report;
    }
}

internal sealed class PlondsUpdateApplier(
    AppDeploymentLocator deploymentLocator,
    PlondsApplyPaths paths,
    UpdateSignatureVerifier signatureVerifier,
    IUpdateProgressReporter progressReporter,
    UpdateSnapshotStore snapshotStore,
    ApplyInstallCheckpointStore checkpointStore,
    DeploymentActivator deploymentActivator,
    IncomingArtifactsCleaner incomingCleaner,
    PlondsPayloadResolver payloadResolver)
{
    public async Task<ApplyUpdateResult> ApplyAsync()
    {
        progressReporter.ReportProgress(new InstallProgressReport(InstallStage.VerifySignature, "Verifying PLONDS signature...", 0, null, 0, 0));
        var verifyResult = signatureVerifier.Verify(paths.PlondsFileMapPath, paths.PlondsSignaturePath, PlondsApplyPaths.PlondsSignatureFileName);
        if (!verifyResult.Success)
        {
            progressReporter.ReportComplete(new InstallCompleteReport(false, null, null, verifyResult.Message, false));
            return ApplyUpdateResults.Failed("update.apply", "signature_failed", verifyResult.Message);
        }

        var fileMapText = await File.ReadAllTextAsync(paths.PlondsFileMapPath).ConfigureAwait(false);
        var fileMap = JsonSerializer.Deserialize(fileMapText, UpdateApplyJsonContext.Default.ApplyPlondsFileMap) ?? new ApplyPlondsFileMap();
        var fileEntries = PlondsManifestParser.CollectFileEntries(fileMap);
        if (fileEntries.Count == 0)
        {
            PlondsManifestParser.PopulateFromRawJson(fileMapText, fileMap, fileEntries);
        }

        if (fileEntries.Count == 0)
        {
            progressReporter.ReportComplete(new InstallCompleteReport(false, null, null, "No PLONDS file entries were found.", false));
            return ApplyUpdateResults.Failed("update.apply", "invalid_manifest", "No PLONDS file entries were found.");
        }

        var plondsMetadata = PlondsManifestParser.LoadMetadata(paths.PlondsUpdateMetadataPath);
        var currentDeployment = deploymentLocator.FindCurrentDeploymentDirectory();
        var currentVersion = deploymentLocator.GetCurrentVersion();
        var sourceVersion = string.IsNullOrWhiteSpace(currentVersion) ? "0.0.0" : currentVersion;
        var expectedSourceVersion = PlondsManifestParser.ResolveSourceVersion(fileMap, plondsMetadata);
        if (!string.IsNullOrWhiteSpace(expectedSourceVersion) &&
            !string.Equals(expectedSourceVersion, sourceVersion, StringComparison.OrdinalIgnoreCase))
        {
            return ApplyUpdateResults.Failed("update.apply", "version_mismatch", $"PLONDS update requires source version {expectedSourceVersion} but current is {sourceVersion}.");
        }

        var targetVersion = PlondsManifestParser.ResolveTargetVersion(fileMap, plondsMetadata);
        if (string.IsNullOrWhiteSpace(targetVersion)) targetVersion = sourceVersion;

        var isInitialDeployment = string.IsNullOrWhiteSpace(currentDeployment);
        var existingCheckpoint = checkpointStore.Load();
        var canResume = existingCheckpoint is not null
                        && string.Equals(existingCheckpoint.SourceVersion, sourceVersion, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(existingCheckpoint.TargetVersion, targetVersion, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(existingCheckpoint.SourceDirectory ?? string.Empty, currentDeployment ?? string.Empty, StringComparison.OrdinalIgnoreCase)
                        && Directory.Exists(existingCheckpoint.TargetDirectory)
                        && File.Exists(Path.Combine(existingCheckpoint.TargetDirectory, ".partial"));

        if (existingCheckpoint is not null && !canResume)
        {
            return ApplyUpdateResults.Failed("update.apply", "resume_state_invalid", "Install checkpoint is stale or invalid. Please cancel and redownload update payload.");
        }

        var targetDeployment = canResume ? existingCheckpoint!.TargetDirectory : deploymentLocator.BuildNextDeploymentDirectory(targetVersion!);
        var snapshot = BuildSnapshot(canResume, existingCheckpoint, sourceVersion, targetVersion, currentDeployment, targetDeployment);
        var snapshotPath = snapshotStore.CreateSnapshotPath(snapshot.SnapshotId);
        var checkpoint = canResume ? existingCheckpoint! : BuildCheckpoint(snapshot, sourceVersion, targetVersion, currentDeployment, targetDeployment, isInitialDeployment);

        try
        {
            snapshotStore.Save(snapshotPath, snapshot);
            if (!canResume)
            {
                if (Directory.Exists(targetDeployment)) Directory.Delete(targetDeployment, true);
                progressReporter.ReportProgress(new InstallProgressReport(InstallStage.CreateTarget, "Creating target deployment...", 20, null, 0, fileEntries.Count));
                Directory.CreateDirectory(targetDeployment);
                File.WriteAllText(Path.Combine(targetDeployment, ".partial"), string.Empty);
            }

            checkpointStore.Save(checkpoint);
            ApplyFiles(fileEntries, currentDeployment, targetDeployment, checkpoint);
            VerifyFiles(fileEntries, targetDeployment, checkpoint);

            if (isInitialDeployment)
            {
                File.WriteAllText(Path.Combine(targetDeployment, ".current"), string.Empty);
                var partialMarker = Path.Combine(targetDeployment, ".partial");
                if (File.Exists(partialMarker)) File.Delete(partialMarker);
            }
            else
            {
                progressReporter.ReportProgress(new InstallProgressReport(InstallStage.ActivateDeployment, "Activating deployment...", 85, null, fileEntries.Count, fileEntries.Count));
                deploymentActivator.Activate(currentDeployment!, targetDeployment);
            }

            snapshot.Status = "applied";
            snapshotStore.Save(snapshotPath, snapshot);
            incomingCleaner.Cleanup();
            deploymentActivator.RetainDeploymentsForRollback();

            progressReporter.ReportProgress(new InstallProgressReport(InstallStage.Completed, $"Updated to {targetVersion}.", 100, null, fileEntries.Count, fileEntries.Count));
            progressReporter.ReportComplete(new InstallCompleteReport(true, sourceVersion, targetVersion, null, false));

            return new ApplyUpdateResult
            {
                Success = true,
                Stage = "update.apply",
                Code = "ok",
                Message = $"Updated to {targetVersion}.",
                CurrentVersion = sourceVersion,
                TargetVersion = targetVersion
            };
        }
        catch (Exception ex)
        {
            return HandleFailure(ex, isInitialDeployment, targetDeployment, snapshot, snapshotPath, sourceVersion, targetVersion);
        }
        finally
        {
            checkpointStore.Delete();
        }
    }

    private void ApplyFiles(IReadOnlyList<ApplyPlondsFileEntry> fileEntries, string? currentDeployment, string targetDeployment, ApplyInstallCheckpoint checkpoint)
    {
        progressReporter.ReportProgress(new InstallProgressReport(InstallStage.ApplyFiles, "Applying PLONDS files...", 30, null, checkpoint.AppliedCount, fileEntries.Count));
        for (var fileIndex = checkpoint.AppliedCount; fileIndex < fileEntries.Count; fileIndex++)
        {
            var entry = fileEntries[fileIndex];
            ApplyFileEntry(entry, currentDeployment, targetDeployment);
            checkpoint.AppliedCount = fileIndex + 1;
            checkpointStore.Save(checkpoint);
            progressReporter.ReportProgress(new InstallProgressReport(InstallStage.ApplyFiles, "Applying PLONDS files...", 30 + (checkpoint.AppliedCount * 30 / fileEntries.Count), entry.Path, checkpoint.AppliedCount, fileEntries.Count));
        }
    }

    private void VerifyFiles(IReadOnlyList<ApplyPlondsFileEntry> fileEntries, string targetDeployment, ApplyInstallCheckpoint checkpoint)
    {
        progressReporter.ReportProgress(new InstallProgressReport(InstallStage.VerifyHashes, "Verifying PLONDS hashes...", 65, null, checkpoint.VerifiedCount, fileEntries.Count));
        for (var verifyIndex = checkpoint.VerifiedCount; verifyIndex < fileEntries.Count; verifyIndex++)
        {
            var entry = fileEntries[verifyIndex];
            VerifyFileEntry(entry, targetDeployment);
            checkpoint.VerifiedCount = verifyIndex + 1;
            checkpointStore.Save(checkpoint);
            progressReporter.ReportProgress(new InstallProgressReport(InstallStage.VerifyHashes, "Verifying PLONDS hashes...", 65 + (checkpoint.VerifiedCount * 15 / fileEntries.Count), entry.Path, checkpoint.VerifiedCount, fileEntries.Count));
        }
    }

    private void ApplyFileEntry(ApplyPlondsFileEntry file, string? currentDeployment, string targetDeployment)
    {
        var normalizedPath = UpdatePathGuard.NormalizeRelativePath(file.Path);
        var action = string.IsNullOrWhiteSpace(file.Action) ? "replace" : file.Action!;
        if (string.Equals(action, "delete", StringComparison.OrdinalIgnoreCase)) return;

        var targetPath = Path.Combine(targetDeployment, normalizedPath);
        UpdatePathGuard.EnsurePathWithinRoot(targetPath, targetDeployment);
        var targetDir = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrWhiteSpace(targetDir)) Directory.CreateDirectory(targetDir);

        if (string.Equals(action, "reuse", StringComparison.OrdinalIgnoreCase))
        {
            CopyReusedFile(file, currentDeployment, normalizedPath, targetPath);
            return;
        }

        var objectPath = payloadResolver.ResolveObjectPath(file);
        var objectBytes = File.ReadAllBytes(objectPath);
        var restoredBytes = PlondsPayloadResolver.TryInflateGzip(objectBytes) ?? objectBytes;
        File.WriteAllBytes(targetPath, restoredBytes);
        ApplyUnixFileModeIfPresent(targetPath, file);
    }

    private static void CopyReusedFile(ApplyPlondsFileEntry file, string? currentDeployment, string normalizedPath, string targetPath)
    {
        if (string.IsNullOrWhiteSpace(currentDeployment)) throw new FileNotFoundException($"Cannot reuse file '{file.Path}' because no source deployment is available.");
        var sourcePath = Path.Combine(currentDeployment, normalizedPath);
        UpdatePathGuard.EnsurePathWithinRoot(sourcePath, currentDeployment);
        if (!File.Exists(sourcePath)) throw new FileNotFoundException($"Cannot reuse file '{file.Path}' because it was not found in current deployment.");

        File.Copy(sourcePath, targetPath, overwrite: true);
        ApplyUnixFileModeIfPresent(targetPath, file);
    }

    private static void VerifyFileEntry(ApplyPlondsFileEntry file, string targetDeployment)
    {
        var action = string.IsNullOrWhiteSpace(file.Action) ? "replace" : file.Action!;
        if (string.Equals(action, "delete", StringComparison.OrdinalIgnoreCase)) return;

        var targetPath = Path.Combine(targetDeployment, UpdatePathGuard.NormalizeRelativePath(file.Path));
        UpdatePathGuard.EnsurePathWithinRoot(targetPath, targetDeployment);
        if (!File.Exists(targetPath)) throw new FileNotFoundException($"Expected target file was not created: {file.Path}");

        if (PlondsManifestParser.TryGetExpectedSha512(file, out var expectedSha512))
        {
            var actualSha512 = UpdateHash.ComputeSha512(targetPath);
            if (!actualSha512.AsSpan().SequenceEqual(expectedSha512)) throw new InvalidOperationException($"SHA-512 mismatch for '{file.Path}'.");
            return;
        }

        if (!string.IsNullOrWhiteSpace(file.Sha256))
        {
            var expectedSha256 = UpdateHash.NormalizeHashText(file.Sha256);
            var actualSha256 = UpdateHash.ComputeSha256Hex(targetPath);
            if (!string.Equals(actualSha256, expectedSha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException($"SHA-256 mismatch for '{file.Path}'.");
        }
    }

    private ApplyUpdateResult HandleFailure(Exception ex, bool isInitialDeployment, string targetDeployment, ApplySnapshotMetadata snapshot, string snapshotPath, string sourceVersion, string targetVersion)
    {
        if (isInitialDeployment)
        {
            TryDeleteDirectory(targetDeployment);
            snapshot.Status = "failed";
            snapshotStore.Save(snapshotPath, snapshot);
            progressReporter.ReportComplete(new InstallCompleteReport(false, "0.0.0", targetVersion, ex.Message, false));
            return new ApplyUpdateResult
            {
                Success = false,
                Stage = "update.apply",
                Code = "initial_deploy_failed",
                Message = "Failed to apply initial PLONDS deployment.",
                ErrorMessage = ex.Message,
                CurrentVersion = "0.0.0",
                TargetVersion = targetVersion
            };
        }

        progressReporter.ReportProgress(new InstallProgressReport(InstallStage.RollingBack, "Rolling back...", 0, null, 0, 0));
        var rollbackResult = deploymentActivator.TryRollbackOnFailure(snapshot);
        snapshot.Status = rollbackResult.Success ? "rolled_back" : "rollback_failed";
        snapshotStore.Save(snapshotPath, snapshot);

        var errorMessage = rollbackResult.Success ? ex.Message : $"{ex.Message}; rollback failed: {rollbackResult.ErrorMessage}";
        progressReporter.ReportComplete(new InstallCompleteReport(false, sourceVersion, targetVersion, errorMessage, rollbackResult.Success));

        return new ApplyUpdateResult
        {
            Success = false,
            Stage = "update.apply",
            Code = rollbackResult.Success ? "apply_failed" : "rollback_failed",
            Message = rollbackResult.Success ? "Failed to apply PLONDS update. Rolled back to previous version." : "Failed to apply PLONDS update and rollback failed.",
            ErrorMessage = errorMessage,
            CurrentVersion = sourceVersion,
            RolledBackTo = rollbackResult.Success ? sourceVersion : null
        };
    }

    private static ApplySnapshotMetadata BuildSnapshot(bool canResume, ApplyInstallCheckpoint? existingCheckpoint, string sourceVersion, string targetVersion, string? currentDeployment, string targetDeployment) =>
        new()
        {
            SnapshotId = canResume ? existingCheckpoint!.SnapshotId : Guid.NewGuid().ToString("N"),
            SourceVersion = sourceVersion,
            TargetVersion = targetVersion,
            CreatedAt = DateTimeOffset.UtcNow,
            SourceDirectory = currentDeployment ?? string.Empty,
            TargetDirectory = targetDeployment,
            Status = "pending"
        };

    private static ApplyInstallCheckpoint BuildCheckpoint(ApplySnapshotMetadata snapshot, string sourceVersion, string targetVersion, string? currentDeployment, string targetDeployment, bool isInitialDeployment) =>
        new()
        {
            SnapshotId = snapshot.SnapshotId,
            SourceVersion = sourceVersion,
            TargetVersion = targetVersion,
            SourceDirectory = currentDeployment,
            TargetDirectory = targetDeployment,
            IsInitialDeployment = isInitialDeployment
        };

    private static void ApplyUnixFileModeIfPresent(string targetPath, ApplyPlondsFileEntry file)
    {
        if (OperatingSystem.IsWindows() || !file.Metadata.TryGetValue("unixFileMode", out var rawMode) || string.IsNullOrWhiteSpace(rawMode)) return;
        try
        {
            var modeValue = Convert.ToInt32(rawMode.Trim(), 8);
            File.SetUnixFileMode(targetPath, (UnixFileMode)modeValue);
        }
        catch
        {
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, true);
        }
        catch
        {
        }
    }
}
