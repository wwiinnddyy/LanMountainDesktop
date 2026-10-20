using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LanDesktopPLONDS.Installer.Services;
using LanMountainDesktop.AirAppSdk;
using LanMountainDesktop.Models;
using LanMountainDesktop.Services;
using LanMountainDesktop.Services.Plonds;
using LanMountainDesktop.Services.Settings;
using LanMountainDesktop.Services.Update;
using LanMountainDesktop.Shared.Contracts.Deployment;
using LanMountainDesktop.Shared.Contracts.Privacy;
using LanMountainDesktop.Shared.Contracts.Update;
using Xunit;

namespace LanMountainDesktop.Tests;

public sealed class UpdateChainRegressionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "LanDesktop.UpdateChainTests", Guid.NewGuid().ToString("N"));
    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OnlineIncremental_DeploysNestedPackageAndRootPayload(bool cleanInstall)
    {
        var root = CreateInstalledRoot();
        var package = CreateOnlinePackage(cleanInstall, badHash: false);
        using var http = new HttpClient(new PackageHandler(package.Manifest, File.ReadAllBytes(package.ZipPath)));
        var service = new OnlineInstallService(
            new InstallerPlondsClient(http, Path.Combine(_root, "staging"), _ => TimeSpan.Zero),
            new FilesPackageInstaller((_, _) => { }), new PrivacyFixture(), new InstalledProductInspector(), new IncrementalPlanBuilder());
        await service.CheckLatestAsync(CancellationToken.None);
        await service.UpdateIncrementalAsync(root, null, CancellationToken.None);
        var current = Directory.GetDirectories(root, "app-*").Single(path => File.Exists(Path.Combine(path, ".current")));
        Assert.Equal("new-host", File.ReadAllText(Path.Combine(current, DeploymentLayout.GetHostExecutableName())));
        Assert.Equal("new-launcher", File.ReadAllText(Path.Combine(root, DeploymentLayout.GetLauncherExecutableName())));
        Assert.Equal("new-runtime", File.ReadAllText(Path.Combine(root, "runtime.bin")));
        Assert.False(File.Exists(Path.Combine(root, "app-1.0.0", ".current")));
        Assert.NotEmpty(Directory.GetFiles(UpdatePaths.GetSnapshotsDirectory(root), "*.json"));
    }

    [Fact]
    public async Task WrongFileHash_DoesNotReplaceRootOrActivateDeployment()
    {
        var root = CreateInstalledRoot();
        var package = CreateOnlinePackage(false, badHash: true);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new FilesPackageInstaller((_, _) => { }).InstallAsync(package, root, null, CancellationToken.None));
        Assert.Equal("old-launcher", File.ReadAllText(Path.Combine(root, DeploymentLayout.GetLauncherExecutableName())));
        Assert.Equal("old-runtime", File.ReadAllText(Path.Combine(root, "runtime.bin")));
        Assert.True(File.Exists(Path.Combine(root, "app-1.0.0", ".current")));
        Assert.Empty(Directory.GetDirectories(root, "app-2.0.0-*"));
    }

    [Fact]
    public async Task RootCopyFailure_RestoresPreviouslyReplacedRootFiles()
    {
        var root = CreateInstalledRoot();
        var package = CreateOnlinePackage(true, false);
        using var locked = File.Open(Path.Combine(root, "runtime.bin"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new FilesPackageInstaller((_, _) => { }).InstallAsync(package, root, null, CancellationToken.None));
        Assert.Equal("old-launcher", File.ReadAllText(Path.Combine(root, DeploymentLayout.GetLauncherExecutableName())));
        Assert.True(File.Exists(Path.Combine(root, "app-1.0.0", ".current")));
    }

    [Fact]
    public void Selector_FiltersChannelAndArchitectureBeforeComparingVersions()
    {
        var source = new PlondsSourceDescriptor("fixture", "s3", "https://fixture.test/PLONDS.json");
        var selected = PlondsManifestSelector.SelectHighestVersionCandidates([
            new(source, Manifest("2.0.0", channel: "stable", platform: "windows-x64")),
            new(source, Manifest("3.0.0", channel: "preview", platform: "windows-x64")),
            new(source, Manifest("4.0.0", channel: "stable", platform: "windows-x86"))
        ], "windows-x64", "stable");
        Assert.Equal("2.0.0", Assert.Single(selected).Manifest.CurrentVersion);
        var preview = PlondsManifestSelector.SelectHighestVersionCandidates([
            new(source, Manifest("2.0.0", channel: "stable", platform: "windows-x64")),
            new(source, Manifest("3.0.0", channel: "preview", platform: "windows-x64"))
        ], "windows-x64", "preview");
        Assert.Equal("3.0.0", Assert.Single(preview).Manifest.CurrentVersion);
    }

    [Fact]
    public async Task BaselineMismatch_ChoosesFullBeforeDownloadingDelta()
    {
        var downloader = new DownloaderFixture();
        var manifest = Manifest("2.0.0", "1.5.0");
        var result = await new PlondsDownloadPlanner(downloader).PrepareAsync(
            new(new("fixture", "s3", "https://fixture.test/PLONDS.json"), manifest),
            CancellationToken.None, currentVersion: new Version(1, 0, 0));
        Assert.True(result.Success);
        Assert.Equal(0, downloader.DeltaCalls);
        Assert.Equal(1, downloader.FullCalls);
    }

    [Fact]
    public async Task Cancel_StopsOperationAndPreventsLateResultFromChangingState()
    {
        var settings = new SettingsFixture();
        var plonds = new WaitingPlondsFixture();
        using var service = new UpdateSettingsService(settings, () => throw new InvalidOperationException(), plonds);
        await service.CheckAsync();
        var download = service.DownloadAsync();
        await plonds.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await service.CancelAsync();
        Assert.False((await download).Success);
        Assert.True(plonds.Token.IsCancellationRequested);
        plonds.Completion.SetResult(PlondsPrepareResult.Prepared(new(new Version(2, 0, 0), PlondsPackageMode.Full, "fixture", null, null, null, null)));
        Assert.Equal(UpdatePhase.Idle, service.CurrentPhase);
        Assert.Null(settings.Snapshot.PendingUpdateInstallerPath);
    }

    [Fact]
    public async Task PackageStore_RetainsPartialDataForTheSameManifest()
    {
        var store = new PlondsPackageStore(Path.Combine(_root, "packages"));
        var manifest = Manifest();
        var source = new PlondsSourceDescriptor("fixture", "s3", "https://fixture.test/PLONDS.json");
        var first = await store.CreateStagingAsync(manifest, source, PlondsPackageMode.Full, CancellationToken.None);
        File.WriteAllText(first.PackageZipPath + ".partial", "partial-content");
        var second = await store.CreateStagingAsync(manifest, source, PlondsPackageMode.Full, CancellationToken.None);
        Assert.Equal("partial-content", File.ReadAllText(second.PackageZipPath + ".partial"));
    }

    [Fact]
    public void LegacyInstallerWorker_PreservesTargetDirectoryWithoutShellInterpolation()
    {
        var root = @"C:\Install Path\阑山";
        var info = LegacyInstallerWorker.CreateStartInfo(@"C:\Updates\setup.exe", root, "2.0.0", "abc", @"C:\Updates\receipt.json");
        Assert.False(info.UseShellExecute);
        Assert.True(info.CreateNoWindow);
        var script = Encoding.Unicode.GetString(Convert.FromBase64String(info.ArgumentList[^1]));
        Assert.Contains("/DIR=", script);
        Assert.Contains("WaitForExit", script);
        Assert.Contains("FromBase64String", script);
        Assert.DoesNotContain(root, script);
    }

    private string CreateInstalledRoot()
    {
        var root = Path.Combine(_root, "installed");
        var app = Path.Combine(root, "app-1.0.0");
        Directory.CreateDirectory(app);
        File.WriteAllText(Path.Combine(app, ".current"), "");
        File.WriteAllText(Path.Combine(app, DeploymentLayout.GetHostExecutableName()), "old-host");
        File.WriteAllText(Path.Combine(root, DeploymentLayout.GetLauncherExecutableName()), "old-launcher");
        File.WriteAllText(Path.Combine(root, "runtime.bin"), "old-runtime");
        return root;
    }

    [Fact]
    public void DownloadedPackage_RestoresAfterServiceRestart()
    {
        var directory = Path.Combine(_root, "pending");
        Directory.CreateDirectory(Path.Combine(directory, "Files"));
        var manifest = Path.Combine(directory, "PLONDS.json");
        var zip = Path.Combine(directory, "Files.zip");
        File.WriteAllText(manifest, JsonSerializer.Serialize(Manifest()));
        File.WriteAllText(zip, "archive-fixture");
        var package = new PlondsPreparedPackage(new(2, 0, 0), PlondsPackageMode.Full, manifest, null, null, zip, Path.Combine(directory, "Files"));
        File.WriteAllText(Path.Combine(directory, "prepared-package.json"), JsonSerializer.Serialize(package));
        var settings = new SettingsFixture();
        settings.Snapshot.PendingUpdateInstallerPath = manifest;
        settings.Snapshot.PendingUpdateVersion = "2.0.0";
        using var service = new UpdateSettingsService(settings, () => throw new InvalidOperationException(), new WaitingPlondsFixture());
        Assert.Equal(UpdatePhase.Downloaded, service.CurrentPhase);
    }

    [Fact]
    public void MaintenanceReceipt_ReportsCompletedOnlyAfterInstallerCompletion()
    {
        var directory = Path.Combine(_root, "receipt");
        Directory.CreateDirectory(directory);
        var manifest = Path.Combine(directory, "PLONDS.json");
        File.WriteAllText(manifest, JsonSerializer.Serialize(Manifest()));
        File.WriteAllText(Path.Combine(directory, DeploymentMaintenance.ReceiptFileName), JsonSerializer.Serialize(
            new DeploymentMaintenanceReceipt("completed", "2.0.0", null), DeploymentMaintenanceJsonContext.Default.DeploymentMaintenanceReceipt));
        var settings = new SettingsFixture();
        settings.Snapshot.PendingUpdateInstallerPath = manifest;
        settings.Snapshot.PendingUpdateVersion = "2.0.0";
        using var service = new UpdateSettingsService(settings, () => throw new InvalidOperationException(), new WaitingPlondsFixture());
        Assert.Equal(UpdatePhase.Completed, service.CurrentPhase);
        Assert.Null(settings.Snapshot.PendingUpdateInstallerPath);
    }

    [Fact]
    public void GitHubDiscovery_SelectsMatchingArchitectureInRequestedChannel()
    {
        var json = """
            [
              {"tag_name":"v3.0.0","draft":false,"prerelease":true,"assets":[{"name":"PLONDS-windows-x64.json","browser_download_url":"https://fixture.test/preview.json"}]},
              {"tag_name":"v2.0.0","draft":false,"prerelease":false,"assets":[{"name":"PLONDS-windows-x86.json","browser_download_url":"https://fixture.test/x86.json"},{"name":"PLONDS-windows-x64.json","browser_download_url":"https://fixture.test/stable.json"}]}
            ]
            """;
        Assert.Equal("https://fixture.test/stable.json", Assert.Single(ReleaseManifestLocator.SelectAssetUrls(json, "windows-x64", false)));
        Assert.Equal("https://fixture.test/preview.json", ReleaseManifestLocator.SelectAssetUrls(json, "windows-x64", true)[0]);
    }

    private PreparedFilesPackage CreateOnlinePackage(bool clean, bool badHash)
    {
        var files = Path.Combine(_root, "source");
        var app = Path.Combine(files, "app-2.0.0");
        Directory.CreateDirectory(app);
        File.WriteAllText(Path.Combine(app, DeploymentLayout.GetHostExecutableName()), "new-host");
        File.WriteAllText(Path.Combine(files, DeploymentLayout.GetLauncherExecutableName()), "new-launcher");
        File.WriteAllText(Path.Combine(files, "runtime.bin"), "new-runtime");
        var archive = Path.Combine(_root, "Files.zip");
        ZipFile.CreateFromDirectory(files, archive);
        var manifest = new InstallerPlondsManifest("2.0", "2.0.0", "1.0.0", false, clean, "stable", DeploymentPlatform.Current,
            DateTimeOffset.UtcNow,
            new Dictionary<string, InstallerPlondsFileEntry> {
                [DeploymentLayout.GetHostExecutableName()] = new("replace", Hash(badHash ? "different" : "new-host"), 8)
            }, new Dictionary<string, InstallerPlondsChangedFileEntry>(),
            new Dictionary<string, string> { ["Files.zip"] = "sha256:" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(archive))).ToLowerInvariant() },
            null, null);
        return new("2.0.0", "fixture", archive, files, manifest);
    }

    private static string Hash(string content) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();
    private static PlondsClientManifest Manifest(string version = "2.0.0", string previous = "1.0.0", string channel = "stable", string? platform = null) =>
        new("2.0", version, previous, false, false, channel, platform ?? DeploymentPlatform.Current, DateTimeOffset.UtcNow,
            new Dictionary<string, PlondsClientFileEntry>(), new Dictionary<string, PlondsClientChangedFileEntry>(), new Dictionary<string, string>(), null, null);

    private sealed class PackageHandler(InstallerPlondsManifest manifest, byte[] archive) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(request.RequestUri!.Host == "api.github.com" ? new HttpResponseMessage(HttpStatusCode.NotFound) :
                new HttpResponseMessage(HttpStatusCode.OK) { Content = request.RequestUri.AbsolutePath.EndsWith(".json", StringComparison.Ordinal)
                    ? new StringContent(JsonSerializer.Serialize(manifest)) : new ByteArrayContent(archive) });
    }
    private sealed class PrivacyFixture : IPrivacyDeviceIdentityProvider { public string GetOrCreateDeviceId() => "fixture"; }
    private sealed class DownloaderFixture : IPlondsPackageDownloader
    {
        public int DeltaCalls;
        public int FullCalls;
        public Task<PlondsPreparedPackage> PrepareDeltaAsync(PlondsClientManifest manifest, PlondsSourceDescriptor source, CancellationToken ct) { DeltaCalls++; return Task.FromResult(new PlondsPreparedPackage(new(2, 0, 0), PlondsPackageMode.Delta, "fixture", null, null, null, null)); }
        public Task<PlondsPreparedPackage> PrepareFullAsync(PlondsClientManifest manifest, PlondsSourceDescriptor source, CancellationToken ct) { FullCalls++; return Task.FromResult(new PlondsPreparedPackage(new(2, 0, 0), PlondsPackageMode.Full, "fixture", null, null, null, null)); }
    }
    private sealed class WaitingPlondsFixture : IPlondsService
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<PlondsPrepareResult> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken Token;
        public Task<PlondsLatestResult> FindLatestAsync(Version current, CancellationToken ct) => Task.FromResult(
            PlondsLatestResult.Available(current, new(2, 0, 0), [new(new("fixture", "s3", "https://fixture.test/PLONDS.json"), Manifest())]));
        public Task<PlondsPrepareResult> FindAndPrepareLatestAsync(CancellationToken ct) => FindAndPrepareLatestAsync(new(1, 0, 0), false, ct);
        public Task<PlondsPrepareResult> FindAndPrepareLatestAsync(Version current, CancellationToken ct) => FindAndPrepareLatestAsync(current, false, ct);
        public Task<PlondsPrepareResult> FindAndPrepareLatestAsync(Version current, bool forceFullPackage, CancellationToken ct) { Token = ct; Entered.SetResult(); return Completion.Task; }
    }
    private sealed class SettingsFixture : ISettingsService
    {
        public AppSettingsSnapshot Snapshot = new();
        public event EventHandler<SettingsChangedEvent>? Changed;
        public T LoadSnapshot<T>(AirAppSettingsScope scope, string? subjectId = null, string? placementId = null) where T : new() =>
            typeof(T) == typeof(AppSettingsSnapshot) ? (T)(object)Snapshot.Clone() : new();
        public void SaveSnapshot<T>(AirAppSettingsScope scope, T snapshot, string? subjectId = null, string? placementId = null, string? sectionId = null, IReadOnlyCollection<string>? changedKeys = null)
        { if (snapshot is AppSettingsSnapshot app) Snapshot = app.Clone(); Changed?.Invoke(this, new(scope, subjectId, placementId, sectionId, changedKeys)); }
        public T LoadSection<T>(AirAppSettingsScope scope, string subjectId, string sectionId, string? placementId = null) where T : new() => new();
        public void SaveSection<T>(AirAppSettingsScope scope, string subjectId, string sectionId, T section, string? placementId = null, IReadOnlyCollection<string>? changedKeys = null) => throw new NotSupportedException();
        public void DeleteSection(AirAppSettingsScope scope, string subjectId, string sectionId, string? placementId = null) => throw new NotSupportedException();
        public T? GetValue<T>(AirAppSettingsScope scope, string key, string? subjectId = null, string? placementId = null, string? sectionId = null) => default;
        public void SetValue<T>(AirAppSettingsScope scope, string key, T value, string? subjectId = null, string? placementId = null, string? sectionId = null, IReadOnlyCollection<string>? changedKeys = null) => throw new NotSupportedException();
        public IComponentSettingsAccessor GetComponentAccessor(string componentId, string? placementId) => throw new NotSupportedException();
    }
}
