using Microsoft.Extensions.DependencyInjection;

namespace LanMountainDesktop.AirAppSdk;

public abstract class AirAppWorkerBase : IAirAppWorker
{
    public virtual void ConfigureServices(IAirAppWorkerContext context, IServiceCollection services)
    {
    }

    public virtual Task StartAsync(IAirAppWorkerContext context, IServiceProvider services, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public virtual Task StopAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
