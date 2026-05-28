using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LanMountainDesktop.Launcher;
using LanMountainDesktop.Launcher.Models;
using Xunit;

namespace LanMountainDesktop.Tests;

public sealed class PendingUpdateDetectorTests : IDisposable
{
    private readonly TempLauncherRoot _root = new();

    [Fact]
    public void ValidateIncomingState_WhenNoPayloadButDeploymentLockExists_ReturnsNoop()
    {
        _root.WriteDeploymentLock();
        var detector = new PendingUpdateDetector(
            new DeploymentLocator(_root.AppRoot),
            _root.Paths,
            new UpdateSignatureVerifier(_root.Paths));

        var result = detector.ValidateIncomingState();

        Assert.True(result.Success);
        Assert.Equal("noop", result.Code);
    }

    public void Dispose() => _root.Dispose();
}

public sealed class UpdateSignatureVerifierTests : IDisposable
{
    private readonly TempLauncherRoot _root = new();

    [Fact]
    public void Verify_WhenSignatureIsMissing_ReturnsStructuredFailure()
    {
        var payload = Path.Combine(_root.Paths.IncomingRoot, "files.json");
        Directory.CreateDirectory(_root.Paths.IncomingRoot);
        File.WriteAllText(payload, "{}");

        var result = new UpdateSignatureVerifier(_root.Paths)
            .Verify(payload, Path.Combine(_root.Paths.IncomingRoot, "files.json.sig"), "files.json.sig");

        Assert.False(result.Success);
        Assert.Equal("Missing files.json.sig.", result.Message);
    }

    public void Dispose() => _root.Dispose();
}

public sealed class IncomingArtifactsCleanerTests : IDisposable
{
    private readonly TempLauncherRoot _root = new();

    [Fact]
    public void Cleanup_RemovesLegacyPlondsAndCheckpointArtifacts()
    {
        Directory.CreateDirectory(_root.Paths.PlondsObjectsRoot);
        foreach (var path in new[]
                 {
                     _root.Paths.FileMapPath,
                     _root.Paths.SignaturePath,
                     _root.Paths.ArchivePath,
                     _root.Paths.PlondsFileMapPath,
                     _root.Paths.PlondsSignaturePath,
                     _root.Paths.PlondsUpdateMetadataPath,
                     _root.Paths.InstallCheckpointPath,
                     Path.Combine(_root.Paths.PlondsObjectsRoot, "payload")
                 })
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "x");
        }

        new IncomingArtifactsCleaner(_root.Paths).Cleanup();

        Assert.False(File.Exists(_root.Paths.FileMapPath));
        Assert.False(File.Exists(_root.Paths.InstallCheckpointPath));
        Assert.False(Directory.Exists(_root.Paths.PlondsObjectsRoot));
    }

    public void Dispose() => _root.Dispose();
}

public sealed class DeploymentActivatorTests : IDisposable
{
    private readonly TempLauncherRoot _root = new();

    [Fact]
    public void Activate_MovesCurrentMarkerAndMarksPreviousDestroy()
    {
        var from = Path.Combine(_root.AppRoot, "app-1");
        var to = Path.Combine(_root.AppRoot, "app-2");
        Directory.CreateDirectory(from);
        Directory.CreateDirectory(to);
        File.WriteAllText(Path.Combine(from, ".current"), string.Empty);
        File.WriteAllText(Path.Combine(to, ".partial"), string.Empty);

        new DeploymentActivator(new DeploymentLocator(_root.AppRoot)).Activate(from, to);

        Assert.False(File.Exists(Path.Combine(from, ".current")));
        Assert.True(File.Exists(Path.Combine(from, ".destroy")));
        Assert.True(File.Exists(Path.Combine(to, ".current")));
        Assert.False(File.Exists(Path.Combine(to, ".partial")));
    }

    public void Dispose() => _root.Dispose();
}

public sealed class RollbackStrategyTests : IDisposable
{
    private readonly TempLauncherRoot _root = new();

