using System.Net;
using System.Security.Cryptography;
using System.IO.Compression;
using LanMountainDesktop.Services.Plonds;
using Xunit;

namespace LanMountainDesktop.Tests;

public sealed class PlondsClientServiceTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(
        Path.GetTempPath(),
        "LanMountainDesktop.Tests",
        nameof(PlondsClientServiceTests),
        Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    [Fact]
    public void SourceRegistry_AddRange_DeduplicatesAndAllowsManifestExtensions()
    {
        var registry = new PlondsSourceRegistry(
        [
            new("s3", "s3", "https://s3.test/PLONDS.json", 100),
            new("github", "github", "https://github.test/PLONDS.json", 50)
        ]);

        registry.AddRange(
        [
            new("mirror", "http", "https://mirror.test/PLONDS.json", 10),
            new("s3", "s3", "https://s3-new.test/PLONDS.json", 200),
            new("duplicate-url", "http", "https://mirror.test/PLONDS.json", 1)
        ]);

        Assert.Equal(3, registry.Sources.Count);
        Assert.Contains(registry.Sources, source => source.Id == "s3" && source.ManifestUrl == "https://s3-new.test/PLONDS.json");
        Assert.Contains(registry.Sources, source => source.Id == "mirror");
    }

    [Fact]
    public void ManifestSelector_WhenVersionsDiffer_SelectsHighestVersion()
    {
        var selected = PlondsManifestSelector.SelectHighestVersion(
        [
            new(new("s3", "s3", "https://s3.test/PLONDS.json", 100), CreateManifest("1.2.0")),
            new(new("github", "github", "https://github.test/PLONDS.json", 50), CreateManifest("1.3.0")),
            new(new("mirror", "http", "https://mirror.test/PLONDS.json", 500), CreateManifest("1.1.9"))
        ]);

        Assert.NotNull(selected);
        Assert.Equal("1.3.0", selected.Manifest.CurrentVersion);
        Assert.Equal("github", selected.Source.Id);
    }

    [Fact]
    public async Task DownloadPlanner_WhenDeltaFails_FallsBackToFullPackage()
    {
        var downloader = new FakeDownloader(deltaFails: true, fullFails: false);
        var planner = new PlondsDownloadPlanner(downloader);

        var result = await planner.PrepareAsync(
            new PlondsManifestCandidate(new("s3", "s3", "https://s3.test/PLONDS.json"), CreateManifest("1.2.3")),
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.False(result.RequiresUiHandling);
        Assert.Equal(PlondsPackageMode.Full, result.Package?.Mode);
        Assert.Equal(1, downloader.DeltaCalls);
        Assert.Equal(1, downloader.FullCalls);
    }

    [Fact]
    public async Task DownloadPlanner_WhenDeltaAndFullFail_ReturnsUiFailure()
    {
        var downloader = new FakeDownloader(deltaFails: true, fullFails: true);
        var planner = new PlondsDownloadPlanner(downloader);

        var result = await planner.PrepareAsync(
            new PlondsManifestCandidate(new("s3", "s3", "https://s3.test/PLONDS.json"), CreateManifest("1.2.3")),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.True(result.RequiresUiHandling);
        Assert.Null(result.Package);
        Assert.Contains("full package fallback also failed", result.ErrorMessage);
    }

    [Fact]
    public async Task DownloadPlanner_WhenManifestRequiresCleanInstall_DoesNotPreparePlondsPackage()
    {
        var downloader = new FakeDownloader(deltaFails: false, fullFails: false);
        var planner = new PlondsDownloadPlanner(downloader);

        var result = await planner.PrepareAsync(
            new PlondsManifestCandidate(
                new("s3", "s3", "https://s3.test/PLONDS.json"),
                CreateManifest("1.2.3", requiresCleanInstall: true)),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.True(result.RequiresUiHandling);
        Assert.Contains("clean install", result.ErrorMessage);
        Assert.Equal(0, downloader.DeltaCalls);
        Assert.Equal(0, downloader.FullCalls);
    }

    [Fact]
    public async Task PlondsService_ReadsBuiltInSources_RegistersManifestSources_AndPreparesHighestVersion()
    {
        using var httpClient = new HttpClient(new ManifestHandler(new Dictionary<string, string>
        {
            ["https://s3.test/PLONDS.json"] = ManifestJson("1.2.0", """
                "sources": [
                  { "id": "mirror", "kind": "http", "manifestUrl": "https://mirror.test/PLONDS.json", "priority": 25 }
                ]
                """),
            ["https://github.test/PLONDS.json"] = ManifestJson("1.3.0")
        }));

        var registry = new PlondsSourceRegistry(
        [
            new("s3", "s3", "https://s3.test/PLONDS.json", 100),
            new("github", "github", "https://github.test/PLONDS.json", 50)
        ]);
        var downloader = new FakeDownloader(deltaFails: false, fullFails: false);
        var service = new PlondsService(
            registry,
            new PlondsManifestClient(httpClient),
            new PlondsDownloadPlanner(downloader));

        var result = await service.FindAndPrepareLatestAsync(CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("1.3.0", result.Package?.Version.ToString());
        Assert.Equal(PlondsPackageMode.Delta, result.Package?.Mode);
        Assert.Contains(registry.Sources, source => source.Id == "mirror" && source.ManifestUrl == "https://mirror.test/PLONDS.json");
    }

    [Fact]
    public void ClientServiceFactory_CreatesBuiltInS3AndGitHubSources()
    {
        var sources = PlondsClientServiceFactory.CreateBuiltInSources();

        Assert.Equal(2, sources.Count);
        Assert.Contains(sources, source => source.Id == "s3" && source.Kind == "s3" && source.ManifestUrl.EndsWith("/PLONDS.json", StringComparison.Ordinal));
        Assert.Contains(sources, source => source.Id == "github" && source.Kind == "github" && source.ManifestUrl.EndsWith("/PLONDS.json", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PlondsService_FindLatest_UsesHighestVersionAndPersistsManifestSources()
    {
        using var httpClient = new HttpClient(new ManifestHandler(new Dictionary<string, string>
        {
            ["https://s3.test/PLONDS.json"] = ManifestJson("1.5.0", """
                "sources": [
                  { "id": "mirror", "kind": "http", "manifestUrl": "https://mirror.test/PLONDS.json", "priority": 25 }
                ]
                """),
            ["https://github.test/PLONDS.json"] = ManifestJson("1.4.0")
        }));
        var sourceStorePath = Path.Combine(_tempRoot, "sources.json");
        var sourceStore = new PlondsSourceStore(sourceStorePath);
        var registry = new PlondsSourceRegistry(
        [
            new("s3", "s3", "https://s3.test/PLONDS.json", 100),
            new("github", "github", "https://github.test/PLONDS.json", 50)
        ]);
        var service = new PlondsService(
            registry,
            new PlondsManifestClient(httpClient),
            new PlondsDownloadPlanner(new FakeDownloader(deltaFails: false, fullFails: false)),
            sourceStore);

        var result = await service.FindLatestAsync(new Version(1, 4, 0), CancellationToken.None);
        var storedSources = await sourceStore.LoadAsync(CancellationToken.None);

        Assert.True(result.Success);
        Assert.True(result.IsUpdateAvailable);
        Assert.Equal("1.5.0", result.LatestVersion?.ToString());
        Assert.Contains(storedSources, source => source.Id == "mirror" && source.ManifestUrl == "https://mirror.test/PLONDS.json");
    }

    [Fact]
    public async Task PlondsService_WhenHighestVersionSourcePackageFails_TriesSameVersionOtherSource()
    {
        using var httpClient = new HttpClient(new ManifestHandler(new Dictionary<string, string>
        {
            ["https://s3.test/PLONDS.json"] = ManifestJson("1.6.0"),
            ["https://github.test/PLONDS.json"] = ManifestJson("1.6.0")
        }));
        var registry = new PlondsSourceRegistry(
        [
            new("s3", "s3", "https://s3.test/PLONDS.json", 100),
            new("github", "github", "https://github.test/PLONDS.json", 50)
        ]);
        var downloader = new SourceAwareFakeDownloader(failingSourceId: "s3");
        var service = new PlondsService(
            registry,
            new PlondsManifestClient(httpClient),
            new PlondsDownloadPlanner(downloader));

        var result = await service.FindAndPrepareLatestAsync(new Version(1, 5, 0), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("github", downloader.SuccessfulSourceId);
        Assert.Equal(2, downloader.DeltaCalls);
    }

    [Fact]
    public async Task PlondsService_WhenManifestSourceThrows_ContinuesWithOtherSources()
    {
        using var httpClient = new HttpClient(new ManifestHandler(
            new Dictionary<string, string>
            {
                ["https://github.test/PLONDS.json"] = ManifestJson("1.7.0")
            },
            throwingUrls: new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "https://s3.test/PLONDS.json"
            }));
        var registry = new PlondsSourceRegistry(
        [
            new("s3", "s3", "https://s3.test/PLONDS.json", 100),
            new("github", "github", "https://github.test/PLONDS.json", 50)
        ]);
        var service = new PlondsService(
            registry,
            new PlondsManifestClient(httpClient),
            new PlondsDownloadPlanner(new FakeDownloader(deltaFails: false, fullFails: false)));

        var result = await service.FindLatestAsync(new Version(1, 6, 0), CancellationToken.None);

        Assert.True(result.Success);
        Assert.True(result.IsUpdateAvailable);
        Assert.Equal("1.7.0", result.LatestVersion?.ToString());
        Assert.Equal("github", Assert.Single(result.Candidates).Source.Id);
    }

    [Fact]
    public async Task HttpDownloader_DownloadsVerifiesAndExtractsDeltaPackage()
    {
        var changedZip = CreateZip(("app.dll", "delta payload"));
        var filesZip = CreateZip(("app.dll", "full payload"));
        var manifest = CreateManifest(
            "1.4.0",
            downloads: CreateDownloads(
                changedUrl: "https://s3.test/1.4.0/changed.zip",
                filesUrl: "https://s3.test/1.4.0/Files.zip"),
            checksums: new Dictionary<string, string>
            {
                ["changed.zip"] = Md5Checksum(changedZip),
                ["Files.zip"] = Md5Checksum(filesZip)
            });

        using var httpClient = new HttpClient(new AssetHandler(new Dictionary<string, byte[]>
        {
            ["https://s3.test/1.4.0/changed.zip"] = changedZip,
            ["https://s3.test/1.4.0/Files.zip"] = filesZip
        }));
        var downloader = CreateHttpDownloader(httpClient);

        var package = await downloader.PrepareDeltaAsync(
            manifest,
            new("s3", "s3", "https://s3.test/1.4.0/PLONDS.json", 100),
            CancellationToken.None);

        Assert.Equal(PlondsPackageMode.Delta, package.Mode);
        Assert.True(File.Exists(package.ManifestPath));
        Assert.True(File.Exists(package.ChangedZipPath));
        Assert.Equal("delta payload", File.ReadAllText(Path.Combine(package.ChangedDirectory!, "app.dll")));
    }

    [Fact]
    public async Task DownloadPlanner_WhenDeltaChecksumFails_PreparesFullPackage()
    {
        var changedZip = CreateZip(("app.dll", "delta payload"));
        var filesZip = CreateZip(("app.dll", "full payload"));
        var manifest = CreateManifest(
            "1.4.1",
            downloads: CreateDownloads(
                changedUrl: "https://s3.test/1.4.1/changed.zip",
                filesUrl: "https://s3.test/1.4.1/Files.zip"),
            checksums: new Dictionary<string, string>
            {
                ["changed.zip"] = "md5:00000000000000000000000000000000",
                ["Files.zip"] = Md5Checksum(filesZip)
            });

        using var httpClient = new HttpClient(new AssetHandler(new Dictionary<string, byte[]>
        {
            ["https://s3.test/1.4.1/changed.zip"] = changedZip,
            ["https://s3.test/1.4.1/Files.zip"] = filesZip
        }));
        var planner = new PlondsDownloadPlanner(CreateHttpDownloader(httpClient));

        var result = await planner.PrepareAsync(
            new PlondsManifestCandidate(new("s3", "s3", "https://s3.test/1.4.1/PLONDS.json", 100), manifest),
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(PlondsPackageMode.Full, result.Package?.Mode);
        Assert.Equal("full payload", File.ReadAllText(Path.Combine(result.Package!.FilesDirectory!, "app.dll")));
    }

    [Fact]
    public async Task DownloadPlanner_WhenDeltaUrlMissing_PreparesFullPackage()
    {
        var filesZip = CreateZip(("app.dll", "full payload"));
        var manifest = CreateManifest(
            "1.4.2",
            downloads: CreateDownloads(
                changedUrl: null,
                filesUrl: "https://s3.test/1.4.2/Files.zip"),
            checksums: new Dictionary<string, string>
            {
                ["Files.zip"] = Md5Checksum(filesZip)
            });

        using var httpClient = new HttpClient(new AssetHandler(new Dictionary<string, byte[]>
        {
            ["https://s3.test/1.4.2/Files.zip"] = filesZip
        }));
        var planner = new PlondsDownloadPlanner(CreateHttpDownloader(httpClient));

        var result = await planner.PrepareAsync(
            new PlondsManifestCandidate(new("s3", "s3", "https://s3.test/1.4.2/PLONDS.json", 100), manifest),
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(PlondsPackageMode.Full, result.Package?.Mode);
    }

    [Fact]
    public async Task DownloadPlanner_WhenFullChecksumFails_ReturnsUiFailure()
    {
        var changedZip = CreateZip(("app.dll", "delta payload"));
        var filesZip = CreateZip(("app.dll", "full payload"));
        var manifest = CreateManifest(
            "1.4.3",
            downloads: CreateDownloads(
                changedUrl: "https://s3.test/1.4.3/changed.zip",
                filesUrl: "https://s3.test/1.4.3/Files.zip"),
            checksums: new Dictionary<string, string>
            {
                ["changed.zip"] = "md5:00000000000000000000000000000000",
                ["Files.zip"] = "md5:11111111111111111111111111111111"
            });

        using var httpClient = new HttpClient(new AssetHandler(new Dictionary<string, byte[]>
        {
            ["https://s3.test/1.4.3/changed.zip"] = changedZip,
            ["https://s3.test/1.4.3/Files.zip"] = filesZip
        }));
        var planner = new PlondsDownloadPlanner(CreateHttpDownloader(httpClient));

        var result = await planner.PrepareAsync(
            new PlondsManifestCandidate(new("s3", "s3", "https://s3.test/1.4.3/PLONDS.json", 100), manifest),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.True(result.RequiresUiHandling);
        Assert.Contains("full package fallback also failed", result.ErrorMessage);
    }

    [Fact]
    public async Task PreparedPackageInstaller_AppliesDeltaPackageWithoutUpdateDownloadSystem()
    {
        var launcherRoot = Path.Combine(_tempRoot, "launcher");
        var currentDeployment = Path.Combine(launcherRoot, "app-1.0.0-0");
        Directory.CreateDirectory(currentDeployment);
        File.WriteAllText(Path.Combine(currentDeployment, ".current"), string.Empty);
        File.WriteAllText(Path.Combine(currentDeployment, "LanMountainDesktop.exe"), "exe");
        File.WriteAllText(Path.Combine(currentDeployment, "app.dll"), "old");
        File.WriteAllText(Path.Combine(currentDeployment, "keep.txt"), "keep");
        File.WriteAllText(Path.Combine(currentDeployment, "delete.txt"), "delete");

        var changedDirectory = Path.Combine(_tempRoot, "changed");
        Directory.CreateDirectory(changedDirectory);
        File.WriteAllText(Path.Combine(changedDirectory, "app.dll"), "new");
        var manifestPath = Path.Combine(_tempRoot, "PLONDS.json");
        await File.WriteAllTextAsync(manifestPath, $$"""
            {
              "formatVersion": "2.0",
              "currentVersion": "1.1.0",
              "previousVersion": "1.0.0",
              "isFullUpdate": false,
              "requiresCleanInstall": false,
              "channel": "stable",
              "platform": "windows-x64",
              "updatedAt": "2026-06-01T00:00:00Z",
              "filesMap": {
                "LanMountainDesktop.exe": { "action": "reuse", "hash": "{{Sha256Text("exe")}}", "size": 3 },
                "app.dll": { "action": "replace", "hash": "{{Sha256Text("new")}}", "size": 3 },
                "keep.txt": { "action": "reuse", "hash": "{{Sha256Text("keep")}}", "size": 4 },
                "delete.txt": { "action": "delete", "hash": "", "size": 0 }
              },
              "changedFilesMap": {
                "app.dll": { "archivePath": "app.dll", "hash": "{{Sha256Text("new")}}", "size": 3 }
              },
              "checksums": {}
            }
            """);
        var package = new PlondsPreparedPackage(
            new Version(1, 1, 0),
            PlondsPackageMode.Delta,
            manifestPath,
            Path.Combine(_tempRoot, "changed.zip"),
            changedDirectory,
            null,
            null);

        var result = await new PlondsPreparedPackageInstaller().InstallAsync(
            package,
            launcherRoot,
            progress: null,
            CancellationToken.None);

        Assert.True(result.Success);
        var target = Assert.Single(Directory.GetDirectories(launcherRoot, "app-1.1.0-*"));
        Assert.Equal("new", File.ReadAllText(Path.Combine(target, "app.dll")));
        Assert.Equal("keep", File.ReadAllText(Path.Combine(target, "keep.txt")));
        Assert.False(File.Exists(Path.Combine(target, "delete.txt")));
        Assert.True(File.Exists(Path.Combine(target, ".current")));
        Assert.True(File.Exists(Path.Combine(currentDeployment, ".destroy")));
    }

    [Fact]
    public async Task PreparedPackageInstaller_InstallsFullPackageFromCompleteRootLayout()
    {
        var launcherRoot = Path.Combine(_tempRoot, "launcher-full");
        var currentDeployment = Path.Combine(launcherRoot, "app-1.0.0-0");
        Directory.CreateDirectory(currentDeployment);
        File.WriteAllText(Path.Combine(currentDeployment, ".current"), string.Empty);
        File.WriteAllText(Path.Combine(currentDeployment, "LanMountainDesktop.exe"), "old-exe");
        File.WriteAllText(Path.Combine(currentDeployment, "app.dll"), "old");

        var filesRoot = Path.Combine(_tempRoot, "Files-root");
        var fullAppDirectory = Path.Combine(filesRoot, "app-1.2.0");
        Directory.CreateDirectory(fullAppDirectory);
        File.WriteAllText(Path.Combine(filesRoot, "LanMountainDesktop.Launcher.exe"), "launcher");
        File.WriteAllText(Path.Combine(filesRoot, "LanMountainDesktop.AirAppRuntime.exe"), "runtime");
        File.WriteAllText(Path.Combine(fullAppDirectory, ".current"), string.Empty);
        File.WriteAllText(Path.Combine(fullAppDirectory, "LanMountainDesktop.exe"), "new-exe");
        File.WriteAllText(Path.Combine(fullAppDirectory, "app.dll"), "new");

        var package = new PlondsPreparedPackage(
            new Version(1, 2, 0),
            PlondsPackageMode.Full,
            Path.Combine(_tempRoot, "PLONDS.json"),
            null,
            null,
            Path.Combine(_tempRoot, "Files.zip"),
            filesRoot);

        var result = await new PlondsPreparedPackageInstaller().InstallAsync(
            package,
            launcherRoot,
            progress: null,
            CancellationToken.None);

        Assert.True(result.Success);
        var target = Assert.Single(Directory.GetDirectories(launcherRoot, "app-1.2.0-*"));
        Assert.Equal("new-exe", File.ReadAllText(Path.Combine(target, "LanMountainDesktop.exe")));
        Assert.Equal("new", File.ReadAllText(Path.Combine(target, "app.dll")));
        Assert.False(File.Exists(Path.Combine(target, "LanMountainDesktop.Launcher.exe")));
        Assert.True(File.Exists(Path.Combine(target, ".current")));
        Assert.True(File.Exists(Path.Combine(currentDeployment, ".destroy")));
    }

    private static PlondsClientManifest CreateManifest(
        string version,
        IReadOnlyList<PlondsSourceDescriptor>? sources = null,
        PlondsClientDownloads? downloads = null,
        IReadOnlyDictionary<string, string>? checksums = null,
        bool requiresCleanInstall = false)
    {
        return new PlondsClientManifest(
            FormatVersion: "2.0",
            CurrentVersion: version,
            PreviousVersion: "1.0.0",
            IsFullUpdate: false,
            RequiresCleanInstall: requiresCleanInstall,
            Channel: "stable",
            Platform: "windows-x64",
            UpdatedAt: DateTimeOffset.Parse("2026-06-01T00:00:00Z"),
            FilesMap: new Dictionary<string, PlondsClientFileEntry>(),
            ChangedFilesMap: new Dictionary<string, PlondsClientChangedFileEntry>(),
            Checksums: checksums ?? new Dictionary<string, string>(),
            Downloads: downloads,
            Sources: sources ?? []);
    }

    private PlondsHttpPackageDownloader CreateHttpDownloader(HttpClient httpClient)
    {
        return new PlondsHttpPackageDownloader(
            httpClient,
            new PlondsPackageStore(_tempRoot),
            new PlondsVerifier());
    }

    private static PlondsClientDownloads CreateDownloads(string? changedUrl, string? filesUrl)
    {
        return new PlondsClientDownloads(
            ReleaseTag: "v1.4.0",
            GitHub: null,
            S3: new PlondsS3Downloads(
                Bucket: "bucket",
                Prefix: "lanmountain/update/plonds/1.4.0",
                ManifestKey: "lanmountain/update/plonds/1.4.0/PLONDS.json",
                ManifestUrl: "https://s3.test/1.4.0/PLONDS.json",
                ChangedZipKey: changedUrl is null ? null : "lanmountain/update/plonds/1.4.0/changed.zip",
                ChangedZipUrl: changedUrl,
                ChangedFolderKey: null,
                ChangedFolderUrl: null,
                FilesZipKey: filesUrl is null ? null : "lanmountain/update/plonds/1.4.0/Files.zip",
                FilesZipUrl: filesUrl,
                FilesFolderKey: null,
                FilesFolderUrl: null));
    }

    private static byte[] CreateZip(params (string Path, string Contents)[] entries)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, contents) in entries)
            {
                var entry = archive.CreateEntry(path);
                using var writer = new StreamWriter(entry.Open());
                writer.Write(contents);
            }
        }

        return stream.ToArray();
    }

    private static string Md5Checksum(byte[] bytes)
    {
        return $"md5:{Convert.ToHexString(MD5.HashData(bytes)).ToLowerInvariant()}";
    }

    private static string Sha256Text(string text)
    {
        return Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    }

    private static string ManifestJson(string version, string extraFields = "")
    {
        var separator = string.IsNullOrWhiteSpace(extraFields) ? string.Empty : ",";
        return $$"""
            {
              "formatVersion": "2.0",
              "currentVersion": "{{version}}",
              "previousVersion": "1.0.0",
              "isFullUpdate": false,
              "requiresCleanInstall": false,
              "channel": "stable",
              "platform": "windows-x64",
              "updatedAt": "2026-06-01T00:00:00Z",
              "filesMap": {},
              "changedFilesMap": {},
              "checksums": {}{{separator}}
              {{extraFields}}
            }
            """;
    }

    private sealed class FakeDownloader(bool deltaFails, bool fullFails) : IPlondsPackageDownloader
    {
        public int DeltaCalls { get; private set; }
        public int FullCalls { get; private set; }

        public Task<PlondsPreparedPackage> PrepareDeltaAsync(
            PlondsClientManifest manifest,
            PlondsSourceDescriptor source,
            CancellationToken cancellationToken)
        {
            DeltaCalls++;
            if (deltaFails)
            {
                throw new InvalidOperationException("delta failed");
            }

            return Task.FromResult(CreatePackage(manifest, PlondsPackageMode.Delta));
        }

        public Task<PlondsPreparedPackage> PrepareFullAsync(
            PlondsClientManifest manifest,
            PlondsSourceDescriptor source,
            CancellationToken cancellationToken)
        {
            FullCalls++;
            if (fullFails)
            {
                throw new InvalidOperationException("full failed");
            }

            return Task.FromResult(CreatePackage(manifest, PlondsPackageMode.Full));
        }

        private static PlondsPreparedPackage CreatePackage(PlondsClientManifest manifest, PlondsPackageMode mode)
        {
            PlondsManifestSelector.TryParseVersion(manifest.CurrentVersion, out var version);
            return new PlondsPreparedPackage(
                version,
                mode,
                "PLONDS.json",
                mode is PlondsPackageMode.Delta ? "changed.zip" : null,
                mode is PlondsPackageMode.Delta ? "changed" : null,
                mode is PlondsPackageMode.Full ? "Files.zip" : null,
                mode is PlondsPackageMode.Full ? "Files" : null);
        }
    }

    private sealed class SourceAwareFakeDownloader(string failingSourceId) : IPlondsPackageDownloader
    {
        public int DeltaCalls { get; private set; }
        public string? SuccessfulSourceId { get; private set; }

        public Task<PlondsPreparedPackage> PrepareDeltaAsync(
            PlondsClientManifest manifest,
            PlondsSourceDescriptor source,
            CancellationToken cancellationToken)
        {
            DeltaCalls++;
            if (string.Equals(source.Id, failingSourceId, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("source failed");
            }

            SuccessfulSourceId = source.Id;
            return Task.FromResult(CreatePackage(manifest, source, PlondsPackageMode.Delta));
        }

        public Task<PlondsPreparedPackage> PrepareFullAsync(
            PlondsClientManifest manifest,
            PlondsSourceDescriptor source,
            CancellationToken cancellationToken)
        {
            if (string.Equals(source.Id, failingSourceId, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("source full failed");
            }

            SuccessfulSourceId = source.Id;
            return Task.FromResult(CreatePackage(manifest, source, PlondsPackageMode.Full));
        }

        private static PlondsPreparedPackage CreatePackage(
            PlondsClientManifest manifest,
            PlondsSourceDescriptor source,
            PlondsPackageMode mode)
        {
            PlondsManifestSelector.TryParseVersion(manifest.CurrentVersion, out var version);
            return new PlondsPreparedPackage(
                version,
                mode,
                $"{source.Id}/PLONDS.json",
                mode is PlondsPackageMode.Delta ? $"{source.Id}/changed.zip" : null,
                mode is PlondsPackageMode.Delta ? $"{source.Id}/changed" : null,
                mode is PlondsPackageMode.Full ? $"{source.Id}/Files.zip" : null,
                mode is PlondsPackageMode.Full ? $"{source.Id}/Files" : null);
        }
    }

    private sealed class ManifestHandler(
        IReadOnlyDictionary<string, string> manifests,
        IReadOnlySet<string>? throwingUrls = null) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri?.ToString() ?? string.Empty;
            if (throwingUrls?.Contains(url) == true)
            {
                throw new HttpRequestException("manifest source failed");
            }

            if (!manifests.TryGetValue(url, out var json))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json)
            });
        }
    }

    private sealed class AssetHandler(IReadOnlyDictionary<string, byte[]> assets) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri?.ToString() ?? string.Empty;
            if (!assets.TryGetValue(url, out var bytes))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(bytes)
            });
        }
    }
}
