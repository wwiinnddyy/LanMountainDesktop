using Microsoft.Extensions.Hosting;

namespace LanMountainDesktop.AirAppSdk;

/// <summary>
/// Provides runtime context and services for an AirApp.
/// The host hands an implementation to <see cref="IAirApp.OnStartedAsync"/> and exposes it
/// through the per-AirApp service container.
/// </summary>
public interface IAirAppRuntimeContext
{
    /// <summary>
    /// Gets the manifest of this AirApp.
    /// </summary>
    AirAppManifest Manifest { get; }

    /// <summary>
    /// Gets the directory that contains the loaded AirApp package.
    /// </summary>
    string AirAppDirectory { get; }

    /// <summary>
    /// Gets the data directory for this AirApp.
    /// Use this directory to store persistent user data.
    /// </summary>
    string DataDirectory { get; }

    /// <summary>
    /// Gets the cache directory for this AirApp.
    /// Use this directory to store temporary cached data.
    /// </summary>
    string CacheDirectory { get; }

    /// <summary>
    /// Gets the service provider for dependency injection.
    /// </summary>
    IServiceProvider Services { get; }

    /// <summary>
    /// Gets a snapshot of the host-provided properties for this AirApp.
    /// </summary>
    IReadOnlyDictionary<string, object?> Properties { get; }

    /// <summary>
    /// Gets the host application lifetime manager.
    /// </summary>
    IHostApplicationLifetime Lifetime { get; }

    /// <summary>
    /// Gets the message bus for inter-AirApp communication.
    /// </summary>
    IAirAppMessageBus MessageBus { get; }

    /// <summary>
    /// Gets the appearance context for theme and styling.
    /// </summary>
    IAirAppAppearanceContext Appearance { get; }

    /// <summary>
    /// Gets the logger for this AirApp.
    /// </summary>
    IAirAppLogger Logger { get; }

    /// <summary>
    /// Resolve a service from the AirApp service provider.
    /// </summary>
    T? GetService<T>();

    /// <summary>
    /// Try to read a host-provided property.
    /// </summary>
    bool TryGetProperty<T>(string key, out T? value);

    /// <summary>
    /// Opens a window defined by this AirApp.
    /// </summary>
    /// <param name="windowId">Window identifier</param>
    /// <returns>The opened window instance</returns>
    Task<IAirAppWindow> OpenWindowAsync(string windowId);

    /// <summary>
    /// Closes a window by its identifier.
    /// </summary>
    /// <param name="windowId">Window identifier</param>
    void CloseWindow(string windowId);

    /// <summary>
    /// Register a desktop component (internal use by <see cref="AirAppBase"/>).
    /// </summary>
    void RegisterComponent(AirAppComponentOptions options);

    /// <summary>
    /// Register a window (internal use by <see cref="AirAppBase"/>).
    /// </summary>
    void RegisterWindow(string id, string name, Type windowType);

    /// <summary>
    /// Register a service (internal use by <see cref="AirAppBase"/>).
    /// </summary>
    void RegisterService<TService, TImplementation>()
        where TService : class
        where TImplementation : class, TService;
}
