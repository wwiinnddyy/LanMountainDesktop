using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using LanMountainDesktop.Launcher.Models;

namespace LanMountainDesktop.Launcher.Services;

internal sealed class UpdateEngineService
{
    private const string LauncherDirectoryName = ".launcher";
    private const string UpdateDirectoryName = "update";
    private const string IncomingDirectoryName = "incoming";
    private const string SnapshotsDirectoryName = "snapshots";
    private const string SignedFileMapName = "files.json";
    private const string SignatureFileName = "files.json.sig";
    private const string ArchiveFileName = "update.zip";
    private const string PublicKeyFileName = "public-key.pem";

    private readonly DeploymentLocator _deploymentLocator;
    private readonly string _appRoot;
    private readonly string _launcherRoot;
    private readonly string _incomingRoot;
    private readonly string _snapshotsRoot;

    public UpdateEngineService(DeploymentLocator deploymentLocator)
    {
        _deploymentLocator = deploymentLocator;
        _appRoot = deploymentLocator.GetAppRoot();
        _launcherRoot = Path.Combine(_appRoot, LauncherDirectoryName);
        _incomingRoot = Path.Combine(_launcherRoot, UpdateDirectoryName, IncomingDirectoryName);
        _snapshotsRoot = Path.Combine(_launcherRoot, SnapshotsDirectoryName);
    }

    public LauncherResult CheckPendingUpdate()
    {
        var fileMapPath = Path.Combine(_incomingRoot, SignedFileMapName);
        var archivePath = Path.Combine(_incomingRoot, ArchiveFileName);
        var signaturePath = Path.Combine(_incomingRoot, SignatureFileName);
        if (!File.Exists(fileMapPath) || !File.Exists(archivePath))
        {
            return new LauncherResult
            {
                Success = true,
                Stage = "update.check",
                Code = "noop",
                Message = "No pending update."
            };
        }

        var fileMapText = File.ReadAllText(fileMapPath);
        var fileMap = JsonSerializer.Deserialize<SignedFileMap>(fileMapText);
        if (fileMap is null)
        {
            return Failed("update.check", "invalid_manifest", "files.json is invalid.");
        }

        var verified = VerifySignature(fileMapPath, signaturePath);
        if (!verified.Success)
        {
            return Failed("update.check", "signature_failed", verified.Message);
        }

        return new LauncherResult
        {
            Success = true,
            Stage = "update.check",
            Code = "available",
            Message = "Pending update is available.",
            CurrentVersion = _deploymentLocator.GetCurrentVersion(),
            TargetVersion = fileMap.ToVersion
        };
    }

