using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LanMountainDesktop;
using LanMountainDesktop.Launcher;
using LanMountainDesktop.Launcher.Models;
using LanMountainDesktop.Launcher.Services;
using LanMountainDesktop.Services;
using LanMountainDesktop.Services.Update;
using LanMountainDesktop.Shared.Contracts.Update;
using Xunit;

namespace LanMountainDesktop.Tests;

public sealed class UpdateEngineRollbackRegressionTests : IDisposable
{
    private readonly UpdateTestDirectory _directory = new();

    [Fact]
    public async Task ApplyPlondsUpdate_KeepsPreviousDeploymentForManualRollback()
    {
        var current = _directory.CreateDeployment("1.0.0", "old-state", isCurrent: true);
        var newState = Encoding.UTF8.GetBytes("new-state");

        _directory.StagePlondsUpdate("1.0.0", "1.1.0", newState, Sha256Hex(newState));

        var service = new UpdateEngineService(new DeploymentLocator(_directory.AppRoot));
        var result = await service.ApplyPendingUpdateAsync();

        Assert.True(result.Success, result.ErrorMessage);
        Assert.True(Directory.Exists(current));
        Assert.False(File.Exists(Path.Combine(current, ".current")));

        var rollback = service.RollbackLatest();

        Assert.True(rollback.Success, rollback.ErrorMessage);
        Assert.Equal("1.0.0", rollback.RolledBackTo);
        Assert.True(File.Exists(Path.Combine(current, ".current")));
        Assert.False(File.Exists(Path.Combine(current, ".destroy")));
        Assert.Equal("old-state", File.ReadAllText(Path.Combine(current, "state.txt")));
    }

    [Fact]
    public async Task ApplyPlondsUpdate_WhenObjectHashMismatches_RollsBackToPreviousDeployment()
    {
        var current = _directory.CreateDeployment("1.0.0", "old-state", isCurrent: true);
        var newState = Encoding.UTF8.GetBytes("new-state");

        _directory.StagePlondsUpdate("1.0.0", "1.1.0", newState, new string('0', 64));

        var service = new UpdateEngineService(new DeploymentLocator(_directory.AppRoot));
        var result = await service.ApplyPendingUpdateAsync();

        Assert.False(result.Success);
        Assert.Equal("apply_failed", result.Code);
        Assert.Equal("1.0.0", result.RolledBackTo);
        Assert.True(File.Exists(Path.Combine(current, ".current")));
        Assert.False(File.Exists(Path.Combine(current, ".destroy")));
        Assert.Equal("old-state", File.ReadAllText(Path.Combine(current, "state.txt")));
        Assert.Empty(Directory.GetDirectories(_directory.AppRoot, "app-1.1.0-*"));
    }

    [Fact]
    public void RollbackLatest_WhenSnapshotSourceDirectoryIsMissing_ReturnsStructuredFailure()
    {
        _directory.CreateDeployment("1.1.0", "new-state", isCurrent: true);
        _directory.WriteSnapshot(
            sourceVersion: "1.0.0",
            sourceDirectory: Path.Combine(_directory.AppRoot, "app-1.0.0-0"),
            targetVersion: "1.1.0",
            targetDirectory: Path.Combine(_directory.AppRoot, "app-1.1.0-0"));

        var service = new UpdateEngineService(new DeploymentLocator(_directory.AppRoot));
        var result = service.RollbackLatest();

        Assert.False(result.Success);
        Assert.Equal("source_missing", result.Code);
        Assert.Contains("app-1.0.0-0", result.ErrorMessage);
    }

    [Fact]
    public async Task ApplyPlondsUpdate_WhenInstallCheckpointIsStale_ReturnsStructuredFailure()
    {
        _directory.CreateDeployment("1.0.0", "old-state", isCurrent: true);
        var newState = Encoding.UTF8.GetBytes("new-state");
        _directory.StagePlondsUpdate("1.0.0", "1.1.0", newState, Sha256Hex(newState));
        _directory.WriteStaleInstallCheckpoint("9.9.9", "1.1.0");

        var service = new UpdateEngineService(new DeploymentLocator(_directory.AppRoot));
        var result = await service.ApplyPendingUpdateAsync();

        Assert.False(result.Success);
        Assert.Equal("resume_state_invalid", result.Code);
    }

