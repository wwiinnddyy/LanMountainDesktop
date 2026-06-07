using Microsoft.Extensions.Hosting;

namespace LanMountainDesktop.AirAppSdk;

/// <summary>
/// Provides runtime context and services for an AirApp.
/// </summary>
public interface IAirAppRuntimeContext
{
    /// <summary>
    /// Gets the unique identifier of this AirApp.
    /// </summary>
    string AirAppId { get; }

    /// <summary>
    /// Gets the display name of this AirApp.
    /// </summary>
    string AirAppName { get; }

    /// <summary>
    /// Gets the AirApp version.
    /// </summary>
    string AirAppVersion { get; }

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
    /// Register a desktop component (internal use by AirAppBase).
    /// </summary>
    void RegisterComponent(AirAppComponentOptions options);

    /// <summary>
    /// Register a window (internal use by AirAppBase).
    /// </summary>
    void RegisterWindow(string id, string name, Type windowType);

    /// <summary>
    /// Register a service (internal use by AirAppBase).
    /// </summary>
    void RegisterService<TService, TImplementation>()
        where TService : class
        where TImplementation : class, TService;
}
