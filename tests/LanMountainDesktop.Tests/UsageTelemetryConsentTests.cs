using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using LanMountainDesktop.AirAppSdk;
using LanMountainDesktop.Models;
using LanMountainDesktop.Services;
using LanMountainDesktop.Services.Settings;

using Xunit;

namespace LanMountainDesktop.Tests;

/// <summary>
/// 隐私开关与"到底发没发"之间的行为钉。
///
/// 为什么现在才补上：这个服务以前在构造函数里直接 new 真客户端（真 project key、真 host），
/// 任何测试跑它都会真往 PostHog 发数据，于是"关掉就不该发"这条闸一格测试都没有——
/// 2026-09-30 是真机日志把它抓出来的（同一秒里 "Sent first-launch baseline event" 与
/// "Usage telemetry initialized. Enabled=False" 并存），不是测试。
/// 现在有了 <see cref="PostHogUsageTelemetryService.IUsageTelemetryClient"/> 接缝，三件事各自钉住：
/// ① 启动时关着 → 一条都不发，也**不**标"已上报"（否则用户后来打开就永远补不回来）；
/// ② 启动时开着 → 首启基线发一次，且只发一次；
/// ③ 先关后开 → 在打开的那一刻发。
///
/// 三格共用进程级单例 <see cref="TelemetryIdentityService"/>，所以必须留在同一个类里
/// （xUnit 默认按类并行；每格开头 <c>ResetForTests</c> 拿回干净起点）。
/// </summary>
public sealed class UsageTelemetryConsentTests
{
    [Fact]
    public void DisabledAtStartup_SendsNothingAndStaysUnreported()
    {
        TelemetryIdentityService.ResetForTests();
        var settings = new FakeSettingsService();
        var client = new RecordingClient();
        TelemetryIdentityService.Initialize(new FakeFacade(settings));

        using (var service = new PostHogUsageTelemetryService(new FakeFacade(settings), client))
        {
            service.Initialize();

            Assert.False(service.IsUsageEnabled);
        }

        Assert.Empty(client.Captured);
        Assert.Equal(0, client.IdentifyCount);
        Assert.False(TelemetryIdentityService.Instance.HasReportedBaseline);
    }

    [Fact]
    public void EnabledAtStartup_SendsTheBaselineOnce()
    {
        TelemetryIdentityService.ResetForTests();
        var settings = new FakeSettingsService();
        settings.Snapshot.UploadAnonymousUsageData = true;
        var facade = new FakeFacade(settings);
        TelemetryIdentityService.Initialize(facade);
        var client = new RecordingClient();

        using (var first = new PostHogUsageTelemetryService(facade, client))
        {
            first.Initialize();

            // 断言放在释放之前：Dispose 会按会话语义再补一条 app_session_end，那是另一件事。
            Assert.Equal(
                [TelemetryEventNames.AppFirstLaunch, TelemetryEventNames.AppSessionStart],
                client.Captured);
            Assert.Equal(1, client.IdentifyCount);
        }

        Assert.True(TelemetryIdentityService.Instance.HasReportedBaseline);

        // 第二个实例（等价于"再启动一次"）只该再开一次会话，不许再发基线：钉的是"基线只发一次"。
        using (var second = new PostHogUsageTelemetryService(facade, client))
        {
            second.Initialize();
        }

        Assert.Equal(
            1,
            client.Captured.Count(name => string.Equals(name, TelemetryEventNames.AppFirstLaunch, StringComparison.Ordinal)));
    }

    [Fact]
    public void EnablingUsageTelemetryLater_SendsTheBaselineAtThatMoment()
    {
        TelemetryIdentityService.ResetForTests();
        var settings = new FakeSettingsService();
        var facade = new FakeFacade(settings);
        TelemetryIdentityService.Initialize(facade);
        var client = new RecordingClient();

        using (var service = new PostHogUsageTelemetryService(facade, client))
        {
            service.Initialize();
            Assert.Empty(client.Captured);

            settings.Snapshot.UploadAnonymousUsageData = true;
            settings.RaiseUsageConsentChanged();

            Assert.True(service.IsUsageEnabled);
            Assert.Equal(
                [TelemetryEventNames.AppFirstLaunch, TelemetryEventNames.AppSessionStart],
                client.Captured);
            Assert.True(TelemetryIdentityService.Instance.HasReportedBaseline);
        }
    }