    [Fact]
    public async Task ApplyLegacyUpdate_WhenInstallCheckpointIsStale_ReturnsStructuredFailure()
    {
        _directory.CreateDeployment("1.0.0", "old-state", isCurrent: true);
        _directory.StageLegacyUpdate("1.0.0", "1.1.0", "new-state");
        _directory.WriteStaleInstallCheckpoint("9.9.9", "1.1.0");

        var service = new UpdateEngineService(new DeploymentLocator(_directory.AppRoot));
        var result = await service.ApplyPendingUpdateAsync();

        Assert.False(result.Success);
        Assert.Equal("resume_state_invalid", result.Code);
    }

    [Fact]
    public async Task ApplyPlondsUpdate_WhenCheckpointIsValid_ResumesAndSucceeds()
    {
        var current = _directory.CreateDeployment("1.0.0", "old-state", isCurrent: true);
        var newState = Encoding.UTF8.GetBytes("new-state");
        _directory.StagePlondsUpdate("1.0.0", "1.1.0", newState, Sha256Hex(newState));
        _directory.WriteValidPlondsResumeCheckpoint("1.0.0", "1.1.0");

        var service = new UpdateEngineService(new DeploymentLocator(_directory.AppRoot));
        var result = await service.ApplyPendingUpdateAsync();

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal("1.1.0", result.TargetVersion);
        Assert.False(File.Exists(Path.Combine(current, ".current")));
        var resumedTarget = Path.Combine(_directory.AppRoot, "app-1.1.0-0");
        Assert.True(File.Exists(Path.Combine(resumedTarget, ".current")));
        Assert.Equal("new-state", File.ReadAllText(Path.Combine(resumedTarget, "state.txt")));
        Assert.False(File.Exists(UpdatePaths.GetInstallCheckpointPath(_directory.AppRoot)));
    }

    [Fact]
    public async Task ApplyLegacyUpdate_WhenCheckpointIsValid_ResumesAndSucceeds()
    {
        var current = _directory.CreateDeployment("1.0.0", "old-state", isCurrent: true);
        _directory.StageLegacyUpdate("1.0.0", "1.1.0", "new-state");
        _directory.WriteValidLegacyResumeCheckpoint("1.0.0", "1.1.0");

        var service = new UpdateEngineService(new DeploymentLocator(_directory.AppRoot));
        var result = await service.ApplyPendingUpdateAsync();

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal("1.1.0", result.TargetVersion);
        Assert.False(File.Exists(Path.Combine(current, ".current")));
        var resumedTarget = Path.Combine(_directory.AppRoot, "app-1.1.0-0");
        Assert.True(File.Exists(Path.Combine(resumedTarget, ".current")));
        Assert.Equal("new-state", File.ReadAllText(Path.Combine(resumedTarget, "state.txt")));
        Assert.False(File.Exists(UpdatePaths.GetInstallCheckpointPath(_directory.AppRoot)));
    }

    public void Dispose() => _directory.Dispose();

