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
        var deployStep = vm.Steps.Single(step => step.StepId == InstallerStepId.Deploy);

        Assert.False(deployStep.IsUnlocked);

        vm.SelectStepCommand.Execute(deployStep);

        Assert.Equal(InstallerStepId.Welcome, vm.CurrentStep);
        Assert.True(vm.Steps.Single(step => step.StepId == InstallerStepId.Welcome).IsSelected);

        await vm.NextCommand.ExecuteAsync(null);
        vm.SelectStepCommand.Execute(vm.Steps.Single(step => step.StepId == InstallerStepId.Welcome));

        Assert.Equal(InstallerStepId.Welcome, vm.CurrentStep);
    }

    [Fact]
    public async Task BrowseCommand_ReportsPickerFailuresWithoutChangingInstallPath()
    {
        var vm = new MainWindowViewModel(
            new FakeInstallService(),
            new PrivacyDeviceIdentityProvider(Path.Combine(_tempRoot, "identity.json")))
        {
            BrowseRequested = _ => throw new InvalidOperationException("picker failed")
        };
        var originalPath = vm.InstallPath;

        await vm.BrowseCommand.ExecuteAsync(null);

        Assert.Equal(originalPath, vm.InstallPath);
        Assert.Contains("选择安装位置失败", vm.ErrorMessage);
        Assert.Contains("picker failed", vm.ErrorMessage);
    }

    [Fact]
    public async Task BrowseCommand_UsesSelectedLocalFolderAsInstallParent()
    {
        var selectedPath = Path.Combine(_tempRoot, "selected-install-root");
        var vm = new MainWindowViewModel(
            new FakeInstallService(),
            new PrivacyDeviceIdentityProvider(Path.Combine(_tempRoot, "identity.json")))
        {
            BrowseRequested = _ => Task.FromResult<string?>(selectedPath)
        };

        await vm.BrowseCommand.ExecuteAsync(null);

        Assert.Equal(Path.Combine(selectedPath, InstallerPathGuard.ApplicationDirectoryName), vm.InstallPath);
        Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public async Task BrowseCommand_DoesNotDuplicateApplicationFolder()
    {
        var selectedPath = Path.Combine(_tempRoot, InstallerPathGuard.ApplicationDirectoryName);
        var vm = new MainWindowViewModel(
            new FakeInstallService(),
            new PrivacyDeviceIdentityProvider(Path.Combine(_tempRoot, "identity.json")))
        {
            BrowseRequested = _ => Task.FromResult<string?>(selectedPath)
        };

        await vm.BrowseCommand.ExecuteAsync(null);

        Assert.Equal(selectedPath, vm.InstallPath);
        Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public async Task StartInstallCommand_PassesShortcutAndStartupOptions()
    {
        var installService = new FakeInstallService();
        var vm = new MainWindowViewModel(
            installService,
            new PrivacyDeviceIdentityProvider(Path.Combine(_tempRoot, "identity.json")))
        {
            InstallPath = Path.Combine(_tempRoot, "install", "LanMountainDesktop"),
            PrivacyConfirmed = true,
            CreateDesktopShortcut = true,
            CreateStartupShortcut = true
        };
        await vm.NextCommand.ExecuteAsync(null);
        await vm.NextCommand.ExecuteAsync(null);
        await vm.NextCommand.ExecuteAsync(null);

        await vm.StartInstallCommand.ExecuteAsync(null);

        Assert.NotNull(installService.LastOptions);
        Assert.True(installService.LastOptions.CreateDesktopShortcut);
        Assert.True(installService.LastOptions.CreateStartupShortcut);
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

    [Fact]
    public async Task FindLatest_ParsesCamelCasePlondsManifest()
    {
        var client = new InstallerPlondsClient(
            new HttpClient(new ManifestHandler("""
                {
                  "formatVersion": "2.0",
                  "currentVersion": "1.2.4",
                  "previousVersion": "1.2.3",
                  "isFullUpdate": false,
                  "requiresCleanInstall": true,
                  "channel": "preview",
                  "platform": "windows-x64",
                  "updatedAt": "2026-06-03T00:00:00Z",
                  "filesMap": {},
                  "changedFilesMap": {},
                  "checksums": {
                    "Files.zip": "md5:00000000000000000000000000000000"
                  },
                  "downloads": {
                    "s3": {
                      "filesZipUrl": "https://s3.test/Files.zip"
                    },
                    "github": {
                      "filesZipUrl": "https://github.test/files-windows-x64.zip"
                    }
                  }
                }
                """)),
            Path.Combine(_tempRoot, "staging"));

        var candidate = await client.FindLatestAsync(CancellationToken.None);

        Assert.Equal("1.2.4", candidate.Manifest.CurrentVersion);
        Assert.Equal("preview", candidate.Manifest.Channel);
        Assert.Equal("https://s3.test/Files.zip", candidate.FilesZipUrl.AbsoluteUri);
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
    public void InstallerPathGuard_DefaultsToUserWritableProgramsFolder()
    {
        var path = InstallerPathGuard.GetDefaultInstallPath();

        Assert.EndsWith(Path.Combine("Programs", InstallerPathGuard.ApplicationDirectoryName), path);
        Assert.DoesNotContain("Program Files", path, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InstallerElevation_DetectsProtectedProgramFilesPath()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (string.IsNullOrWhiteSpace(programFiles))
        {
            return;
        }

        Assert.True(InstallerElevation.RequiresElevation(Path.Combine(programFiles, InstallerPathGuard.ApplicationDirectoryName)));
        Assert.False(InstallerElevation.RequiresElevation(Path.Combine(_tempRoot, InstallerPathGuard.ApplicationDirectoryName)));
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

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.DownloadAndPrepareFullPackageAsync(candidate, null, CancellationToken.None));
        Assert.IsType<InvalidDataException>(exception.InnerException);
    }

    [Fact]
    public async Task DownloadAndPrepareFullPackage_FallsBackWhenFirstPackageUrlFails()
    {
        var zipPath = Path.Combine(_tempRoot, "Files.zip");
        Directory.CreateDirectory(_tempRoot);
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("LanMountainDesktop.exe");
            await using var stream = entry.Open();
            await using var writer = new StreamWriter(stream);
            await writer.WriteAsync("host");
        }

        var manifest = CreateManifest(
            downloads: new InstallerPlondsDownloads(
                new InstallerPlondsGitHubDownloads(null, null, null, "https://github.test/files-windows-x64.zip"),
                new InstallerPlondsS3Downloads(null, null, null, null, null, null, null, null, null, "https://s3.test/Files.zip", null, null)),
            checksums: new Dictionary<string, string>
            {
                ["Files.zip"] = "sha256:" + Sha256(zipPath)
            });
        var client = new InstallerPlondsClient(
            new HttpClient(new FallbackPackageHandler(zipPath)),
            Path.Combine(_tempRoot, "staging"));
        var candidate = new InstallerPlondsCandidate(
            new InstallerPlondsSource("s3", "s3", "https://origin.test/releases/PLONDS.json", 100),
            manifest,
            new Uri("https://s3.test/Files.zip"));

        var package = await client.DownloadAndPrepareFullPackageAsync(candidate, null, CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(package.ExtractDirectory, "LanMountainDesktop.exe")));
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
        public OnlineInstallOptions? LastOptions { get; private set; }

        public Task<OnlineInstallPackageInfo> CheckLatestAsync(CancellationToken cancellationToken)
            => Task.FromResult(new OnlineInstallPackageInfo("1.2.3", "test", new Uri("https://test/Files.zip"), 1));

        public Task InstallFreshAsync(string installPath, IProgress<InstallerDeployProgress>? progress, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task InstallFreshAsync(
            string installPath,
            OnlineInstallOptions options,
            IProgress<InstallerDeployProgress>? progress,
            CancellationToken cancellationToken)
        {
            LastOptions = options;
            return Task.CompletedTask;
        }

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

    private sealed class FallbackPackageHandler(string zipPath) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri?.AbsoluteUri == "https://github.test/files-windows-x64.zip")
            {
                var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(File.ReadAllBytes(zipPath))
                };
                response.Content.Headers.ContentLength = new FileInfo(zipPath).Length;
                return Task.FromResult(response);
            }

            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
        }
    }

    private sealed class ManifestHandler(string manifestJson) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(manifestJson)
            });
        }
    }
}
