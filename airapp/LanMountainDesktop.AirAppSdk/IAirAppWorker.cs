using Microsoft.Extensions.DependencyInjection;

namespace LanMountainDesktop.AirAppSdk;

public interface IAirAppWorker
{
    void ConfigureServices(IAirAppWorkerContext context, IServiceCollection services);

    Task StartAsync(IAirAppWorkerContext context, IServiceProvider services, CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);
}