    private static string Sha256Hex(byte[] bytes)
    {
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private sealed class UpdateTestDirectory : IDisposable
    {
        private readonly string _root;
        private readonly RSA _rsa = RSA.Create(2048);

        public UpdateTestDirectory()
        {
            _root = Path.Combine(Path.GetTempPath(), "LanMountainDesktop.UpdateRegression", Guid.NewGuid().ToString("N"));
            AppRoot = Path.Combine(_root, "app-root");
            Directory.CreateDirectory(AppRoot);

            var resolver = new DataLocationResolver(AppRoot);
            LauncherRoot = resolver.ResolveLauncherDataPath();
            IncomingRoot = Path.Combine(LauncherRoot, "update", "incoming");
            SnapshotsRoot = Path.Combine(LauncherRoot, "snapshots");

            Directory.CreateDirectory(Path.Combine(LauncherRoot, "update"));
            File.WriteAllText(Path.Combine(LauncherRoot, "update", "public-key.pem"), _rsa.ExportSubjectPublicKeyInfoPem());
        }

        public string AppRoot { get; }

        private string LauncherRoot { get; }

        private string IncomingRoot { get; }

        private string SnapshotsRoot { get; }

        public string CreateDeployment(string version, string state, bool isCurrent)
        {
            var deployment = Path.Combine(AppRoot, $"app-{version}-0");
            Directory.CreateDirectory(deployment);
            File.WriteAllText(Path.Combine(deployment, ExecutableName), $"exe-{version}");
            File.WriteAllText(Path.Combine(deployment, "state.txt"), state);

            if (isCurrent)
            {
                File.WriteAllText(Path.Combine(deployment, ".current"), string.Empty);
            }

            return deployment;
        }

        public void StagePlondsUpdate(string fromVersion, string toVersion, byte[] statePayload, string expectedStateSha256)
        {
            Directory.CreateDirectory(IncomingRoot);
            var objectsRoot = Path.Combine(IncomingRoot, "objects");
            Directory.CreateDirectory(objectsRoot);

            var objectHash = Convert.ToHexString(SHA256.HashData(statePayload)).ToLowerInvariant();
            File.WriteAllBytes(Path.Combine(objectsRoot, objectHash), statePayload);

            var currentExecutable = Path.Combine(AppRoot, $"app-{fromVersion}-0", ExecutableName);
            var fileMap = new PlondsFileMap
            {
                DistributionId = $"stable-{PlondsStaticUpdateService.ResolveCurrentPlatform()}-{toVersion}",
                FromVersion = fromVersion,
                ToVersion = toVersion,
                Platform = PlondsStaticUpdateService.ResolveCurrentPlatform(),
                Files =
                [
                    new PlondsFileEntry
                    {
                        Path = ExecutableName,
                        Action = "reuse",
                        Sha256 = Sha256File(currentExecutable)
                    },
                    new PlondsFileEntry
                    {
                        Path = "state.txt",
                        Action = "replace",
                        Sha256 = expectedStateSha256,
                        ObjectUrl = $"https://static.example/lanmountain/update/repo/sha256/{objectHash[..2]}/{objectHash}"
                    }
                ]
            };

            var fileMapPath = Path.Combine(IncomingRoot, "plonds-filemap.json");
            File.WriteAllText(fileMapPath, JsonSerializer.Serialize(fileMap, AppJsonContext.Default.PlondsFileMap));
            Sign(fileMapPath, Path.Combine(IncomingRoot, "plonds-filemap.sig"));

            var deploymentLock = new DeploymentLock(
                SchemaVersion: 1,
                Kind: "delta",
                TargetVersion: toVersion,
                PayloadPath: fileMapPath,
                PayloadSha256: Sha256File(fileMapPath),
                CreatedAtUtc: DateTimeOffset.UtcNow);
            var deploymentLockPath = UpdatePaths.GetDeploymentLockPath(AppRoot);
            Directory.CreateDirectory(Path.GetDirectoryName(deploymentLockPath)!);
            File.WriteAllText(deploymentLockPath, JsonSerializer.Serialize(deploymentLock));

            var markerPath = UpdatePaths.GetDownloadMarkerPath(AppRoot);
            File.WriteAllText(markerPath, UpdatePaths.GetDownloadMarkerContent(
                manifestSha256: Sha256File(fileMapPath),
                targetVersion: toVersion,
                objectCount: 1));
        }

        public void StageLegacyUpdate(string fromVersion, string toVersion, string newState)
        {
            Directory.CreateDirectory(IncomingRoot);
            var extractRoot = Path.Combine(IncomingRoot, "legacy-src");
            Directory.CreateDirectory(extractRoot);

            File.WriteAllText(Path.Combine(extractRoot, ExecutableName), $"exe-{toVersion}");
            File.WriteAllText(Path.Combine(extractRoot, "state.txt"), newState);

            var archivePath = Path.Combine(IncomingRoot, "update.zip");
            if (File.Exists(archivePath))
            {
                File.Delete(archivePath);
            }

            System.IO.Compression.ZipFile.CreateFromDirectory(extractRoot, archivePath);

            var fileMap = new SignedFileMap
            {
                FromVersion = fromVersion,
                ToVersion = toVersion,
                Files =
                [
                    new LanMountainDesktop.Launcher.Models.UpdateFileEntry
                    {
                        Path = ExecutableName,
                        ArchivePath = ExecutableName,
                        Action = "replace",
                        Sha256 = Sha256File(Path.Combine(extractRoot, ExecutableName))
                    },
                    new LanMountainDesktop.Launcher.Models.UpdateFileEntry
                    {
                        Path = "state.txt",
                        ArchivePath = "state.txt",
                        Action = "replace",
                        Sha256 = Sha256File(Path.Combine(extractRoot, "state.txt"))
                    }
                ]
            };

            var fileMapPath = Path.Combine(IncomingRoot, "files.json");
            File.WriteAllText(fileMapPath, JsonSerializer.Serialize(fileMap, AppJsonContext.Default.SignedFileMap));
            Sign(fileMapPath, Path.Combine(IncomingRoot, "files.json.sig"));

            var deploymentLock = new DeploymentLock(
                SchemaVersion: 1,
                Kind: "delta",
                TargetVersion: toVersion,
                PayloadPath: fileMapPath,
                PayloadSha256: Sha256File(fileMapPath),
                CreatedAtUtc: DateTimeOffset.UtcNow);
            var deploymentLockPath = UpdatePaths.GetDeploymentLockPath(AppRoot);
            Directory.CreateDirectory(Path.GetDirectoryName(deploymentLockPath)!);
            File.WriteAllText(deploymentLockPath, JsonSerializer.Serialize(deploymentLock));

            Directory.Delete(extractRoot, true);
        }

        public void WriteSnapshot(string sourceVersion, string sourceDirectory, string targetVersion, string targetDirectory)
        {
            Directory.CreateDirectory(SnapshotsRoot);
            var snapshot = new SnapshotMetadata
            {
                SnapshotId = Guid.NewGuid().ToString("N"),
                SourceVersion = sourceVersion,
                TargetVersion = targetVersion,
                CreatedAt = DateTimeOffset.UtcNow,
                SourceDirectory = sourceDirectory,
                TargetDirectory = targetDirectory,
                Status = "applied"
            };

            File.WriteAllText(
                Path.Combine(SnapshotsRoot, $"{snapshot.SnapshotId}.json"),
                JsonSerializer.Serialize(snapshot, AppJsonContext.Default.SnapshotMetadata));
        }

        public void WriteStaleInstallCheckpoint(string sourceVersion, string targetVersion)
        {
            var checkpoint = new InstallCheckpoint
            {
                SnapshotId = Guid.NewGuid().ToString("N"),
                SourceVersion = sourceVersion,
                TargetVersion = targetVersion,
                SourceDirectory = Path.Combine(AppRoot, $"app-{sourceVersion}-0"),
                TargetDirectory = Path.Combine(AppRoot, $"app-{targetVersion}-999"),
                IsInitialDeployment = false,
                AppliedCount = 1,
                VerifiedCount = 1
            };

            var checkpointPath = UpdatePaths.GetInstallCheckpointPath(AppRoot);
            Directory.CreateDirectory(Path.GetDirectoryName(checkpointPath)!);
            File.WriteAllText(checkpointPath, JsonSerializer.Serialize(checkpoint, AppJsonContext.Default.InstallCheckpoint));
        }

        public void WriteValidPlondsResumeCheckpoint(string sourceVersion, string targetVersion)
        {
            var targetDeployment = Path.Combine(AppRoot, $"app-{targetVersion}-0");
            Directory.CreateDirectory(targetDeployment);
            File.WriteAllText(Path.Combine(targetDeployment, ".partial"), string.Empty);
            File.WriteAllText(Path.Combine(targetDeployment, ExecutableName), $"exe-{sourceVersion}");

            var checkpoint = new InstallCheckpoint
            {
                SnapshotId = Guid.NewGuid().ToString("N"),
                SourceVersion = sourceVersion,
                TargetVersion = targetVersion,
                SourceDirectory = Path.Combine(AppRoot, $"app-{sourceVersion}-0"),
                TargetDirectory = targetDeployment,
                IsInitialDeployment = false,
                AppliedCount = 1,
                VerifiedCount = 0
            };

            var checkpointPath = UpdatePaths.GetInstallCheckpointPath(AppRoot);
            Directory.CreateDirectory(Path.GetDirectoryName(checkpointPath)!);
            File.WriteAllText(checkpointPath, JsonSerializer.Serialize(checkpoint, AppJsonContext.Default.InstallCheckpoint));
        }

        public void WriteValidLegacyResumeCheckpoint(string sourceVersion, string targetVersion)
        {
            var targetDeployment = Path.Combine(AppRoot, $"app-{targetVersion}-0");
            Directory.CreateDirectory(targetDeployment);
            File.WriteAllText(Path.Combine(targetDeployment, ".partial"), string.Empty);

            var checkpoint = new InstallCheckpoint
            {
                SnapshotId = Guid.NewGuid().ToString("N"),
                SourceVersion = sourceVersion,
                TargetVersion = targetVersion,
                SourceDirectory = Path.Combine(AppRoot, $"app-{sourceVersion}-0"),
                TargetDirectory = targetDeployment,
                IsInitialDeployment = false,
                AppliedCount = 0,
                VerifiedCount = 0
            };

            var checkpointPath = UpdatePaths.GetInstallCheckpointPath(AppRoot);
            Directory.CreateDirectory(Path.GetDirectoryName(checkpointPath)!);
            File.WriteAllText(checkpointPath, JsonSerializer.Serialize(checkpoint, AppJsonContext.Default.InstallCheckpoint));
        }

        public void Dispose()
        {
            _rsa.Dispose();
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }

        private void Sign(string payloadPath, string signaturePath)
        {
            var signature = _rsa.SignData(File.ReadAllBytes(payloadPath), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            File.WriteAllText(signaturePath, Convert.ToBase64String(signature));
        }

        private static string Sha256File(string path)
        {
            using var stream = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        }

        private static string ExecutableName => OperatingSystem.IsWindows()
            ? "LanMountainDesktop.exe"
            : "LanMountainDesktop";
    }
}

public sealed class PlondsStaticUpdateServiceTests
{
    [Fact]
    public async Task CheckForUpdatesAsync_ReadsStaticLatestDistributionAndBuildsPayloadUrls()
    {
        var platform = PlondsStaticUpdateService.ResolveCurrentPlatform();
        var handler = new StaticManifestHandler(request =>
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            if (path.EndsWith($"/meta/channels/stable/{platform}/latest.json", StringComparison.Ordinal))
            {
                return Json("""{"distributionId":"dist-1","version":"1.2.0","channel":"stable","platform":"PLATFORM","publishedAt":"2026-05-06T00:00:00Z"}"""
                    .Replace("PLATFORM", platform));
            }

            if (path.EndsWith("/meta/distributions/dist-1.json", StringComparison.Ordinal))
            {
                return Json("""{"distributionId":"dist-1","version":"1.2.0","sourceVersion":"1.0.0","channel":"stable","platform":"PLATFORM","publishedAt":"2026-05-06T00:00:00Z","fileMapUrl":"https://static.example/lanmountain/update/manifests/dist-1/plonds-filemap.json","fileMapSignatureUrl":"https://static.example/lanmountain/update/manifests/dist-1/plonds-filemap.json.sig"}"""
                    .Replace("PLATFORM", platform));
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        using var client = new HttpClient(handler);
        using var service = new PlondsStaticUpdateService("https://static.example/lanmountain/update", client);

        var result = await service.CheckForUpdatesAsync(new Version(1, 0, 0), includePrerelease: false);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.True(result.IsUpdateAvailable);
        Assert.Equal("1.2.0", result.LatestVersionText);
        Assert.NotNull(result.PlondsPayload);
        Assert.Equal("dist-1", result.PlondsPayload.DistributionId);
        Assert.Equal(platform, result.PlondsPayload.SubChannel);
        Assert.Equal("https://static.example/lanmountain/update/manifests/dist-1/plonds-filemap.json", result.PlondsPayload.FileMapJsonUrl);
        Assert.Equal("https://static.example/lanmountain/update/manifests/dist-1/plonds-filemap.json.sig", result.PlondsPayload.FileMapSignatureUrl);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_WhenLatestIsMissing_ReturnsFailureForFallback()
    {
        using var client = new HttpClient(new StaticManifestHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)));
        using var service = new PlondsStaticUpdateService("https://static.example/lanmountain/update", client);

        var result = await service.CheckForUpdatesAsync(new Version(1, 0, 0), includePrerelease: false);

        Assert.False(result.Success);
        Assert.False(result.IsUpdateAvailable);
        Assert.Contains("latest manifest", result.ErrorMessage);
    }

    [Fact]
    public void ResolveCurrentPlatform_UsesCanonicalNames()
    {
        var platform = PlondsStaticUpdateService.ResolveCurrentPlatform();

        Assert.DoesNotContain("win-", platform, StringComparison.OrdinalIgnoreCase);
        if (OperatingSystem.IsWindows())
        {
            Assert.StartsWith("windows-", platform, StringComparison.Ordinal);
        }
        else if (OperatingSystem.IsLinux())
        {
            Assert.StartsWith("linux-", platform, StringComparison.Ordinal);
        }
    }

    private static HttpResponseMessage Json(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private sealed class StaticManifestHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(responder(request));
        }
    }
}

public sealed class UpdatePathConsistencyTests
{
    [Fact]
    public void HostAndSharedUpdatePathsUseLauncherDirectoryCasing()
    {
        var incoming = UpdatePaths.GetIncomingDirectory("root");
        var sharedIncoming = UpdatePaths.GetIncomingDirectory("root");

        Assert.Contains($"{Path.DirectorySeparatorChar}.Launcher{Path.DirectorySeparatorChar}", incoming);
        Assert.Equal(
            Path.Combine("root", ".Launcher", "update", "incoming"),
            sharedIncoming);
    }
}

public sealed class PlondsApiManifestProviderTests
{
    [Fact]
    public async Task GetLatestAsync_MapsCanonicalAndLegacyFileFields()
    {
        using var client = new HttpClient(new StaticManifestHandler(request =>
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            if (path.EndsWith("/api/plonds/v1/channels/stable/windows-x64/latest", StringComparison.Ordinal))
            {
                return Json("""{"distributionId":"dist-2","version":"1.2.0","publishedAt":"2026-05-06T00:00:00Z"}""");
            }

            if (path.EndsWith("/api/plonds/v1/distributions/dist-2", StringComparison.Ordinal))
            {
                return Json("""
                    {
                      "distributionId": "dist-2",
                      "version": "1.2.0",
                      "sourceVersion": "1.1.0",
                      "publishedAt": "2026-05-06T00:00:00Z",
                      "fileMapUrl": "https://static.example/filemap.json",
                      "signatures": [{ "signature": "https://static.example/filemap.json.sig" }],
                      "components": [
                        {
                          "files": [
                            {
                              "path": "LanMountainDesktop.exe",
                              "action": "replace",
                              "sha256": "abc123",
                              "size": 42,
                              "objectUrl": "https://static.example/repo/sha256/ab/abc123",
                              "archiveSha256": "archive123"
                            },
                            {
                              "path": "legacy.dll",
                              "op": "add",
                              "contentHash": "def456",
                              "size": 7
                            }
                          ]
                        }
                      ]
                    }
                    """);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }));
        var provider = new PlondsApiManifestProvider("https://static.example", client);

        var manifest = await provider.GetLatestAsync("stable", "windows-x64", new Version(1, 1, 0), CancellationToken.None);

        Assert.NotNull(manifest);
        Assert.Equal(UpdatePayloadKind.DeltaPlonds, manifest.Kind);
        Assert.Equal("https://static.example/filemap.json.sig", manifest.FileMapSignatureUrl);
        Assert.Collection(
            manifest.Files,
            first =>
            {
                Assert.Equal("replace", first.Action);
                Assert.Equal("abc123", first.Sha256);
                Assert.Equal("https://static.example/repo/sha256/ab/abc123", first.ObjectUrl);
                Assert.Equal("archive123", first.ArchiveSha256);
            },
            second =>
            {
                Assert.Equal("add", second.Action);
                Assert.Equal("def456", second.Sha256);
            });
    }

    private static HttpResponseMessage Json(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private sealed class StaticManifestHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(responder(request));
        }
    }
}
