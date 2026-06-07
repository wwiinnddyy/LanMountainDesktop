using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace LanMountainDesktop.AirAppSdk;

/// <summary>
/// Base class for AirApp implementations.
/// Inherit from this class and apply the [AirAppEntrance] attribute.
/// </summary>
public abstract class AirAppBase : IAirApp
{
    /// <summary>
    /// Gets the runtime context after the AirApp has started.
    /// Available after OnStartedAsync is called.
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

    /// <summary>
    /// Register a desktop component widget.
    /// </summary>
    /// <typeparam name="TWidget">Widget implementation type</typeparam>
    /// <param name="id">Unique component identifier</param>
    /// <param name="name">Display name</param>
    /// <param name="configure">Optional configuration</param>
    protected void RegisterComponent<TWidget>(
        string id,
        string name,
        Action<AirAppComponentOptions>? configure = null)
        where TWidget : class, IAirAppWidget
    {
        if (RuntimeContext == null)
        {
            throw new InvalidOperationException(
                "RegisterComponent can only be called after OnStartedAsync. " +
                "Use IServiceCollection extension methods in Initialize() instead.");
        }

        var options = new AirAppComponentOptions
        {
            Id = id,
            Name = name,
            WidgetType = typeof(TWidget)
        };

        configure?.Invoke(options);

        // Delegate to runtime context
        RuntimeContext.RegisterComponent(options);
    }

    /// <summary>
    /// Register a window.
    /// </summary>
    /// <typeparam name="TWindow">Window implementation type</typeparam>
    /// <param name="id">Unique window identifier</param>
    /// <param name="name">Display name</param>
    protected void RegisterWindow<TWindow>(string id, string name)
        where TWindow : class, IAirAppWindow
    {
        if (RuntimeContext == null)
        {
            throw new InvalidOperationException(
                "RegisterWindow can only be called after OnStartedAsync.");
        }

        RuntimeContext.RegisterWindow(id, name, typeof(TWindow));
    }

    /// <summary>
    /// Register a service in the DI container.
    /// </summary>
    /// <typeparam name="TService">Service interface</typeparam>
    /// <typeparam name="TImplementation">Implementation type</typeparam>
    protected void RegisterService<TService, TImplementation>()
        where TService : class
        where TImplementation : class, TService
    {
        if (RuntimeContext == null)
        {
            throw new InvalidOperationException(
                "RegisterService can only be called after OnStartedAsync. " +
                "Use IServiceCollection in Initialize() instead.");
        }

        RuntimeContext.RegisterService<TService, TImplementation>();
    }
}
