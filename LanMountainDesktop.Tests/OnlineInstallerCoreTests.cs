using System.IO.Compression;
using System.Security.Cryptography;
using LanDesktopPLONDS.Installer.Models;
using LanDesktopPLONDS.Installer.Services;
using LanDesktopPLONDS.Installer.ViewModels;
using LanMountainDesktop.Shared.Contracts.Privacy;
using Xunit;

namespace LanMountainDesktop.Tests;

public sealed class OnlineInstallerCoreTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(
        AppContext.BaseDirectory,
        "TestArtifacts",
        "LanMountainDesktop.Tests",
        nameof(OnlineInstallerCoreTests),
        Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    [Fact]
    public void PrivacyDeviceIdentityProvider_ReturnsStableAnonymousId()
    {
        var path = Path.Combine(_tempRoot, "identity.json");
        var first = new PrivacyDeviceIdentityProvider(path).GetOrCreateDeviceId();
        var second = new PrivacyDeviceIdentityProvider(path).GetOrCreateDeviceId();

        Assert.False(string.IsNullOrWhiteSpace(first));
        Assert.Equal(first, second);
        Assert.DoesNotContain(Environment.MachineName, first, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Environment.UserName, first, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InstallerPrivacyConsentStore_PersistsConfirmationForDeviceId()
    {
        var path = Path.Combine(_tempRoot, "privacy-consent.json");
        var store = new InstallerPrivacyConsentStore(path);

        Assert.False(store.HasConfirmed("device-a"));

        store.SaveConfirmed("device-a");

        Assert.True(new InstallerPrivacyConsentStore(path).HasConfirmed("device-a"));
        Assert.False(new InstallerPrivacyConsentStore(path).HasConfirmed("device-b"));
    }

    [Fact]
    public async Task InstallerWorkflowNavigation_AllowsOnlyUnlockedSteps()
    {
        var vm = new MainWindowViewModel(new FakeInstallService(), new PrivacyDeviceIdentityProvider(Path.Combine(_tempRoot, "identity.json")));

        vm.SelectedStep = vm.Steps.Single(step => step.StepId == InstallerStepId.Deploy);

        Assert.Equal(InstallerStepId.Welcome, vm.CurrentStep);

        await vm.NextCommand.ExecuteAsync(null);
        vm.SelectedStep = vm.Steps.Single(step => step.StepId == InstallerStepId.Welcome);

        Assert.Equal(InstallerStepId.Welcome, vm.CurrentStep);
    }

    [Fact]
    public void FilesZipUrlResolver_PrefersSourceSpecificThenDerivedThenFallbacks()
    {
        var manifest = CreateManifest(
            downloads: new InstallerPlondsDownloads(
                new InstallerPlondsGitHubDownloads(null, null, null, "https://github.test/Files.zip"),
                new InstallerPlondsS3Downloads(null, null, null, null, null, null, null, null, null, "https://s3.test/Files.zip", null, null)));

        var urls = InstallerPlondsUrlResolver.ResolveFilesZipUrls(
            manifest,
            new InstallerPlondsSource("s3", "s3", "https://origin.test/releases/PLONDS.json", 100));

        Assert.Equal("https://s3.test/Files.zip", urls[0].AbsoluteUri);
        Assert.Contains(urls, uri => uri.AbsoluteUri == "https://origin.test/releases/Files.zip");
        Assert.Contains(urls, uri => uri.AbsoluteUri == "https://github.test/Files.zip");
    }

    [Theory]
    [InlineData("")]
    [InlineData("C:\\")]
    [InlineData("C:\\Windows")]
    public void InstallerPathGuard_RejectsDangerousPaths(string path)
    {
        Assert.ThrowsAny<Exception>(() => InstallerPathGuard.NormalizeInstallPath(path));
    }

    [Fact]
    public async Task FilesPackageInstaller_DeploysFullPackageWithCurrentMarker()
    {
        var packageRoot = Path.Combine(_tempRoot, "Files");
        var appRoot = Path.Combine(packageRoot, "app-1.2.3");
        Directory.CreateDirectory(appRoot);
        File.WriteAllText(Path.Combine(packageRoot, "LanMountainDesktop.Launcher.exe"), "launcher");
        File.WriteAllText(Path.Combine(appRoot, "LanMountainDesktop.exe"), "host");
        File.WriteAllText(Path.Combine(appRoot, ".partial"), "old marker");
        var package = new PreparedFilesPackage(
            "1.2.3",
            "s3",
            Path.Combine(_tempRoot, "Files.zip"),
            packageRoot,
            CreateManifest());
        var target = Path.Combine(_tempRoot, "install", "LanMountainDesktop");

        await new FilesPackageInstaller().InstallAsync(package, target, null, CancellationToken.None);

        var deployment = Directory.GetDirectories(target, "app-1.2.3-0").Single();
        Assert.True(File.Exists(Path.Combine(target, "LanMountainDesktop.Launcher.exe")));
        Assert.True(File.Exists(Path.Combine(deployment, "LanMountainDesktop.exe")));
        Assert.True(File.Exists(Path.Combine(deployment, ".current")));
        Assert.False(File.Exists(Path.Combine(deployment, ".partial")));
    }

    [Fact]
    public async Task ZipExtraction_RejectsEscapingEntry()
    {
        var zipPath = Path.Combine(_tempRoot, "bad.zip");
        Directory.CreateDirectory(_tempRoot);
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("../escape.txt");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("bad");
        }

        var manifest = CreateManifest(checksums: new Dictionary<string, string>
        {
            ["Files.zip"] = "sha256:" + Sha256(zipPath)
        });
        var client = new InstallerPlondsClient(new HttpClient(new FileHandler(zipPath)), Path.Combine(_tempRoot, "staging"));
        var candidate = new InstallerPlondsCandidate(
            new InstallerPlondsSource("s3", "s3", "https://s3.test/PLONDS.json", 100),
            manifest,
            new Uri("https://s3.test/Files.zip"));

        await Assert.ThrowsAsync<InvalidDataException>(() => client.DownloadAndPrepareFullPackageAsync(candidate, null, CancellationToken.None));
    }

    private static InstallerPlondsManifest CreateManifest(
        InstallerPlondsDownloads? downloads = null,
        IReadOnlyDictionary<string, string>? checksums = null)
    {
        return new InstallerPlondsManifest(
            "1",
            "1.2.3",
            "1.2.2",
            true,
            false,
            "stable",
            "windows-x64",
            DateTimeOffset.UtcNow,
            new Dictionary<string, InstallerPlondsFileEntry>(),
            new Dictionary<string, InstallerPlondsChangedFileEntry>(),
            checksums ?? new Dictionary<string, string>(),
            downloads,
            null);
    }

    private static string Sha256(string filePath)
    {
        using var sha = SHA256.Create();
        using var stream = File.OpenRead(filePath);
        return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
    }

    private sealed class FakeInstallService : IOnlineInstallService
    {
        public Task<OnlineInstallPackageInfo> CheckLatestAsync(CancellationToken cancellationToken)
            => Task.FromResult(new OnlineInstallPackageInfo("1.2.3", "test", new Uri("https://test/Files.zip"), 1));

        public Task InstallFreshAsync(string installPath, IProgress<InstallerDeployProgress>? progress, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task InstallFreshAsync(
            string installPath,
            OnlineInstallOptions options,
            IProgress<InstallerDeployProgress>? progress,
            CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task RepairAsync(string installPath, IProgress<InstallerDeployProgress>? progress, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task UpdateIncrementalAsync(string installPath, IProgress<InstallerDeployProgress>? progress, CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }

    private sealed class FileHandler(string zipPath) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(File.ReadAllBytes(zipPath))
            };
            response.Content.Headers.ContentLength = new FileInfo(zipPath).Length;
            return Task.FromResult(response);
        }
    }
}