    [Fact]
    public void RollbackLatest_WhenNoSnapshotsExist_ReturnsNoSnapshot()
    {
        var snapshotStore = new UpdateSnapshotStore(_root.Paths);
        var activator = new DeploymentActivator(new DeploymentLocator(_root.AppRoot));

        var result = new RollbackStrategy(new DeploymentLocator(_root.AppRoot), snapshotStore, activator)
            .RollbackLatest();

        Assert.False(result.Success);
        Assert.Equal("no_snapshot", result.Code);
    }

    public void Dispose() => _root.Dispose();
}

public sealed class PlondsUpdateApplierTests
{
    [Fact]
    public void ManifestParser_ReadsObjectComponentFiles()
    {
        var map = new PlondsFileMap();
        var entries = PlondsManifestParser.CollectFileEntries(map);

        PlondsManifestParser.PopulateFromRawJson(
            """
            {
              "toVersion": "2.0.0",
              "components": {
                "desktop": {
                  "files": {
                    "LanMountainDesktop.exe": {
                      "archiveSha512": "abcd",
                      "archivePath": "objects/ab/cd"
                    }
                  }
                }
              }
            }
            """,
            map,
            entries);

        Assert.Equal("2.0.0", PlondsManifestParser.ResolveTargetVersion(map, null));
        var entry = Assert.Single(entries);
        Assert.Equal("LanMountainDesktop.exe", entry.Path);
        Assert.Equal("desktop", entry.Metadata["component"]);
    }
}

public sealed class LegacyUpdateApplierTests : IDisposable
{
    private readonly TempLauncherRoot _root = new();

    [Fact]
    public async Task ApplyAsync_WhenSignatureIsMissing_ReturnsSignatureFailure()
    {
        _root.WriteDeploymentLock();
        Directory.CreateDirectory(_root.Paths.IncomingRoot);
        File.WriteAllText(_root.Paths.FileMapPath, JsonSerializer.Serialize(new SignedFileMap
        {
            FromVersion = "1.0.0",
            ToVersion = "2.0.0",
            Files = [new UpdateFileEntry { Path = "state.txt" }]
        }, AppJsonContext.Default.SignedFileMap));
        using (var archive = ZipFile.Open(_root.Paths.ArchivePath, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("state.txt");
            await using var stream = entry.Open();
            await stream.WriteAsync(Encoding.UTF8.GetBytes("state"));
        }

        var applier = CreateLegacyApplier();
        var result = await applier.ApplyAsync();

        Assert.False(result.Success);
        Assert.Equal("signature_failed", result.Code);
    }

    public void Dispose() => _root.Dispose();

    private LegacyUpdateApplier CreateLegacyApplier()
    {
        var locator = new DeploymentLocator(_root.AppRoot);
        var snapshotStore = new UpdateSnapshotStore(_root.Paths);
        var checkpointStore = new InstallCheckpointStore(_root.Paths);
        var activator = new DeploymentActivator(locator);
        var cleaner = new IncomingArtifactsCleaner(_root.Paths);
        return new LegacyUpdateApplier(
            locator,
            _root.Paths,
            new UpdateSignatureVerifier(_root.Paths),
            new NullUpdateProgressReporter(),
            snapshotStore,
            checkpointStore,
            activator,
            cleaner);
    }
}

internal sealed class TempLauncherRoot : IDisposable
{
    public TempLauncherRoot()
    {
        AppRoot = Path.Combine(Path.GetTempPath(), "lmd-launcher-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(AppRoot);
        Paths = new UpdateEnginePaths(AppRoot);
        Directory.CreateDirectory(Paths.IncomingRoot);
    }

    public string AppRoot { get; }

    public UpdateEnginePaths Paths { get; }

    public void WriteDeploymentLock()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Paths.DeploymentLockPath)!);
        File.WriteAllText(Paths.DeploymentLockPath, string.Empty);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(AppRoot))
            {
                Directory.Delete(AppRoot, true);
            }
        }
        catch
        {
        }
    }
}
