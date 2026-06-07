namespace LanMountainDesktop.AirAppSdk;

/// <summary>
/// Context provided to an AirApp desktop component instance.
/// </summary>
public interface IAirAppComponentContext
{
    /// <summary>
    /// Gets the component identifier.
    /// </summary>
    string ComponentId { get; }

    /// <summary>
    /// Gets the unique placement identifier for this component instance.
    /// </summary>
    string PlacementId { get; }

    /// <summary>
    /// Gets the current width in grid cells.
    /// </summary>
    int Width { get; }

    /// <summary>
    /// Gets the current height in grid cells.
    /// </summary>
    int Height { get; }

    /// <summary>
    /// Gets the service provider for this component.
    /// </summary>
    IServiceProvider Services { get; }

    /// <summary>
    /// Gets the appearance context.
    /// </summary>
    IAirAppAppearanceContext Appearance { get; }

    /// <summary>
    /// Request a window to be opened.
    /// </summary>
    /// <param name="windowId">Window identifier</param>
    Task OpenWindowAsync(string windowId);

    /// <summary>
    /// Send a message to other components or AirApps.
    /// </summary>
    /// <param name="topic">Message topic</param>
    /// <param name="payload">Message payload</param>
    void SendMessage(string topic, object? payload = null);

    /// <summary>
    /// Subscribe to messages.
    /// </summary>
    /// <param name="topic">Message topic</param>
    /// <param name="handler">Message handler</param>
    /// <returns>Subscription token for unsubscribing</returns>
    IDisposable Subscribe(string topic, Action<object?> handler);
}
