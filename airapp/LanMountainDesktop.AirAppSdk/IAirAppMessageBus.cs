namespace LanMountainDesktop.AirAppSdk;

/// <summary>
/// Message bus for inter-AirApp communication.
/// Supports both strong-typed messages and topic-based routing.
/// </summary>
public interface IAirAppMessageBus
{
    /// <summary>
    /// Subscribe to a strong-typed message.
    /// </summary>
    IDisposable Subscribe<TMessage>(Action<TMessage> handler);

    /// <summary>
    /// Publish a strong-typed message.
    /// </summary>
    void Publish<TMessage>(TMessage message);

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
    /// Subscribe to a topic with a typed payload.
    /// </summary>
    /// <typeparam name="T">Payload type</typeparam>
    /// <param name="topic">Message topic</param>
    /// <param name="handler">Typed message handler</param>
    /// <returns>Subscription token</returns>
    IDisposable Subscribe<T>(string topic, Action<T?> handler);
}