    public async Task<LauncherResult> DownloadAsync(string manifestUrl, string signatureUrl, string archiveUrl, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_incomingRoot);
        using var client = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(2)
        };

        var manifestPath = Path.Combine(_incomingRoot, SignedFileMapName);
        var signaturePath = Path.Combine(_incomingRoot, SignatureFileName);
        var archivePath = Path.Combine(_incomingRoot, ArchiveFileName);

        await using (var stream = await client.GetStreamAsync(manifestUrl, cancellationToken).ConfigureAwait(false))
        await using (var output = File.Create(manifestPath))
        {
            await stream.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        }

        await using (var stream = await client.GetStreamAsync(signatureUrl, cancellationToken).ConfigureAwait(false))
        await using (var output = File.Create(signaturePath))
        {
            await stream.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        }

        await using (var stream = await client.GetStreamAsync(archiveUrl, cancellationToken).ConfigureAwait(false))
        await using (var output = File.Create(archivePath))
        {
            await stream.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        }

        return new LauncherResult
        {
            Success = true,
            Stage = "update.download",
            Code = "ok",
            Message = "Update downloaded."
        };
    }

    public LauncherResult ApplyPendingUpdate()
    {
        Directory.CreateDirectory(_incomingRoot);
        Directory.CreateDirectory(_snapshotsRoot);

        var fileMapPath = Path.Combine(_incomingRoot, SignedFileMapName);
        var signaturePath = Path.Combine(_incomingRoot, SignatureFileName);
        var archivePath = Path.Combine(_incomingRoot, ArchiveFileName);

        if (!File.Exists(fileMapPath) || !File.Exists(archivePath))
        {
            return new LauncherResult
            {
                Success = true,
                Stage = "update.apply",
                Code = "noop",
                Message = "No update payload found."
            };
        }

        var verifyResult = VerifySignature(fileMapPath, signaturePath);
        if (!verifyResult.Success)
        {
            return Failed("update.apply", "signature_failed", verifyResult.Message);
        }

        var fileMapText = File.ReadAllText(fileMapPath);
        var fileMap = JsonSerializer.Deserialize<SignedFileMap>(fileMapText);
        if (fileMap is null || fileMap.Files.Count == 0)
        {
            return Failed("update.apply", "invalid_manifest", "No update file entries were found.");
        }

        var currentDeployment = _deploymentLocator.FindCurrentDeploymentDirectory();
        if (string.IsNullOrWhiteSpace(currentDeployment))
        {
            return Failed("update.apply", "no_current_deployment", "Current deployment directory not found.");
        }

        var currentVersion = _deploymentLocator.GetCurrentVersion();
        if (!string.IsNullOrWhiteSpace(fileMap.FromVersion) &&
            !string.Equals(fileMap.FromVersion, currentVersion, StringComparison.OrdinalIgnoreCase))
        {
            return Failed(
                "update.apply",
                "version_mismatch",
                $"Update requires source version {fileMap.FromVersion} but current is {currentVersion}.");
        }

        var targetVersion = string.IsNullOrWhiteSpace(fileMap.ToVersion) ? currentVersion : fileMap.ToVersion!;
        var targetDeployment = _deploymentLocator.BuildNextDeploymentDirectory(targetVersion);
        var partialMarker = Path.Combine(targetDeployment, ".partial");
        var snapshot = new SnapshotMetadata
        {
            SnapshotId = Guid.NewGuid().ToString("N"),
            SourceVersion = currentVersion,
            TargetVersion = targetVersion,
            CreatedAt = DateTimeOffset.UtcNow,
            SourceDirectory = currentDeployment,
            TargetDirectory = targetDeployment,
            Status = "pending"
        };
        var snapshotPath = Path.Combine(_snapshotsRoot, $"{snapshot.SnapshotId}.json");

        var extractRoot = Path.Combine(_incomingRoot, "extracted");
        try
        {
            SaveSnapshot(snapshotPath, snapshot);

            if (Directory.Exists(extractRoot))
            {
                Directory.Delete(extractRoot, true);
            }

            Directory.CreateDirectory(extractRoot);
            ZipFile.ExtractToDirectory(archivePath, extractRoot, overwriteFiles: true);

            Directory.CreateDirectory(targetDeployment);
            File.WriteAllText(partialMarker, string.Empty);

            foreach (var file in fileMap.Files)
            {
                ApplyFileEntry(file, currentDeployment, targetDeployment, extractRoot);
            }

            foreach (var file in fileMap.Files)
            {
                if (!NeedsVerification(file))
                {
                    continue;
                }

                var fullPath = Path.Combine(targetDeployment, file.Path);
                var actualHash = ComputeSha256Hex(fullPath);
                if (!string.Equals(actualHash, file.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"File hash mismatch for '{file.Path}'.");
                }
            }

            ActivateDeployment(currentDeployment, targetDeployment);

            snapshot.Status = "applied";
            SaveSnapshot(snapshotPath, snapshot);
            CleanupIncomingArtifacts();
            CleanupDestroyedDeployments();

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
            TryRollbackOnFailure(snapshot);
            snapshot.Status = "rolled_back";
            SaveSnapshot(snapshotPath, snapshot);
            return new LauncherResult
            {
                Success = false,
                Stage = "update.apply",
                Code = "apply_failed",
                Message = "Failed to apply update. Rolled back to previous version.",
                ErrorMessage = ex.Message,
                CurrentVersion = currentVersion,
                RolledBackTo = currentVersion
            };
        }
        finally
        {
            try
            {
                if (Directory.Exists(extractRoot))
                {
                    Directory.Delete(extractRoot, true);
                }
            }
            catch
            {
            }
        }
    }

    public LauncherResult RollbackLatest()
    {
        if (!Directory.Exists(_snapshotsRoot))
        {
            return Failed("update.rollback", "no_snapshot", "No snapshot found.");
        }

        var snapshotPath = Directory
            .EnumerateFiles(_snapshotsRoot, "*.json", SearchOption.TopDirectoryOnly)
            .OrderByDescending(File.GetCreationTimeUtc)
            .FirstOrDefault();
        if (string.IsNullOrWhiteSpace(snapshotPath))
        {
            return Failed("update.rollback", "no_snapshot", "No snapshot found.");
        }

        var snapshot = JsonSerializer.Deserialize<SnapshotMetadata>(File.ReadAllText(snapshotPath));
        if (snapshot is null || string.IsNullOrWhiteSpace(snapshot.SourceDirectory))
        {
            return Failed("update.rollback", "invalid_snapshot", "Invalid snapshot metadata.");
        }

        var currentDeployment = _deploymentLocator.FindCurrentDeploymentDirectory();
        if (string.IsNullOrWhiteSpace(currentDeployment))
        {
            return Failed("update.rollback", "no_current_deployment", "Current deployment not found.");
        }

        ActivateDeployment(currentDeployment, snapshot.SourceDirectory);
        snapshot.Status = "manual_rollback";
        SaveSnapshot(snapshotPath, snapshot);

        return new LauncherResult
        {
            Success = true,
            Stage = "update.rollback",
            Code = "ok",
            Message = $"Rolled back to {snapshot.SourceVersion}.",
            RolledBackTo = snapshot.SourceVersion
        };
    }

    public void CleanupDestroyedDeployments()
    {
        foreach (var dir in Directory.EnumerateDirectories(_appRoot, "app-*", SearchOption.TopDirectoryOnly))
        {
            if (!File.Exists(Path.Combine(dir, ".destroy")))
            {
                continue;
            }

            try
            {
                Directory.Delete(dir, true);
            }
            catch
            {
            }
        }
    }

    private void ApplyFileEntry(UpdateFileEntry file, string currentDeployment, string targetDeployment, string extractRoot)
    {
        var normalizedPath = NormalizeRelativePath(file.Path);
        if (string.Equals(file.Action, "delete", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var targetPath = Path.Combine(targetDeployment, normalizedPath);
        EnsurePathWithinRoot(targetPath, targetDeployment);
        var targetDir = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrWhiteSpace(targetDir))
        {
            Directory.CreateDirectory(targetDir);
        }

        if (string.Equals(file.Action, "reuse", StringComparison.OrdinalIgnoreCase))
        {
            var sourcePath = Path.Combine(currentDeployment, normalizedPath);
            EnsurePathWithinRoot(sourcePath, currentDeployment);
            if (!File.Exists(sourcePath))
            {
                throw new FileNotFoundException($"Cannot reuse file '{file.Path}' because it was not found in current deployment.");
            }

            File.Copy(sourcePath, targetPath, overwrite: true);
            return;
        }

        var archiveRelative = string.IsNullOrWhiteSpace(file.ArchivePath) ? normalizedPath : NormalizeRelativePath(file.ArchivePath);
        var extractedPath = Path.Combine(extractRoot, archiveRelative);
        EnsurePathWithinRoot(extractedPath, extractRoot);
        if (!File.Exists(extractedPath))
        {
            throw new FileNotFoundException($"Archive file '{archiveRelative}' not found for '{file.Path}'.");
        }

        File.Copy(extractedPath, targetPath, overwrite: true);
    }

    private void ActivateDeployment(string fromDeployment, string toDeployment)
    {
        var toCurrent = Path.Combine(toDeployment, ".current");
        var fromCurrent = Path.Combine(fromDeployment, ".current");
        var fromDestroy = Path.Combine(fromDeployment, ".destroy");
        var toPartial = Path.Combine(toDeployment, ".partial");

        File.WriteAllText(toCurrent, string.Empty);
        if (File.Exists(fromCurrent))
        {
            File.Delete(fromCurrent);
        }

        File.WriteAllText(fromDestroy, string.Empty);
        if (File.Exists(toPartial))
        {
            File.Delete(toPartial);
        }
    }

    private void TryRollbackOnFailure(SnapshotMetadata snapshot)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(snapshot.TargetDirectory) && Directory.Exists(snapshot.TargetDirectory))
            {
                Directory.Delete(snapshot.TargetDirectory, true);
            }

            if (File.Exists(Path.Combine(snapshot.SourceDirectory, ".destroy")))
            {
                File.Delete(Path.Combine(snapshot.SourceDirectory, ".destroy"));
            }

            if (!File.Exists(Path.Combine(snapshot.SourceDirectory, ".current")))
            {
                File.WriteAllText(Path.Combine(snapshot.SourceDirectory, ".current"), string.Empty);
            }
        }
        catch
        {
        }
    }

    private void CleanupIncomingArtifacts()
    {
        foreach (var path in new[]
                 {
                     Path.Combine(_incomingRoot, SignedFileMapName),
                     Path.Combine(_incomingRoot, SignatureFileName),
                     Path.Combine(_incomingRoot, ArchiveFileName)
                 })
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
            }
        }
    }

    private (bool Success, string Message) VerifySignature(string fileMapPath, string signaturePath)
    {
        if (!File.Exists(signaturePath))
        {
            return (false, "Missing files.json.sig.");
        }

        var publicKeyPath = Path.Combine(_launcherRoot, UpdateDirectoryName, PublicKeyFileName);
        if (!File.Exists(publicKeyPath))
        {
            return (false, $"Missing public key: {publicKeyPath}");
        }

        var jsonBytes = File.ReadAllBytes(fileMapPath);
        var signatureBase64 = File.ReadAllText(signaturePath).Trim();
        if (string.IsNullOrWhiteSpace(signatureBase64))
        {
            return (false, "Signature is empty.");
        }

        byte[] signature;
        try
        {
            signature = Convert.FromBase64String(signatureBase64);
        }
        catch (FormatException)
        {
            return (false, "Signature is not valid base64.");
        }

        using var rsa = RSA.Create();
        rsa.ImportFromPem(File.ReadAllText(publicKeyPath));
        var isValid = rsa.VerifyData(jsonBytes, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return isValid ? (true, "ok") : (false, "Signature verification failed.");
    }

    private static string NormalizeRelativePath(string path)
    {
        var normalized = path.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        return normalized.TrimStart(Path.DirectorySeparatorChar);
    }

    private static void EnsurePathWithinRoot(string targetPath, string rootPath)
    {
        var fullTarget = Path.GetFullPath(targetPath);
        var fullRoot = Path.GetFullPath(rootPath);
        if (!fullTarget.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Path traversal detected: {targetPath}");
        }
    }

    private static bool NeedsVerification(UpdateFileEntry file)
    {
        return !string.Equals(file.Action, "delete", StringComparison.OrdinalIgnoreCase) &&
               !string.IsNullOrWhiteSpace(file.Sha256);
    }

    private static string ComputeSha256Hex(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        var hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void SaveSnapshot(string path, SnapshotMetadata snapshot)
    {
        File.WriteAllText(path, JsonSerializer.Serialize(snapshot, new JsonSerializerOptions
        {
            WriteIndented = true
        }));
    }

    private static LauncherResult Failed(string stage, string code, string message)
    {
        return new LauncherResult
        {
            Success = false,
            Stage = stage,
            Code = code,
            Message = message,
            ErrorMessage = message
        };
    }
}
