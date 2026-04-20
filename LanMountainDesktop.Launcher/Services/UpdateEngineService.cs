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
    private const string PlondsFileMapName = "plonds-filemap.json";
    private const string PlondsSignatureFileName = "plonds-filemap.sig";
    private const string PlondsUpdateMetadataName = "plonds-update.json";
    private const string PlondsObjectsDirectoryName = "objects";
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
        var pdcFileMapPath = Path.Combine(_incomingRoot, PlondsFileMapName);
        var pdcSignaturePath = Path.Combine(_incomingRoot, PlondsSignatureFileName);
        var pdcUpdatePath = Path.Combine(_incomingRoot, PlondsUpdateMetadataName);
        if (File.Exists(pdcFileMapPath) && File.Exists(pdcSignaturePath))
        {
            var pdcFileMapText = File.ReadAllText(pdcFileMapPath);
            var pdcFileMap = JsonSerializer.Deserialize(pdcFileMapText, AppJsonContext.Default.PlondsFileMap);
            if (pdcFileMap is null)
            {
                return Failed("update.check", "invalid_manifest", "plonds-filemap.json is invalid.");
            }

            var pdcVerified = VerifySignature(pdcFileMapPath, pdcSignaturePath, PlondsSignatureFileName);
            if (!pdcVerified.Success)
            {
                return Failed("update.check", "signature_failed", pdcVerified.Message);
            }

            var pdcMetadata = LoadPlondsUpdateMetadata(pdcUpdatePath);
            return new LauncherResult
            {
                Success = true,
                Stage = "update.check",
                Code = "available",
                Message = "Pending PLONDS update is available.",
                CurrentVersion = _deploymentLocator.GetCurrentVersion(),
                TargetVersion = ResolvePlondsTargetVersion(pdcFileMap, pdcMetadata)
            };
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

        var verified = VerifySignature(fileMapPath, signaturePath, SignatureFileName);
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
        Directory.CreateDirectory(_incomingRoot);
        Directory.CreateDirectory(_snapshotsRoot);

        var pdcFileMapPath = Path.Combine(_incomingRoot, PlondsFileMapName);
        var pdcSignaturePath = Path.Combine(_incomingRoot, PlondsSignatureFileName);
        var pdcUpdatePath = Path.Combine(_incomingRoot, PlondsUpdateMetadataName);
        if (File.Exists(pdcFileMapPath) && File.Exists(pdcSignaturePath))
        {
            return await ApplyPendingPlondsUpdateAsync(pdcFileMapPath, pdcSignaturePath, pdcUpdatePath);
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

        var verifyResult = VerifySignature(fileMapPath, signaturePath, SignatureFileName);
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
            // Initial install path: no current deployment exists, so apply the staged package directly.
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
            // 婵炴挸鎳愰幃濠囧籍瑜忔晶妤呭嫉椤掑﹦绀夊ù锝呮缁绘岸鎮惧▎鎰粯閺?濞戞搩浜炴晶妤呭嫉椤戝じ绨伴柡鈧娑樼槷闁搞儳鍋炵划?
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

    private async Task<LauncherResult> ApplyPendingPlondsUpdateAsync(
        string pdcFileMapPath,
        string pdcSignaturePath,
        string pdcUpdatePath)
    {
        var verifyResult = VerifySignature(pdcFileMapPath, pdcSignaturePath, PlondsSignatureFileName);
        if (!verifyResult.Success)
        {
            return Failed("update.apply", "signature_failed", verifyResult.Message);
        }

        var fileMapText = await File.ReadAllTextAsync(pdcFileMapPath).ConfigureAwait(false);
        var fileMap = JsonSerializer.Deserialize(fileMapText, AppJsonContext.Default.PlondsFileMap) ?? new PlondsFileMap();
        var fileEntries = CollectPlondsFileEntries(fileMap);
        if (fileEntries.Count == 0)
        {
            PopulatePlondsManifestFromRawJson(fileMapText, fileMap, fileEntries);
        }

        if (fileEntries.Count == 0)
        {
            return Failed("update.apply", "invalid_manifest", "No PLONDS file entries were found.");
        }

        var pdcMetadata = LoadPlondsUpdateMetadata(pdcUpdatePath);

        var currentDeployment = _deploymentLocator.FindCurrentDeploymentDirectory();
        var currentVersion = _deploymentLocator.GetCurrentVersion();
        var sourceVersion = string.IsNullOrWhiteSpace(currentVersion) ? "0.0.0" : currentVersion;
        var expectedSourceVersion = ResolvePlondsSourceVersion(fileMap, pdcMetadata);
        if (!string.IsNullOrWhiteSpace(expectedSourceVersion) &&
            !string.Equals(expectedSourceVersion, sourceVersion, StringComparison.OrdinalIgnoreCase))
        {
            return Failed(
                "update.apply",
                "version_mismatch",
                $"PLONDS update requires source version {expectedSourceVersion} but current is {sourceVersion}.");
        }

        var targetVersion = ResolvePlondsTargetVersion(fileMap, pdcMetadata);
        if (string.IsNullOrWhiteSpace(targetVersion))
        {
            targetVersion = sourceVersion;
        }

        var isInitialDeployment = string.IsNullOrWhiteSpace(currentDeployment);
        var targetDeployment = _deploymentLocator.BuildNextDeploymentDirectory(targetVersion!);
        var partialMarker = Path.Combine(targetDeployment, ".partial");
        var snapshot = new SnapshotMetadata
        {
            SnapshotId = Guid.NewGuid().ToString("N"),
            SourceVersion = sourceVersion,
            TargetVersion = targetVersion,
            CreatedAt = DateTimeOffset.UtcNow,
            SourceDirectory = currentDeployment ?? string.Empty,
            TargetDirectory = targetDeployment,
            Status = "pending"
        };
        var snapshotPath = Path.Combine(_snapshotsRoot, $"{snapshot.SnapshotId}.json");

        try
        {
            SaveSnapshot(snapshotPath, snapshot);

            if (Directory.Exists(targetDeployment))
            {
                Directory.Delete(targetDeployment, true);
            }

            Directory.CreateDirectory(targetDeployment);
            File.WriteAllText(partialMarker, string.Empty);

            foreach (var entry in fileEntries)
            {
                ApplyPlondsFileEntry(entry, currentDeployment, targetDeployment);
            }

            foreach (var entry in fileEntries)
            {
                VerifyPlondsFileEntry(entry, targetDeployment);
            }

            if (isInitialDeployment)
            {
                File.WriteAllText(Path.Combine(targetDeployment, ".current"), string.Empty);
                if (File.Exists(partialMarker))
                {
                    File.Delete(partialMarker);
                }
            }
            else
            {
                ActivateDeployment(currentDeployment!, targetDeployment);
            }

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
                CurrentVersion = sourceVersion,
                TargetVersion = targetVersion
            };
        }
        catch (Exception ex)
        {
            if (isInitialDeployment)
            {
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

                snapshot.Status = "failed";
                SaveSnapshot(snapshotPath, snapshot);
                return new LauncherResult
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

            TryRollbackOnFailure(snapshot);
            snapshot.Status = "rolled_back";
            SaveSnapshot(snapshotPath, snapshot);
            return new LauncherResult
            {
                Success = false,
                Stage = "update.apply",
                Code = "apply_failed",
                Message = "Failed to apply PLONDS update. Rolled back to previous version.",
                ErrorMessage = ex.Message,
                CurrentVersion = sourceVersion,
                RolledBackTo = sourceVersion
            };
        }
    }

    private void ApplyPlondsFileEntry(PlondsFileEntry file, string? currentDeployment, string targetDeployment)
    {
        var normalizedPath = NormalizeRelativePath(file.Path);
        var action = string.IsNullOrWhiteSpace(file.Action) ? "replace" : file.Action!;
        if (string.Equals(action, "delete", StringComparison.OrdinalIgnoreCase))
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

        if (string.Equals(action, "reuse", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(currentDeployment))
            {
                throw new FileNotFoundException($"Cannot reuse file '{file.Path}' because no source deployment is available.");
            }

            var sourcePath = Path.Combine(currentDeployment, normalizedPath);
            EnsurePathWithinRoot(sourcePath, currentDeployment);
            if (!File.Exists(sourcePath))
            {
                throw new FileNotFoundException($"Cannot reuse file '{file.Path}' because it was not found in current deployment.");
            }

            File.Copy(sourcePath, targetPath, overwrite: true);
            return;
        }

        var objectPath = ResolvePlondsObjectPath(file);
        var objectBytes = File.ReadAllBytes(objectPath);
        var restoredBytes = TryInflateGzip(objectBytes) ?? objectBytes;
        File.WriteAllBytes(targetPath, restoredBytes);
    }

    private void VerifyPlondsFileEntry(PlondsFileEntry file, string targetDeployment)
    {
        var action = string.IsNullOrWhiteSpace(file.Action) ? "replace" : file.Action!;
        if (string.Equals(action, "delete", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var targetPath = Path.Combine(targetDeployment, NormalizeRelativePath(file.Path));
        EnsurePathWithinRoot(targetPath, targetDeployment);
        if (!File.Exists(targetPath))
        {
            throw new FileNotFoundException($"Expected target file was not created: {file.Path}");
        }

        if (TryGetExpectedSha512(file, out var expectedSha512))
        {
            var actualSha512 = ComputeSha512(targetPath);
            if (!actualSha512.AsSpan().SequenceEqual(expectedSha512))
            {
                throw new InvalidOperationException($"SHA-512 mismatch for '{file.Path}'.");
            }
            return;
        }

        if (!string.IsNullOrWhiteSpace(file.Sha256))
        {
            var expectedSha256 = NormalizeHashText(file.Sha256);
            var actualSha256 = ComputeSha256Hex(targetPath);
            if (!string.Equals(actualSha256, expectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"SHA-256 mismatch for '{file.Path}'.");
            }
        }
    }

    private string ResolvePlondsObjectPath(PlondsFileEntry file)
    {
        var candidates = new List<string>();
        AddPlondsPathCandidates(candidates, file.ObjectPath);
        AddPlondsPathCandidates(candidates, file.ObjectKey);
        AddPlondsPathCandidates(candidates, file.ArchivePath);
        AddPlondsPathCandidates(candidates, file.ObjectUrl);
        AddPlondsPathCandidates(candidates, file.Url);

        if (TryGetExpectedObjectSha512(file, out var expectedSha512) || TryGetExpectedSha512(file, out expectedSha512))
        {
            var hashHex = Convert.ToHexString(expectedSha512).ToLowerInvariant();
            AddPlondsPathCandidates(candidates, Path.Combine(PlondsObjectsDirectoryName, hashHex));
            if (hashHex.Length > 2)
            {
                AddPlondsPathCandidates(candidates, Path.Combine(PlondsObjectsDirectoryName, hashHex[..2], hashHex));
                // Backward compatibility for previously staged paths.
                AddPlondsPathCandidates(candidates, Path.Combine(PlondsObjectsDirectoryName, hashHex[..2], hashHex[2..]));
            }
            AddPlondsPathCandidates(candidates, Path.Combine(PlondsObjectsDirectoryName, $"{hashHex}.gz"));
        }

        foreach (var relativePath in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var fullPath = Path.GetFullPath(Path.Combine(_incomingRoot, relativePath));
            if (!fullPath.StartsWith(Path.GetFullPath(_incomingRoot), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (File.Exists(fullPath))
            {
                return fullPath;
            }
        }

        throw new FileNotFoundException($"Unable to resolve object payload for '{file.Path}'.");
    }

    private static byte[]? TryInflateGzip(byte[] payload)
    {
        try
        {
            using var input = new MemoryStream(payload, writable: false);
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            gzip.CopyTo(output);
            return output.ToArray();
        }
        catch
        {
            return null;
        }
    }

    private void AddPlondsPathCandidates(ICollection<string> candidates, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var normalized = value.Trim();
        if (Uri.TryCreate(normalized, UriKind.Absolute, out var absoluteUri))
        {
            normalized = Uri.UnescapeDataString(absoluteUri.AbsolutePath);
        }

        normalized = normalized.TrimStart('/', '\\');
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return;
        }

        normalized = normalized.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        candidates.Add(normalized);

        if (!normalized.StartsWith($"{PlondsObjectsDirectoryName}{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
        {
            candidates.Add(Path.Combine(PlondsObjectsDirectoryName, normalized));
        }

        var fileName = Path.GetFileName(normalized);
        if (!string.IsNullOrWhiteSpace(fileName))
        {
            candidates.Add(Path.Combine(PlondsObjectsDirectoryName, fileName));
        }
    }

    private static bool TryGetExpectedSha512(PlondsFileEntry file, out byte[] expected)
    {
        expected = [];
        if (file.Sha512Bytes is { Length: > 0 })
        {
            expected = file.Sha512Bytes;
            return true;
        }

        if (file.Hash is not null)
        {
            if (file.Hash.Bytes is { Length: > 0 })
            {
                expected = file.Hash.Bytes;
                return true;
            }

            if (string.IsNullOrWhiteSpace(file.Hash.Algorithm) ||
                file.Hash.Algorithm.Contains("sha512", StringComparison.OrdinalIgnoreCase))
            {
                if (TryParseHashBytes(file.Hash.Value, out expected))
                {
                    return true;
                }
            }
        }

        if (TryParseHashBytes(file.Sha512, out expected))
        {
            return true;
        }

        return TryParseHashBytes(file.Sha512Base64, out expected);
    }

    private static bool TryGetExpectedObjectSha512(PlondsFileEntry file, out byte[] expected)
    {
        expected = [];
        if (file.Hash is null)
        {
            return false;
        }

        if (file.Hash.Bytes is { Length: > 0 })
        {
            expected = file.Hash.Bytes;
            return true;
        }

        if (!string.IsNullOrWhiteSpace(file.Hash.Algorithm) &&
            !file.Hash.Algorithm.Contains("sha512", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return TryParseHashBytes(file.Hash.Value, out expected);
    }

    private static bool TryParseHashBytes(string? rawHash, out byte[] bytes)
    {
        bytes = [];
        if (string.IsNullOrWhiteSpace(rawHash))
        {
            return false;
        }

        var normalized = rawHash.Trim();
        var separator = normalized.IndexOf(':');
        if (separator >= 0 && separator < normalized.Length - 1)
        {
            normalized = normalized[(separator + 1)..].Trim();
        }

        var compact = normalized.Replace("-", string.Empty);
        if (compact.Length > 0 && compact.Length % 2 == 0 && IsHexString(compact))
        {
            try
            {
                bytes = Convert.FromHexString(compact);
                return true;
            }
            catch
            {
                return false;
            }
        }

        try
        {
            bytes = Convert.FromBase64String(normalized);
            return bytes.Length > 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsHexString(string value)
    {
        foreach (var ch in value)
        {
            if (!Uri.IsHexDigit(ch))
            {
                return false;
            }
        }

        return true;
    }

    private static string NormalizeHashText(string hash)
    {
        var normalized = hash.Trim();
        var separator = normalized.IndexOf(':');
        if (separator >= 0 && separator < normalized.Length - 1)
        {
            normalized = normalized[(separator + 1)..];
        }

        return normalized.Replace("-", string.Empty).Trim().ToLowerInvariant();
    }

    private static List<PlondsFileEntry> CollectPlondsFileEntries(PlondsFileMap fileMap)
    {
        var files = new List<PlondsFileEntry>();
        if (fileMap.Files is { Count: > 0 })
        {
            files.AddRange(fileMap.Files);
        }

        if (fileMap.Components is null)
        {
            return files;
        }

        foreach (var component in fileMap.Components)
        {
            if (component.Files is { Count: > 0 })
            {
                files.AddRange(component.Files);
            }
        }

        return files;
    }

    private static void PopulatePlondsManifestFromRawJson(string fileMapJson, PlondsFileMap fileMap, ICollection<PlondsFileEntry> files)
    {
        if (string.IsNullOrWhiteSpace(fileMapJson))
        {
            return;
        }

        using var document = JsonDocument.Parse(fileMapJson);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        fileMap.FromVersion ??= ReadJsonStringIgnoreCase(root, "fromversion");
        fileMap.ToVersion ??= ReadJsonStringIgnoreCase(root, "toversion");
        fileMap.Version ??= ReadJsonStringIgnoreCase(root, "version");
        fileMap.Platform ??= ReadJsonStringIgnoreCase(root, "platform");
        fileMap.Arch ??= ReadJsonStringIgnoreCase(root, "arch");
        fileMap.DistributionId ??= ReadJsonStringIgnoreCase(root, "distributionid");

        if (TryGetJsonPropertyIgnoreCase(root, "metadata", out var metadataNode) &&
            metadataNode.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in metadataNode.EnumerateObject())
            {
                var key = property.Name;
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                var value = property.Value.ValueKind == JsonValueKind.String
                    ? property.Value.GetString()
                    : property.Value.ToString();
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                fileMap.Metadata[key] = value;
            }
        }

        if (TryGetJsonPropertyIgnoreCase(root, "files", out var rootFilesNode))
        {
            ParsePlondsFilesNode(rootFilesNode, null, files);
        }

        if (!TryGetJsonPropertyIgnoreCase(root, "components", out var componentsNode))
        {
            return;
        }

        if (componentsNode.ValueKind == JsonValueKind.Object)
        {
            foreach (var component in componentsNode.EnumerateObject())
            {
                if (component.Value.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                if (TryGetJsonPropertyIgnoreCase(component.Value, "files", out var componentFilesNode))
                {
                    ParsePlondsFilesNode(componentFilesNode, component.Name, files);
                }
            }

            return;
        }

        if (componentsNode.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var component in componentsNode.EnumerateArray())
        {
            if (component.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var componentName = ReadJsonStringIgnoreCase(component, "name");
            if (TryGetJsonPropertyIgnoreCase(component, "files", out var componentFilesNode))
            {
                ParsePlondsFilesNode(componentFilesNode, componentName, files);
            }
        }
    }

    private static void ParsePlondsFilesNode(JsonElement filesNode, string? componentName, ICollection<PlondsFileEntry> files)
    {
        if (filesNode.ValueKind == JsonValueKind.Object)
        {
            foreach (var fileEntry in filesNode.EnumerateObject())
            {
                if (fileEntry.Value.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                if (TryCreatePlondsFileEntry(fileEntry.Name, componentName, fileEntry.Value, out var parsed))
                {
                    files.Add(parsed);
                }
            }

            return;
        }

        if (filesNode.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var fileEntry in filesNode.EnumerateArray())
        {
            if (fileEntry.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var fallbackPath = ReadJsonStringIgnoreCase(fileEntry, "path");
            if (TryCreatePlondsFileEntry(fallbackPath, componentName, fileEntry, out var parsed))
            {
                files.Add(parsed);
            }
        }
    }

    private static bool TryCreatePlondsFileEntry(string? fallbackPath, string? componentName, JsonElement node, out PlondsFileEntry entry)
    {
        entry = new PlondsFileEntry();
        var path = ReadJsonStringIgnoreCase(node, "path");
        if (string.IsNullOrWhiteSpace(path))
        {
            path = fallbackPath;
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var fileSha512 = ReadJsonByteArrayIgnoreCase(node, "filesha512")
                         ?? ReadJsonByteArrayIgnoreCase(node, "sha512");
        var archiveSha512 = ReadJsonByteArrayIgnoreCase(node, "archivesha512");

        var fileSha512Text = ReadJsonStringIgnoreCase(node, "filesha512")
                             ?? ReadJsonStringIgnoreCase(node, "sha512");
        var archiveSha512Text = ReadJsonStringIgnoreCase(node, "archivesha512");

        var downloadUrl = ReadJsonStringIgnoreCase(node, "archivedownloadurl")
                          ?? ReadJsonStringIgnoreCase(node, "downloadurl")
                          ?? ReadJsonStringIgnoreCase(node, "url");
        var objectPath = ReadJsonStringIgnoreCase(node, "objectpath")
                         ?? ReadJsonStringIgnoreCase(node, "archivepath");
        var objectKey = ReadJsonStringIgnoreCase(node, "objectkey");
        var action = ReadJsonStringIgnoreCase(node, "action");

        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(componentName))
        {
            metadata["component"] = componentName;
        }

        entry = new PlondsFileEntry
        {
            Path = path,
            Action = string.IsNullOrWhiteSpace(action) ? "replace" : action,
            Url = downloadUrl,
            ObjectUrl = ReadJsonStringIgnoreCase(node, "objecturl"),
            ObjectPath = objectPath,
            ObjectKey = objectKey,
            ArchivePath = ReadJsonStringIgnoreCase(node, "archivepath"),
            Sha256 = ReadJsonStringIgnoreCase(node, "sha256") ?? ReadJsonStringIgnoreCase(node, "filesha256"),
            Sha512 = fileSha512Text,
            Sha512Base64 = null,
            Sha512Bytes = fileSha512,
            Metadata = metadata
        };

        if (archiveSha512 is { Length: > 0 } || !string.IsNullOrWhiteSpace(archiveSha512Text))
        {
            entry.Hash = new PlondsHashDescriptor
            {
                Algorithm = "sha512",
                Bytes = archiveSha512,
                Value = archiveSha512Text ?? (archiveSha512 is { Length: > 0 }
                    ? Convert.ToHexString(archiveSha512).ToLowerInvariant()
                    : null)
            };
        }
        else if (TryGetJsonPropertyIgnoreCase(node, "hash", out var hashNode) && hashNode.ValueKind == JsonValueKind.Object)
        {
            entry.Hash = new PlondsHashDescriptor
            {
                Algorithm = ReadJsonStringIgnoreCase(hashNode, "algorithm"),
                Value = ReadJsonStringIgnoreCase(hashNode, "value"),
                Bytes = ReadJsonByteArrayIgnoreCase(hashNode, "bytes")
            };
        }

        return true;
    }

    private static bool TryGetJsonPropertyIgnoreCase(JsonElement node, string propertyName, out JsonElement value)
    {
        if (node.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in node.EnumerateObject())
            {
                if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    private static string? ReadJsonStringIgnoreCase(JsonElement node, string propertyName)
    {
        if (!TryGetJsonPropertyIgnoreCase(node, propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
                ? null
                : value.ToString();
    }

    private static byte[]? ReadJsonByteArrayIgnoreCase(JsonElement node, string propertyName)
    {
        if (!TryGetJsonPropertyIgnoreCase(node, propertyName, out var value))
        {
            return null;
        }

        return ParseJsonByteArrayValue(value);
    }

    private static byte[]? ParseJsonByteArrayValue(JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Array:
            {
                var bytes = new byte[value.GetArrayLength()];
                var index = 0;
                foreach (var element in value.EnumerateArray())
                {
                    if (!element.TryGetInt32(out var number) || number < byte.MinValue || number > byte.MaxValue)
                    {
                        return null;
                    }

                    bytes[index++] = (byte)number;
                }

                return bytes;
            }
            case JsonValueKind.String:
            {
                var text = value.GetString();
                return TryParseHashBytes(text, out var parsed) ? parsed : null;
            }
            default:
                return null;
        }
    }

    private static PlondsUpdateMetadata? LoadPlondsUpdateMetadata(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var text = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            return JsonSerializer.Deserialize(text, AppJsonContext.Default.PlondsUpdateMetadata);
        }
        catch
        {
            return null;
        }
    }

    private static string? ResolvePlondsSourceVersion(PlondsFileMap fileMap, PlondsUpdateMetadata? metadata)
    {
        return FirstNonEmpty(
            metadata?.FromVersion,
            fileMap.FromVersion,
            TryGetMetadataValue(fileMap.Metadata, "fromVersion"),
            TryGetMetadataValue(fileMap.Metadata, "sourceVersion"));
    }

    private static string? ResolvePlondsTargetVersion(PlondsFileMap fileMap, PlondsUpdateMetadata? metadata)
    {
        return FirstNonEmpty(
            metadata?.ToVersion,
            fileMap.ToVersion,
            fileMap.Version,
            TryGetMetadataValue(fileMap.Metadata, "toVersion"),
            TryGetMetadataValue(fileMap.Metadata, "targetVersion"));
    }

    private static string? TryGetMetadataValue(Dictionary<string, string>? metadata, string key)
    {
        if (metadata is null || metadata.Count == 0)
        {
            return null;
        }

        foreach (var pair in metadata)
        {
            if (!string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(pair.Value))
            {
                return pair.Value;
            }
        }

        return null;
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    /// <summary>
    /// 闁稿繈鍔嶉弻濠勨偓鐟邦槼椤ュ﹪宕烽悜妯荤彲闁挎稒姘ㄥú鍧楀箳閵夈儳瀹夐柣顫妽濞插潡寮弶鍨樁濞达絾绮堢拹鐔革純閺嶎煈鍋ч梺顔哄妿鐠?
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
            // Save a snapshot for diagnostics and future rollback consistency.
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

            // 婵炴挸鎳愰幃濠囩嵁閹澏鎺楀储鐎ｎ偅绾柡鍌涙緲鐎?
            if (Directory.Exists(extractRoot))
            {
                Directory.Delete(extractRoot, true);
            }
            Directory.CreateDirectory(extractRoot);
            ZipFile.ExtractToDirectory(archivePath, extractRoot, overwriteFiles: true);

            // 闁告帗绋戠紓鎾绘儎椤旂晫鍨奸梺顔哄妿鐠佹煡鎯勯鑲╃Э
            Directory.CreateDirectory(targetDeployment);
            File.WriteAllText(partialMarker, string.Empty);

            // Apply all files from the extracted payload into the first deployment directory.
            foreach (var file in fileMap.Files)
            {
                ApplyInitialFileEntry(file, targetDeployment, extractRoot);
            }

            // 濡ょ姴鐭侀惁澶愬棘閸ワ附顐介柛婵嗙墕缁?
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

            // Mark the deployment as current and remove the partial marker.
            var currentMarker = Path.Combine(targetDeployment, ".current");
            File.WriteAllText(currentMarker, string.Empty);
            if (File.Exists(partialMarker))
            {
                File.Delete(partialMarker);
            }

            // 婵炴挸鎳愰幃濠囧即鐎涙ɑ鐓€闁?            snapshot.Status = "applied";
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
            // Clean up the failed target deployment before returning the error result.
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
    /// 閹煎瓨姊婚弫銈夊礆濠靛棭娼楅梺顔哄妿鐠佹煡寮崶锔筋偨闁挎稑鐗嗛崣蹇涘棘閺夎法鏆旈悷浣告噹濠р偓闁哄拋鍨界槐婵囩▔瀹ュ浠橀悷鏇氱劍缁噣鎯勯鑲╃Э闁?    /// </summary>
    private void ApplyInitialFileEntry(UpdateFileEntry file, string targetDeployment, string extractRoot)
    {
        var normalizedPath = NormalizeRelativePath(file.Path);

        // 闁告帞濞€濞呭酣骞欏鍕▕闁革负鍔岄崣蹇涘棘閺夎法鏆旈悷浣告噺濡炲倽绠涢悾灞炬
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

        // 闁哄啰濮鹃鎴﹀及?add 閺夆晜蓱濡?replace闁挎稑鐭傞崗妯荤鎼粹€崇缂傚倵鏅涚€垫ɑ寰勫鍛厬
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
                     Path.Combine(_incomingRoot, PlondsFileMapName),
                     Path.Combine(_incomingRoot, PlondsSignatureFileName),
                     Path.Combine(_incomingRoot, PlondsUpdateMetadataName)
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

        foreach (var directory in new[]
                 {
                     Path.Combine(_incomingRoot, PlondsObjectsDirectoryName)
                 })
        {
            try
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, true);
                }
            }
            catch
            {
            }
        }
    }

    private (bool Success, string Message) VerifySignature(string payloadPath, string signaturePath, string signatureName)
    {
        if (!File.Exists(signaturePath))
        {
            return (false, $"Missing {signatureName}.");
        }

        var publicKeyPath = Path.Combine(_launcherRoot, UpdateDirectoryName, PublicKeyFileName);
        if (!File.Exists(publicKeyPath))
        {
            return (false, $"Missing public key: {publicKeyPath}");
        }

        var payloadBytes = File.ReadAllBytes(payloadPath);
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
        var isValid = rsa.VerifyData(payloadBytes, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
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

    private static byte[] ComputeSha512(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        return SHA512.HashData(stream);
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
