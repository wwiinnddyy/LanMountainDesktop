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
    private const string VelopackReleasesFileName = "releases.win.json";
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
        var velopackFeedPath = Path.Combine(_incomingRoot, VelopackReleasesFileName);
        if (File.Exists(velopackFeedPath))
        {
            var velopackResult = CheckVelopackPendingUpdate(velopackFeedPath);
            if (velopackResult is not null)
            {
                return velopackResult;
            }
        }

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
        var fileMap = JsonSerializer.Deserialize(fileMapText, AppJsonContext.Default.SignedFileMap);
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

    public async Task<LauncherResult> DownloadVelopackAsync(
        string releasesJsonUrl,
        IReadOnlyList<string> packageUrls,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(releasesJsonUrl))
        {
            return Failed("update.download", "invalid_argument", "Missing releases feed url.");
        }

        Directory.CreateDirectory(_incomingRoot);

        using var client = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(2)
        };

        var releasesPath = Path.Combine(_incomingRoot, VelopackReleasesFileName);
        await DownloadToFileAsync(client, releasesJsonUrl, releasesPath, cancellationToken).ConfigureAwait(false);

        foreach (var url in packageUrls.Where(u => !string.IsNullOrWhiteSpace(u)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var fileName = Path.GetFileName(new Uri(url).AbsolutePath);
            if (string.IsNullOrWhiteSpace(fileName))
            {
                continue;
            }

            var destination = Path.Combine(_incomingRoot, fileName);
            await DownloadToFileAsync(client, url, destination, cancellationToken).ConfigureAwait(false);
        }

        return new LauncherResult
        {
            Success = true,
            Stage = "update.download",
            Code = "ok",
            Message = "Velopack update payload downloaded."
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

    public async Task<LauncherResult> ApplyPendingUpdateAsync()
    {
        Directory.CreateDirectory(_incomingRoot);
        Directory.CreateDirectory(_snapshotsRoot);

        var velopackFeedPath = Path.Combine(_incomingRoot, VelopackReleasesFileName);
        if (File.Exists(velopackFeedPath))
        {
            return await ApplyVelopackPendingUpdateAsync(velopackFeedPath).ConfigureAwait(false);
        }

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

        var fileMapText = await File.ReadAllTextAsync(fileMapPath);
        var fileMap = JsonSerializer.Deserialize(fileMapText, AppJsonContext.Default.SignedFileMap);
        if (fileMap is null || fileMap.Files.Count == 0)
        {
            return Failed("update.apply", "invalid_manifest", "No update file entries were found.");
        }

        var currentDeployment = _deploymentLocator.FindCurrentDeploymentDirectory();
        if (string.IsNullOrWhiteSpace(currentDeployment))
        {
            // 全新安装场景：没有当前部署目录，但有更新包
            // 这种情况下应该直接应用更新作为首次安装
            return await ApplyInitialDeploymentAsync(fileMap, archivePath, fileMapPath, signaturePath);
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
            // 清理旧版本，但保留最近3个版本以支持回滚
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

    /// <summary>
    /// 全新安装场景：直接应用更新包作为首次部署
    /// </summary>
    private async Task<LauncherResult> ApplyInitialDeploymentAsync(
        SignedFileMap fileMap,
        string archivePath,
        string fileMapPath,
        string signaturePath)
    {
        var targetVersion = string.IsNullOrWhiteSpace(fileMap.ToVersion) ? "1.0.0" : fileMap.ToVersion!;
        var targetDeployment = _deploymentLocator.BuildNextDeploymentDirectory(targetVersion);
        var partialMarker = Path.Combine(targetDeployment, ".partial");
        var snapshotPath = Path.Combine(_snapshotsRoot, $"initial-{Guid.NewGuid():N}.json");

        var extractRoot = Path.Combine(_incomingRoot, "extracted");
        try
        {
            // 保存快照（用于回滚，虽然首次安装回滚意义不大）
            var snapshot = new SnapshotMetadata
            {
                SnapshotId = Guid.NewGuid().ToString("N"),
                SourceVersion = "0.0.0",
                TargetVersion = targetVersion,
                CreatedAt = DateTimeOffset.UtcNow,
                SourceDirectory = "",
                TargetDirectory = targetDeployment,
                Status = "pending"
            };
            SaveSnapshot(snapshotPath, snapshot);

            // 清理并解压更新包
            if (Directory.Exists(extractRoot))
            {
                Directory.Delete(extractRoot, true);
            }
            Directory.CreateDirectory(extractRoot);
            ZipFile.ExtractToDirectory(archivePath, extractRoot, overwriteFiles: true);

            // 创建目标部署目录
            Directory.CreateDirectory(targetDeployment);
            File.WriteAllText(partialMarker, string.Empty);

            // 应用所有文件（全新安装时，所有文件都是新增或替换）
            foreach (var file in fileMap.Files)
            {
                ApplyInitialFileEntry(file, targetDeployment, extractRoot);
            }

            // 验证文件哈希
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

            // 激活部署（创建 .current 标记，删除 .partial 标记）
            var currentMarker = Path.Combine(targetDeployment, ".current");
            File.WriteAllText(currentMarker, string.Empty);
            if (File.Exists(partialMarker))
            {
                File.Delete(partialMarker);
            }

            // 清理更新包
            snapshot.Status = "applied";
            SaveSnapshot(snapshotPath, snapshot);
            CleanupIncomingArtifacts();

            return new LauncherResult
            {
                Success = true,
                Stage = "update.apply",
                Code = "ok",
                Message = $"Initial deployment to {targetVersion}.",
                CurrentVersion = "0.0.0",
                TargetVersion = targetVersion
            };
        }
        catch (Exception ex)
        {
            // 清理失败的目标目录
            try
            {
                if (Directory.Exists(targetDeployment))
                {
                    Directory.Delete(targetDeployment, true);
                }
            }
            catch
            {
            }

            return new LauncherResult
            {
                Success = false,
                Stage = "update.apply",
                Code = "initial_deploy_failed",
                Message = "Failed to apply initial deployment.",
                ErrorMessage = ex.Message,
                CurrentVersion = "0.0.0",
                TargetVersion = targetVersion
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

    /// <summary>
    /// 应用初始部署文件（全新安装场景，不需要源目录）
    /// </summary>
    private void ApplyInitialFileEntry(UpdateFileEntry file, string targetDeployment, string extractRoot)
    {
        var normalizedPath = NormalizeRelativePath(file.Path);

        // 删除操作在全新安装时忽略
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

        // 无论是 add 还是 replace，都从压缩包复制
        var archiveRelative = string.IsNullOrWhiteSpace(file.ArchivePath) ? normalizedPath : NormalizeRelativePath(file.ArchivePath);
        var extractedPath = Path.Combine(extractRoot, archiveRelative);
        EnsurePathWithinRoot(extractedPath, extractRoot);

        if (!File.Exists(extractedPath))
        {
            throw new FileNotFoundException($"Archive file '{archiveRelative}' not found for '{file.Path}'.");
        }

        File.Copy(extractedPath, targetPath, overwrite: true);
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

        var snapshot = JsonSerializer.Deserialize(File.ReadAllText(snapshotPath), AppJsonContext.Default.SnapshotMetadata);
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
                     Path.Combine(_incomingRoot, ArchiveFileName),
                     Path.Combine(_incomingRoot, VelopackReleasesFileName)
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

        try
        {
            foreach (var nupkgPath in Directory.EnumerateFiles(_incomingRoot, "*.nupkg", SearchOption.TopDirectoryOnly))
            {
                File.Delete(nupkgPath);
            }
        }
        catch
        {
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

    private LauncherResult? CheckVelopackPendingUpdate(string feedPath)
    {
        try
        {
            var feed = JsonSerializer.Deserialize(File.ReadAllText(feedPath), AppJsonContext.Default.VelopackReleaseFeed);
            if (feed?.Assets is null || feed.Assets.Count == 0)
            {
                return Failed("update.check", "invalid_manifest", "releases.win.json is invalid.");
            }

            var currentVersion = ParseVersionSafe(_deploymentLocator.GetCurrentVersion());
            var latest = feed.Assets
                .Where(a => string.Equals(a.Type, "Full", StringComparison.OrdinalIgnoreCase))
                .Select(a => new { Asset = a, Version = ParseVersionSafe(a.Version) })
                .Where(x => x.Version > currentVersion)
                .OrderByDescending(x => x.Version)
                .FirstOrDefault();

            if (latest is null)
            {
                return new LauncherResult
                {
                    Success = true,
                    Stage = "update.check",
                    Code = "noop",
                    Message = "No pending update for current version."
                };
            }

            var packagePath = Path.Combine(_incomingRoot, latest.Asset.FileName);
            if (!File.Exists(packagePath))
            {
                return Failed("update.check", "missing_payload", $"Missing Velopack package '{latest.Asset.FileName}'.");
            }

            return new LauncherResult
            {
                Success = true,
                Stage = "update.check",
                Code = "available",
                Message = "Pending Velopack update is available.",
                CurrentVersion = _deploymentLocator.GetCurrentVersion(),
                TargetVersion = latest.Asset.Version
            };
        }
        catch (Exception ex)
        {
            return Failed("update.check", "invalid_manifest", ex.Message);
        }
    }

    private async Task<LauncherResult> ApplyVelopackPendingUpdateAsync(string feedPath)
    {
        VelopackReleaseFeed? feed;
        try
        {
            var json = await File.ReadAllTextAsync(feedPath).ConfigureAwait(false);
            feed = JsonSerializer.Deserialize(json, AppJsonContext.Default.VelopackReleaseFeed);
        }
        catch (Exception ex)
        {
            return Failed("update.apply", "invalid_manifest", $"Invalid releases feed: {ex.Message}");
        }

        if (feed?.Assets is null || feed.Assets.Count == 0)
        {
            return Failed("update.apply", "invalid_manifest", "releases.win.json has no assets.");
        }

        var currentDeployment = _deploymentLocator.FindCurrentDeploymentDirectory();
        if (string.IsNullOrWhiteSpace(currentDeployment))
        {
            return Failed("update.apply", "no_current_deployment", "Current deployment not found.");
        }

        var currentVersionText = _deploymentLocator.GetCurrentVersion();
        var currentVersion = ParseVersionSafe(currentVersionText);
        var target = feed.Assets
            .Where(a => string.Equals(a.Type, "Full", StringComparison.OrdinalIgnoreCase))
            .Select(a => new { Asset = a, Version = ParseVersionSafe(a.Version) })
            .Where(x => x.Version > currentVersion)
            .OrderByDescending(x => x.Version)
            .FirstOrDefault();

        if (target is null)
        {
            return new LauncherResult
            {
                Success = true,
                Stage = "update.apply",
                Code = "noop",
                Message = "No Velopack update payload found."
            };
        }

        var packagePath = Path.Combine(_incomingRoot, target.Asset.FileName);
        if (!File.Exists(packagePath))
        {
            return Failed("update.apply", "missing_payload", $"Missing Velopack package '{target.Asset.FileName}'.");
        }

        if (!VerifyVelopackPackageChecksum(packagePath, target.Asset))
        {
            return Failed("update.apply", "checksum_failed", "Velopack package checksum verification failed.");
        }

        var targetVersion = string.IsNullOrWhiteSpace(target.Asset.Version) ? currentVersionText : target.Asset.Version;
        var targetDeployment = _deploymentLocator.BuildNextDeploymentDirectory(targetVersion);
        var partialMarker = Path.Combine(targetDeployment, ".partial");
        var snapshot = new SnapshotMetadata
        {
            SnapshotId = Guid.NewGuid().ToString("N"),
            SourceVersion = currentVersionText,
            TargetVersion = targetVersion,
            CreatedAt = DateTimeOffset.UtcNow,
            SourceDirectory = currentDeployment,
            TargetDirectory = targetDeployment,
            Status = "pending"
        };
        var snapshotPath = Path.Combine(_snapshotsRoot, $"{snapshot.SnapshotId}.json");
        var extractRoot = Path.Combine(_incomingRoot, "extracted-velopack");

        try
        {
            SaveSnapshot(snapshotPath, snapshot);

            if (Directory.Exists(extractRoot))
            {
                Directory.Delete(extractRoot, true);
            }

            Directory.CreateDirectory(extractRoot);
            ZipFile.ExtractToDirectory(packagePath, extractRoot, overwriteFiles: true);

            var contentRoot = ResolveVelopackContentRoot(extractRoot);
            if (contentRoot is null)
            {
                throw new InvalidOperationException("Unable to locate app payload in Velopack package.");
            }

            Directory.CreateDirectory(targetDeployment);
            File.WriteAllText(partialMarker, string.Empty);
            CopyDirectory(contentRoot, targetDeployment);

            var hostExecutable = OperatingSystem.IsWindows() ? "LanMountainDesktop.exe" : "LanMountainDesktop";
            if (!File.Exists(Path.Combine(targetDeployment, hostExecutable)))
            {
                throw new InvalidOperationException($"Host executable '{hostExecutable}' not found after applying Velopack package.");
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
                CurrentVersion = currentVersionText,
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
                CurrentVersion = currentVersionText,
                RolledBackTo = currentVersionText
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

    private static Version ParseVersionSafe(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return new Version(0, 0, 0);
        }

        var normalized = version.Trim();
        var separatorIndex = normalized.IndexOfAny(['-', '+', ' ']);
        if (separatorIndex > 0)
        {
            normalized = normalized[..separatorIndex];
        }

        return Version.TryParse(normalized, out var parsed) ? parsed : new Version(0, 0, 0);
    }

    private static bool VerifyVelopackPackageChecksum(string packagePath, VelopackReleaseAsset asset)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(asset.SHA256))
            {
                var actualSha256 = ComputeSha256Hex(packagePath);
                return string.Equals(actualSha256, asset.SHA256, StringComparison.OrdinalIgnoreCase);
            }

            if (!string.IsNullOrWhiteSpace(asset.SHA1))
            {
                using var stream = File.OpenRead(packagePath);
                var sha1 = SHA1.HashData(stream);
                var actualSha1 = Convert.ToHexString(sha1);
                return string.Equals(actualSha1, asset.SHA1, StringComparison.OrdinalIgnoreCase);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string? ResolveVelopackContentRoot(string extractRoot)
    {
        var hostExecutable = OperatingSystem.IsWindows() ? "LanMountainDesktop.exe" : "LanMountainDesktop";
        var hostPath = Directory
            .EnumerateFiles(extractRoot, hostExecutable, SearchOption.AllDirectories)
            .FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(hostPath))
        {
            return Path.GetDirectoryName(hostPath);
        }

        // common nupkg layout fallback
        var libRoot = Path.Combine(extractRoot, "lib");
        if (Directory.Exists(libRoot))
        {
            var best = Directory.GetDirectories(libRoot, "*", SearchOption.TopDirectoryOnly)
                .OrderByDescending(d => Directory.EnumerateFiles(d, "*", SearchOption.AllDirectories).Count())
                .FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(best))
            {
                return best;
            }
        }

        var candidate = Directory.GetDirectories(extractRoot, "*", SearchOption.TopDirectoryOnly)
            .Where(d => !string.Equals(Path.GetFileName(d), "_rels", StringComparison.OrdinalIgnoreCase))
            .Where(d => !string.Equals(Path.GetFileName(d), "package", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(d => Directory.EnumerateFiles(d, "*", SearchOption.AllDirectories).Count())
            .FirstOrDefault();

        return candidate;
    }

    private static void CopyDirectory(string sourceDir, string targetDir)
    {
        foreach (var dirPath in Directory.EnumerateDirectories(sourceDir, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDir, dirPath);
            Directory.CreateDirectory(Path.Combine(targetDir, relative));
        }

        foreach (var sourceFile in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDir, sourceFile);
            var destFile = Path.Combine(targetDir, relative);
            var destDir = Path.GetDirectoryName(destFile);
            if (!string.IsNullOrWhiteSpace(destDir))
            {
                Directory.CreateDirectory(destDir);
            }
            File.Copy(sourceFile, destFile, overwrite: true);
        }
    }

    private static async Task DownloadToFileAsync(HttpClient client, string url, string destination, CancellationToken cancellationToken)
    {
        await using var stream = await client.GetStreamAsync(url, cancellationToken).ConfigureAwait(false);
        await using var output = File.Create(destination);
        await stream.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
    }

    private static void SaveSnapshot(string path, SnapshotMetadata snapshot)
    {
        File.WriteAllText(path, JsonSerializer.Serialize(snapshot, AppJsonContext.Default.SnapshotMetadata));
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
