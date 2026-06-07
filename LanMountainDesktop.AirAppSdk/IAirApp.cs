using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace LanMountainDesktop.AirAppSdk;

/// <summary>
/// Core interface for AirApp entry point.
/// </summary>
public interface IAirApp
{
    /// <summary>
    /// Initialize the AirApp and register services.
    /// Called during host startup before the application is fully running.
    /// </summary>
    /// <param name="context">Host builder context</param>
    /// <param name="services">Service collection for dependency injection</param>
    void Initialize(HostBuilderContext context, IServiceCollection services);

    /// <summary>
    /// Called after the host application has started.
    /// Use this for initialization that requires runtime services.
    /// </summary>
    /// <param name="context">AirApp runtime context</param>
    Task OnStartedAsync(IAirAppRuntimeContext context);

    /// <summary>
    /// Called when the host application is stopping.
    /// Use this for cleanup and resource disposal.
    /// </summary>
    Task OnStoppingAsync();
}
