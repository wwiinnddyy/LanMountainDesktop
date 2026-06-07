namespace LanMountainDesktop.AirAppSdk;

/// <summary>
/// Message bus for inter-AirApp communication.
/// </summary>
public interface IAirAppMessageBus
{
    /// <summary>
    /// Publish a message to a topic.
    /// </summary>
    /// <param name="topic">Message topic</param>
    /// <param name="payload">Message payload</param>
    void Publish(string topic, object? payload = null);

    /// <summary>
    /// Subscribe to a topic.
    /// </summary>
    /// <param name="topic">Message topic</param>
    /// <param name="handler">Message handler</param>
    /// <returns>Subscription token</returns>
    IDisposable Subscribe(string topic, Action<object?> handler);

    /// <summary>
    /// Subscribe to a topic with typed payload.
    /// </summary>
    /// <typeparam name="T">Payload type</typeparam>
    /// <param name="topic">Message topic</param>
    /// <param name="handler">Typed message handler</param>
    /// <returns>Subscription token</returns>
    IDisposable Subscribe<T>(string topic, Action<T?> handler);
}
