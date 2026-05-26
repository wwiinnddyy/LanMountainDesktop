using CommunityToolkit.Mvvm.Input;
using LanMountainDesktop.Models;
using LanMountainDesktop.PluginSdk;
using LanMountainDesktop.Services;
using LanMountainDesktop.Services.Settings;
using LanMountainDesktop.Services.Update;
using LanMountainDesktop.Shared.Contracts.Update;
using LanMountainDesktop.ViewModels;
using Xunit;
using UpdateDownloadResult = LanMountainDesktop.Services.Update.DownloadResult;
using SettingsUpdateState = LanMountainDesktop.Services.Settings.UpdateSettingsState;

namespace LanMountainDesktop.Tests;

public sealed class UpdateSettingsInterfaceTests
{
    [Fact]
    public async Task UpdateSettingsViewModel_RoutesActionsThroughUpdateSettingsService()
    {
        var update = new FakeUpdateSettingsService();
        var viewModel = new UpdateSettingsViewModel(new FakeSettingsFacade(update));

        Assert.Equal(0, update.SaveCalls);

        update.CheckReport = new UpdateCheckReport(
            true,
            "1.2.3",
            "1.0.0",
            UpdatePayloadKind.DeltaPlonds,
            "dist-1",
            UpdateSettingsValues.ChannelStable,
            DateTimeOffset.Parse("2026-05-06T00:00:00Z"),
            42,
            null,
            null);

        await ((IAsyncRelayCommand)viewModel.CheckCommand).ExecuteAsync(null);

        Assert.Equal(1, update.CheckCalls);
        Assert.Equal("1.2.3", viewModel.LatestVersionText);
        Assert.True(viewModel.IsDeltaUpdate);

        update.SetPhase(UpdatePhase.Checked);
        await ((IAsyncRelayCommand)viewModel.DownloadCommand).ExecuteAsync(null);
        Assert.Equal(1, update.DownloadCalls);

        update.SetPhase(UpdatePhase.Downloaded);
        await ((IAsyncRelayCommand)viewModel.InstallCommand).ExecuteAsync(null);
        Assert.Equal(1, update.InstallCalls);

        update.SetPhase(UpdatePhase.Downloading);
        await ((IAsyncRelayCommand)viewModel.PauseCommand).ExecuteAsync(null);
        Assert.Equal(1, update.PauseCalls);

        update.SetPhase(UpdatePhase.PausedDownloading);
        await ((IAsyncRelayCommand)viewModel.ResumeCommand).ExecuteAsync(null);
        Assert.Equal(1, update.ResumeCalls);

        update.SetPhase(UpdatePhase.Downloading);
        await ((IAsyncRelayCommand)viewModel.CancelCommand).ExecuteAsync(null);
        Assert.Equal(1, update.CancelCalls);
    }

    [Fact]
    public void UpdateSettingsViewModel_SavesPreferencesThroughUpdateSettingsService()
    {
        var update = new FakeUpdateSettingsService();
        var viewModel = new UpdateSettingsViewModel(new FakeSettingsFacade(update));

        viewModel.SelectedUpdateChannelValue = UpdateSettingsValues.ChannelPreview;
        viewModel.SelectedUpdateSourceValue = UpdateSettingsValues.DownloadSourceGitHub;
        viewModel.SelectedUpdateModeValue = UpdateSettingsValues.ModeManual;
        viewModel.DownloadThreadsSliderValue = 12;
        viewModel.ForceReinstall = true;

        Assert.True(update.SaveCalls >= 5);
        Assert.Equal(UpdateSettingsValues.ChannelPreview, update.State.UpdateChannel);
        Assert.Equal(UpdateSettingsValues.DownloadSourceGitHub, update.State.UpdateDownloadSource);
        Assert.Equal(UpdateSettingsValues.ModeManual, update.State.UpdateMode);
        Assert.Equal(12, update.State.UpdateDownloadThreads);
        Assert.True(update.State.ForceUpdateReinstall);
    }