    private sealed class RecordingClient : PostHogUsageTelemetryService.IUsageTelemetryClient
    {
        public List<string> Captured { get; } = [];

        public int IdentifyCount { get; private set; }

        public void Capture(string distinctId, string eventName, Dictionary<string, object?> properties)
            => Captured.Add(eventName);

        public Task IdentifyAsync(
            string distinctId,
            Dictionary<string, object?> properties,
            CancellationToken cancellationToken)
        {
            IdentifyCount++;
            return Task.CompletedTask;
        }

        public Task FlushAsync() => Task.CompletedTask;

        public void Dispose()
        {
        }
    }

    private sealed class FakeSettingsService : ISettingsService
    {
        public AppSettingsSnapshot Snapshot { get; } = new();

        public event EventHandler<SettingsChangedEvent>? Changed;

        public void RaiseUsageConsentChanged() => Changed?.Invoke(
            this,
            new SettingsChangedEvent(
                AirAppSettingsScope.App,
                changedKeys: [nameof(AppSettingsSnapshot.UploadAnonymousUsageData)]));

        public T LoadSnapshot<T>(
            AirAppSettingsScope scope,
            string? subjectId = null,
            string? placementId = null) where T : new() => (T)(object)Snapshot;

        public void SaveSnapshot<T>(
            AirAppSettingsScope scope,
            T snapshot,
            string? subjectId = null,
            string? placementId = null,
            string? sectionId = null,
            IReadOnlyCollection<string>? changedKeys = null)
        {
        }

        public T LoadSection<T>(
            AirAppSettingsScope scope,
            string subjectId,
            string sectionId,
            string? placementId = null) where T : new() => throw new NotSupportedException();

        public void SaveSection<T>(
            AirAppSettingsScope scope,
            string subjectId,
            string sectionId,
            T section,
            string? placementId = null,
            IReadOnlyCollection<string>? changedKeys = null) => throw new NotSupportedException();

        public void DeleteSection(
            AirAppSettingsScope scope,
            string subjectId,
            string sectionId,
            string? placementId = null) => throw new NotSupportedException();

        public T? GetValue<T>(
            AirAppSettingsScope scope,
            string key,
            string? subjectId = null,
            string? placementId = null,
            string? sectionId = null) => throw new NotSupportedException();

        public void SetValue<T>(
            AirAppSettingsScope scope,
            string key,
            T value,
            string? subjectId = null,
            string? placementId = null,
            string? sectionId = null,
            IReadOnlyCollection<string>? changedKeys = null) => throw new NotSupportedException();

        public IComponentSettingsAccessor GetComponentAccessor(
            string componentId,
            string? placementId) => throw new NotSupportedException();
    }

    private sealed class FakeFacade(ISettingsService settings) : ISettingsFacadeService
    {
        public ISettingsService Settings { get; } = settings;

        public ISettingsCatalog Catalog => throw new NotSupportedException();
        public IGridSettingsService Grid => throw new NotSupportedException();
        public IWallpaperSettingsService Wallpaper => throw new NotSupportedException();
        public IWallpaperMediaService WallpaperMedia => throw new NotSupportedException();
        public IThemeAppearanceService Theme => throw new NotSupportedException();
        public IStatusBarSettingsService StatusBar => throw new NotSupportedException();
        public ITextCapsuleSettingsService TextCapsule => throw new NotSupportedException();
        public IWeatherSettingsService Weather => throw new NotSupportedException();
        public IRegionSettingsService Region => throw new NotSupportedException();
        public IPrivacySettingsService Privacy => throw new NotSupportedException();
        public IUpdateSettingsService Update => throw new NotSupportedException();
        public ILauncherCatalogService LauncherCatalog => throw new NotSupportedException();
        public ILauncherPolicyService LauncherPolicy => throw new NotSupportedException();
        public IAirAppManagementSettingsService AirAppManagement => throw new NotSupportedException();
        public IAirAppCatalogSettingsService AirAppCatalog => throw new NotSupportedException();
        public IApplicationInfoService ApplicationInfo => throw new NotSupportedException();
    }
}
