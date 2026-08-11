using LanMountainDesktop.PluginSdk;
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
        host.PluginDescriptorProvider = () =>
        [
            new PublicPluginDescriptor("sample.plugin", "Sample Plugin", "1.0.0", true, true)
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

        var proxy = client.CreateProxy<IPublicAppInfoService>();
        var remoteInfo = proxy.GetAppInfo();
        Assert.Equal(appInfo.ApplicationName, remoteInfo.ApplicationName);
        Assert.Equal(appInfo.Version, remoteInfo.Version);
        Assert.Equal(appInfo.Codename, remoteInfo.Codename);

        var initialCatalog = await client.GetCatalogAsync();
        Assert.NotNull(initialCatalog);
        Assert.Contains(initialCatalog!.Services, service => service.ContractTypeName == typeof(IPublicAppInfoService).FullName);
        Assert.Contains(initialCatalog.Plugins, plugin => plugin.PluginId == "sample.plugin");

        host.RegisterPublicService<IPublicPluginCatalogService>(new TestPublicPluginCatalogService(initialCatalog));
        var updatedCatalog = await catalogChanged.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Contains(updatedCatalog.Services, service => service.ContractTypeName == typeof(IPublicPluginCatalogService).FullName);

        var sessionInfo = await client.GetSessionInfoAsync();
        Assert.NotNull(sessionInfo);
        Assert.Equal(pipeName, sessionInfo!.PipeName);
        Assert.Equal(IpcConstants.ProtocolVersion, sessionInfo.ProtocolVersion);
    }

    [Fact]
    public void AddPluginPublicIpc_RegistersServiceDescriptor()
    {
        var services = new ServiceCollection();
        services.AddPluginPublicIpc<ITestPluginPublicService, TestPluginPublicService>(
            objectId: "plugin-service",
            notifyIds: ["lanmountain.plugin.sample.updated"]);

        using var provider = services.BuildServiceProvider();
        var registration = Assert.Single(provider.GetServices<PluginPublicIpcServiceRegistration>());
        Assert.Equal(typeof(ITestPluginPublicService), registration.ContractType);
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

    private sealed class TestPublicPluginCatalogService : IPublicPluginCatalogService
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
public interface ITestPluginPublicService
{
    string Ping();
}

public sealed class TestPluginPublicService : ITestPluginPublicService
{
    public string Ping()
    {
        return "pong";
    }
}
