using System.Text.Json;
using LanMountainDesktop.Launcher.Models;

namespace LanMountainDesktop.Launcher.Update;

internal sealed class PendingUpdateDetector(
    DeploymentLocator deploymentLocator,
    UpdateEnginePaths paths,
    UpdateSignatureVerifier signatureVerifier)
{
    public LauncherResult CheckPendingUpdate()
    {
        if (File.Exists(paths.PlondsFileMapPath) && File.Exists(paths.PlondsSignaturePath))
        {
            var pdcFileMapText = File.ReadAllText(paths.PlondsFileMapPath);
            var pdcFileMap = JsonSerializer.Deserialize(pdcFileMapText, AppJsonContext.Default.PlondsFileMap);
            if (pdcFileMap is null)
            {
                return UpdateEngineResults.Failed("update.check", "invalid_manifest", "plonds-filemap.json is invalid.");
            }

            var pdcVerified = signatureVerifier.Verify(
                paths.PlondsFileMapPath,
                paths.PlondsSignaturePath,
                UpdateEnginePaths.PlondsSignatureFileName);
            if (!pdcVerified.Success)
            {
                return UpdateEngineResults.Failed("update.check", "signature_failed", pdcVerified.Message);
            }

            var pdcMetadata = PlondsManifestParser.LoadMetadata(paths.PlondsUpdateMetadataPath);
            return new LauncherResult
            {
                Success = true,
                Stage = "update.check",
                Code = "available",
                Message = "Pending PLONDS update is available.",
                CurrentVersion = deploymentLocator.GetCurrentVersion(),
                TargetVersion = PlondsManifestParser.ResolveTargetVersion(pdcFileMap, pdcMetadata)
            };
        }

        if (!File.Exists(paths.FileMapPath) || !File.Exists(paths.ArchivePath))
        {
            return new LauncherResult
            {
                Success = true,
                Stage = "update.check",
                Code = "noop",
                Message = "No pending update."
            };
        }

        var fileMapText = File.ReadAllText(paths.FileMapPath);
        var fileMap = JsonSerializer.Deserialize(fileMapText, AppJsonContext.Default.SignedFileMap);
        if (fileMap is null)
        {
            return UpdateEngineResults.Failed("update.check", "invalid_manifest", "files.json is invalid.");
        }

        var verified = signatureVerifier.Verify(paths.FileMapPath, paths.SignaturePath, UpdateEnginePaths.SignatureFileName);
        if (!verified.Success)
        {
            return UpdateEngineResults.Failed("update.check", "signature_failed", verified.Message);
        }

        return new LauncherResult
        {
            Success = true,
            Stage = "update.check",
            Code = "available",
            Message = "Pending update is available.",
            CurrentVersion = deploymentLocator.GetCurrentVersion(),
            TargetVersion = fileMap.ToVersion
        };
    }

    public LauncherResult ValidateIncomingState()
    {
        if (File.Exists(paths.ApplyLockPath))
        {
            return UpdateEngineResults.Failed("update.apply", "lock_conflict", "Another update apply operation is already in progress.");
        }

        if (!File.Exists(paths.DeploymentLockPath))
        {
            return UpdateEngineResults.Failed("update.apply", "staging_incomplete", "Deployment lock is missing. Please redownload the update.");
        }

        var hasPlondsMap = File.Exists(paths.PlondsFileMapPath);
        var hasLegacyMap = File.Exists(paths.FileMapPath);
        if (hasPlondsMap && !File.Exists(paths.DownloadMarkerPath))
        {
            return UpdateEngineResults.Failed("update.apply", "staging_incomplete", "Download marker is missing for pending PLONDS update.");
        }

        if (!hasPlondsMap && !hasLegacyMap)
        {
            return new LauncherResult
            {
                Success = true,
                Stage = "update.apply",
                Code = "noop",
                Message = "No update payload found."
            };
        }

        return new LauncherResult
        {
            Success = true,
            Stage = "update.apply",
            Code = "ok",
            Message = "Incoming update state validated."
        };
    }
}
