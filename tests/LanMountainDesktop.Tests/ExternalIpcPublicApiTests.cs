using LanMountainDesktop.AirAppSdk;
using LanMountainDesktop.Shared.IPC;
using LanMountainDesktop.Shared.IPC.Abstractions.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LanMountainDesktop.Tests;

public sealed class ExternalIpcPublicApiTests
{
    [Fact]
    public async Task PublicIpcHost_ExposesStrongTypedServiceAndCatalog()
    {
        var pipeName = "LanMountainDesktop.Test." + Guid.NewGuid().ToString("N");
        using var host = new PublicIpcHostService(pipeName);
        host.AirAppDescriptorProvider = () =>
        [
            new PublicAirAppDescriptor("sample.plugin", "Sample AirApp", "1.0.0", true, true)
        ];

        var appInfo = new PublicAppInfoSnapshot(
            "LanMountainDesktop",
            "1.2.3",
            "Administrate",
            pipeName,
            42,
            DateTimeOffset.UtcNow);
        host.RegisterPublicService<IPublicAppInfoService>(new TestPublicAppInfoService(appInfo));
        host.Start();

        using var client = new LanMountainDesktopIpcClient();
        var catalogChanged = new TaskCompletionSource<PublicIpcCatalogSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.RegisterNotifyHandler<PublicIpcCatalogSnapshot>(IpcRoutedNotifyIds.CatalogChanged, snapshot =>
        {
            catalogChanged.TrySetResult(snapshot);
        });

        await client.ConnectAsync(pipeName);

        // IPublicAppInfoService.GetAppInfo() 是同步契约，生成的代理内部用 Task.Wait() 等回包。
        // 而 ConnectAsync 的延续跑在 dotnetCampus 自己的读循环线程上（栈可见 WaitForPeerConnectFinishedAsync），
        // 在这条线程上同步等回包 = 等一条只有当前线程才能投递的响应，必死锁。
        // 真实调用方从别的线程发起，所以这里也必须换线程，而不是掩盖成"测试超时"。
        var remoteInfo = await Task.Run(() => client.CreateProxy<IPublicAppInfoService>().GetAppInfo());
        Assert.Equal(appInfo.ApplicationName, remoteInfo.ApplicationName);
        Assert.Equal(appInfo.Version, remoteInfo.Version);
        Assert.Equal(appInfo.Codename, remoteInfo.Codename);

        var initialCatalog = await client.GetCatalogAsync();
        Assert.NotNull(initialCatalog);
        Assert.Contains(initialCatalog!.Services, service => service.ContractTypeName == typeof(IPublicAppInfoService).FullName);
        Assert.Contains(initialCatalog.Plugins, plugin => plugin.PluginId == "sample.plugin");

        host.RegisterPublicService<IPublicAirAppCatalogService>(new TestPublicPluginCatalogService(initialCatalog));
        var updatedCatalog = await catalogChanged.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Contains(updatedCatalog.Services, service => service.ContractTypeName == typeof(IPublicAirAppCatalogService).FullName);

        var sessionInfo = await client.GetSessionInfoAsync();
        Assert.NotNull(sessionInfo);
        Assert.Equal(pipeName, sessionInfo!.PipeName);
        Assert.Equal(IpcConstants.ProtocolVersion, sessionInfo.ProtocolVersion);
    }

    [Fact]
    public void AddAirAppPublicIpc_RegistersServiceDescriptor()
    {
        var services = new ServiceCollection();
        services.AddAirAppPublicIpc<ITestAirAppPublicService, TestAirAppPublicService>(
            objectId: "plugin-service",
            notifyIds: ["lanmountain.plugin.sample.updated"]);

        using var provider = services.BuildServiceProvider();
        var registration = Assert.Single(provider.GetServices<AirAppPublicIpcServiceRegistration>());
        Assert.Equal(typeof(ITestAirAppPublicService), registration.ContractType);
        Assert.Equal("plugin-service", registration.ObjectId);
        Assert.Contains("lanmountain.plugin.sample.updated", registration.NotifyIds);
    }

    private sealed class TestPublicAppInfoService : IPublicAppInfoService
    {
        private readonly PublicAppInfoSnapshot _snapshot;

        public TestPublicAppInfoService(PublicAppInfoSnapshot snapshot)
        {
            _snapshot = snapshot;
        }

        public PublicAppInfoSnapshot GetAppInfo()
        {
            return _snapshot;
        }
    }

    private sealed class TestPublicPluginCatalogService : IPublicAirAppCatalogService
    {
        private readonly PublicIpcCatalogSnapshot _snapshot;

        public TestPublicPluginCatalogService(PublicIpcCatalogSnapshot snapshot)
        {
            _snapshot = snapshot;
        }

        public PublicIpcCatalogSnapshot GetCatalog()
        {
            return _snapshot;
        }
    }

}

[dotnetCampus.Ipc.CompilerServices.Attributes.IpcPublic]
public interface ITestAirAppPublicService
{
    string Ping();
}

public sealed class TestAirAppPublicService : ITestAirAppPublicService
{
    public string Ping()
    {
        return "pong";
    }
}
