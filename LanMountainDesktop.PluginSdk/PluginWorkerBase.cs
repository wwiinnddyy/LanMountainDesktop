using Microsoft.Extensions.DependencyInjection;

namespace LanMountainDesktop.PluginSdk;

public abstract class PluginWorkerBase : IPluginWorker
{
    public virtual void ConfigureServices(IPluginWorkerContext context, IServiceCollection services)
    {
    }

    public virtual Task StartAsync(IPluginWorkerContext context, IServiceProvider services, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public virtual Task StopAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
