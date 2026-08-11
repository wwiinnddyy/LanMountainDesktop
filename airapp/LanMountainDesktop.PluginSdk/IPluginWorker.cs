using Microsoft.Extensions.DependencyInjection;

namespace LanMountainDesktop.PluginSdk;

public interface IPluginWorker
{
    void ConfigureServices(IPluginWorkerContext context, IServiceCollection services);

    Task StartAsync(IPluginWorkerContext context, IServiceProvider services, CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);
}