    [Fact]
    public void UpdateSettingsViewModel_RestoresPersistedPendingAndLastCheckedState()
    {
        var update = new FakeUpdateSettingsService
        {
            State = DefaultUpdateState() with
            {
                PendingUpdateVersion = "2.0.0",
                PendingUpdatePublishedAtUtcMs = DateTimeOffset.Parse("2026-05-06T00:00:00Z").ToUnixTimeMilliseconds(),
                LastUpdateCheckUtcMs = DateTimeOffset.Parse("2026-05-07T00:00:00Z").ToUnixTimeMilliseconds()
            }
        };

        var viewModel = new UpdateSettingsViewModel(new FakeSettingsFacade(update));

        Assert.True(viewModel.IsUpdateAvailable);
        Assert.Equal("2.0.0", viewModel.LatestVersionText);
        Assert.NotEmpty(viewModel.PublishedAtText);
        Assert.Contains("Last checked", viewModel.LastCheckedText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SettingsUpdateManifestProvider_UsesSelectedUpdateSource()
    {
        var update = new FakeUpdateSettingsService
        {
            State = DefaultUpdateState() with { UpdateDownloadSource = UpdateSettingsValues.DownloadSourceGitHub }
        };
        var plonds = new FakeManifestProvider("plonds");
        var github = new FakeManifestProvider("github");
        var provider = new SettingsUpdateManifestProvider(new FakeSettingsFacade(update), plonds, github);

        var manifest = await provider.GetLatestAsync(
            UpdateSettingsValues.ChannelStable,
            "windows-x64",
            new Version(1, 0, 0),
            CancellationToken.None);

        Assert.Equal("github", manifest?.DistributionId);
        Assert.Equal(0, plonds.GetLatestCalls);
        Assert.Equal(1, github.GetLatestCalls);

        update.State = update.State with { UpdateDownloadSource = UpdateSettingsValues.DownloadSourcePlonds };
        manifest = await provider.GetLatestAsync(
            UpdateSettingsValues.ChannelStable,
            "windows-x64",
            new Version(1, 0, 0),
            CancellationToken.None);

        Assert.Equal("plonds", manifest?.DistributionId);
        Assert.Equal(1, plonds.GetLatestCalls);
    }

    [Fact]
    public void FromFullInstaller_IncludesPreferredInstallerInMirrors()
    {
        var release = new GitHubReleaseInfo(
            "v1.2.3",
            "Release",
            false,
            false,
            DateTimeOffset.Parse("2026-05-06T00:00:00Z"),
            [new GitHubReleaseAsset("LanMountainDesktop-setup-x64.exe", "https://example.test/setup.exe", 123, "abc")]);

        var manifest = UpdateManifestMapper.FromFullInstaller(release, UpdateSettingsValues.ChannelStable, "windows-x64");

        Assert.NotNull(manifest.InstallerMirrors);
        var mirror = Assert.Single(manifest.InstallerMirrors!);
        Assert.Equal("https://example.test/setup.exe", mirror.Url);
    }

    [Fact]
    public void ApplyDownloadSource_UsesGhProxyForGithubProxySource()
    {
        var url = "https://github.com/owner/repo/releases/download/v1/app.exe";

        Assert.Equal(url, UpdateDownloadEngine.ApplyDownloadSource(url, UpdateSettingsValues.DownloadSourceGitHub));
        Assert.Equal(
            $"{UpdateSettingsValues.DefaultGhProxyBaseUrl}{url}",
            UpdateDownloadEngine.ApplyDownloadSource(url, UpdateSettingsValues.DownloadSourceGhProxy));
    }

    private static SettingsUpdateState DefaultUpdateState() => new(
        IncludePrereleaseUpdates: false,
        UpdateChannel: UpdateSettingsValues.ChannelStable,
        UpdateMode: UpdateSettingsValues.ModeSilentDownload,
        UpdateDownloadSource: UpdateSettingsValues.DownloadSourcePlonds,
        UpdateDownloadThreads: UpdateSettingsValues.DefaultDownloadThreads,
        ForceUpdateReinstall: false,
        UseGhProxyMirror: false,
        PendingUpdateInstallerPath: null,
        PendingUpdateVersion: null,
        PendingUpdatePublishedAtUtcMs: null,
        LastUpdateCheckUtcMs: null,
        PendingUpdateSha256: null);

    private sealed class FakeUpdateSettingsService : IUpdateSettingsService
    {
        public SettingsUpdateState State { get; set; } = DefaultUpdateState();
        public UpdatePhase CurrentPhase { get; private set; } = UpdatePhase.Idle;
        public UpdateCheckReport CheckReport { get; set; } = new(false, null, null, null, null, null, null, null, null, null);
        public UpdateDownloadResult DownloadResult { get; set; } = new(true, "downloaded", null, true);
        public InstallResult InstallResult { get; set; } = new(true, null, false);
        public int SaveCalls { get; private set; }
        public int CheckCalls { get; private set; }
        public int DownloadCalls { get; private set; }
        public int InstallCalls { get; private set; }
        public int PauseCalls { get; private set; }
        public int ResumeCalls { get; private set; }
        public int CancelCalls { get; private set; }

        public event Action<UpdatePhase>? PhaseChanged;
        public event Action<UpdateProgressReport>? ProgressChanged;

        public void SetPhase(UpdatePhase phase)
        {
            CurrentPhase = phase;
            PhaseChanged?.Invoke(phase);
        }

        public SettingsUpdateState Get() => State;

        public void Save(SettingsUpdateState state)
        {
            SaveCalls++;
            State = state;
        }

        public Task<UpdateCheckReport> CheckAsync(CancellationToken cancellationToken = default)
        {
            CheckCalls++;
            SetPhase(UpdatePhase.Checked);
            return Task.FromResult(CheckReport);
        }

        public Task<UpdateDownloadResult> DownloadAsync(CancellationToken cancellationToken = default)
        {
            DownloadCalls++;
            SetPhase(UpdatePhase.Downloaded);
            return Task.FromResult(DownloadResult);
        }

        public Task<InstallResult> InstallAsync(CancellationToken cancellationToken = default)
        {
            InstallCalls++;
            SetPhase(UpdatePhase.Installed);
            return Task.FromResult(InstallResult);
        }

        public Task RollbackAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task PauseAsync()
        {
            PauseCalls++;
            SetPhase(UpdatePhase.PausedDownloading);
            return Task.CompletedTask;
        }

        public Task<UpdateDownloadResult> ResumeAsync(CancellationToken cancellationToken = default)
        {
            ResumeCalls++;
            SetPhase(UpdatePhase.Downloaded);
            return Task.FromResult(DownloadResult);
        }

        public Task CancelAsync()
        {
            CancelCalls++;
            SetPhase(UpdatePhase.Idle);
            return Task.CompletedTask;
        }

        public Task AutoCheckIfEnabledAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public bool TryApplyOnExit() => false;

        public Task<UpdateCheckResult> CheckForUpdatesAsync(Version currentVersion, bool includePrerelease, CancellationToken cancellationToken = default)
            => Task.FromResult(new UpdateCheckResult(true, false, currentVersion.ToString(), string.Empty, null, null, null));

        public Task<UpdateCheckResult> ForceCheckForUpdatesAsync(Version currentVersion, bool includePrerelease, CancellationToken cancellationToken = default)
            => CheckForUpdatesAsync(currentVersion, includePrerelease, cancellationToken);

        public Task<PlondsUpdatePayload?> GetPlondsUpdatePayloadAsync(Version currentVersion, bool includePrerelease, bool isForce = false, CancellationToken cancellationToken = default)
            => Task.FromResult<PlondsUpdatePayload?>(null);

        public Task<LanMountainDesktop.Services.UpdateDownloadResult> DownloadAssetAsync(
            GitHubReleaseAsset asset,
            string destinationFilePath,
            string downloadSource,
            int maxParallelSegments,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new LanMountainDesktop.Services.UpdateDownloadResult(false, null, "not used", false));

        public Task<LanMountainDesktop.Services.UpdateDownloadResult> RedownloadAssetAsync(
            GitHubReleaseAsset asset,
            string destinationFilePath,
            string downloadSource,
            int maxParallelSegments,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new LanMountainDesktop.Services.UpdateDownloadResult(false, null, "not used", false));
    }

    private sealed class FakeManifestProvider(string providerName) : IUpdateManifestProvider
    {
        public string ProviderName { get; } = providerName;
        public int GetLatestCalls { get; private set; }

        public Task<UpdateManifest?> GetLatestAsync(string channel, string platform, Version currentVersion, CancellationToken ct)
        {
            GetLatestCalls++;
            return Task.FromResult<UpdateManifest?>(CreateManifest(ProviderName, channel, platform));
        }

        public Task<UpdateManifest?> GetByVersionAsync(string version, string channel, string platform, CancellationToken ct)
            => Task.FromResult<UpdateManifest?>(CreateManifest(ProviderName, channel, platform));

        public Task<IReadOnlyList<UpdateManifest>> GetIncrementalChainAsync(string channel, string platform, Version fromVersion, Version toVersion, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<UpdateManifest>>([CreateManifest(ProviderName, channel, platform)]);

        private static UpdateManifest CreateManifest(string id, string channel, string platform) => new(
            id,
            "1.0.0",
            "1.1.0",
            platform,
            channel,
            DateTimeOffset.Parse("2026-05-06T00:00:00Z"),
            UpdatePayloadKind.DeltaPlonds,
            "https://example.test/filemap.json",
            "https://example.test/filemap.json.sig",
            null,
            [],
            null,
            new Dictionary<string, string>());
    }

    private sealed class FakeSettingsFacade(IUpdateSettingsService update) : ISettingsFacadeService
    {
        public ISettingsService Settings => throw new NotSupportedException();
        public ISettingsCatalog Catalog => throw new NotSupportedException();
        public IGridSettingsService Grid => throw new NotSupportedException();
        public IWallpaperSettingsService Wallpaper => throw new NotSupportedException();
        public IWallpaperMediaService WallpaperMedia => throw new NotSupportedException();
        public IThemeAppearanceService Theme => throw new NotSupportedException();
        public IStatusBarSettingsService StatusBar => throw new NotSupportedException();
        public ITextCapsuleSettingsService TextCapsule => throw new NotSupportedException();
        public IWeatherSettingsService Weather => throw new NotSupportedException();
        public IRegionSettingsService Region { get; } = new FakeRegionSettingsService();
        public IPrivacySettingsService Privacy => throw new NotSupportedException();
        public IUpdateSettingsService Update { get; } = update;
        public ILauncherCatalogService LauncherCatalog => throw new NotSupportedException();
        public ILauncherPolicyService LauncherPolicy => throw new NotSupportedException();
        public IPluginManagementSettingsService PluginManagement => throw new NotSupportedException();
        public IPluginCatalogSettingsService PluginCatalog => throw new NotSupportedException();
        public IApplicationInfoService ApplicationInfo { get; } = new FakeApplicationInfoService();
    }

    private sealed class FakeRegionSettingsService : IRegionSettingsService
    {
        public RegionSettingsState Get() => new("en-US", null);
        public void Save(RegionSettingsState state) { }
        public TimeZoneService GetTimeZoneService() => throw new NotSupportedException();
    }

    private sealed class FakeApplicationInfoService : IApplicationInfoService
    {
        public string GetAppVersionText() => "1.0.0";
        public string GetAppCodenameText() => "Test";
        public AppRenderBackendInfo GetRenderBackendInfo() => throw new NotSupportedException();
    }
}
