using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace LanMountainDesktop.AirAppSdk;

/// <summary>
/// Base class for AirApp implementations.
/// Inherit from this class and apply the <see cref="AirAppEntranceAttribute"/> attribute.
/// </summary>
/// <remarks>
/// Register everything the AirApp contributes — desktop components, windows, settings
/// sections, exports and background services — from <see cref="Initialize"/> using the
/// <see cref="AirAppServiceCollectionExtensions"/> methods. The host reads those
/// registrations out of the service collection once initialization completes.
/// </remarks>
public abstract class AirAppBase : IAirApp
{
    /// <summary>
    /// Gets the runtime context after the AirApp has started.
    /// Available after <see cref="OnStartedAsync"/> is called.
    /// </summary>
    protected IAirAppRuntimeContext? RuntimeContext { get; private set; }

    /// <summary>
    /// Initialize the AirApp and register services.
    /// Override this method to register your components, windows, and services.
    /// </summary>
    /// <param name="context">Host builder context</param>
    /// <param name="services">Service collection</param>
    public virtual void Initialize(HostBuilderContext context, IServiceCollection services)
    {
        // Default implementation: do nothing
        // Derived classes can override to register services
    }

    /// <summary>
    /// Called after the host application has started.
    /// Override this for runtime initialization.
    /// </summary>
    /// <param name="context">AirApp runtime context</param>
    public virtual Task OnStartedAsync(IAirAppRuntimeContext context)
    {
        RuntimeContext = context;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Called when the host application is stopping.
    /// Override this for cleanup logic.
    /// </summary>
    public virtual Task OnStoppingAsync()
    {
        return Task.CompletedTask;
    }
}
