using System.Text.Json;
using System.Text.Json.Serialization;

namespace LanMountainDesktop.PluginIsolation.Ipc;

public sealed class PluginIpcServer
{
    private readonly Dictionary<string, Func<JsonElement?, CancellationToken, Task<JsonElement?>>> _requestHandlers =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, Func<JsonElement?, CancellationToken, Task>> _notificationHandlers =
        new(StringComparer.OrdinalIgnoreCase);

    public PluginIpcServer(PluginIpcServerOptions options)
    {
        Options = options ?? throw new ArgumentNullException(nameof(options));
        SerializerContext = options.SerializerContext ?? throw new ArgumentNullException(nameof(options.SerializerContext));
        SerializerOptions = SerializerContext.Options;
    }

    public PluginIpcServerOptions Options { get; }

    public JsonSerializerContext SerializerContext { get; }

    public JsonSerializerOptions SerializerOptions { get; }

    public void MapRequest<TRequest, TResponse>(
        string route,
        Func<TRequest, CancellationToken, Task<TResponse>> handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(route);
        ArgumentNullException.ThrowIfNull(handler);

        _requestHandlers[route] = async (payload, cancellationToken) =>
        {
            var request = Deserialize<TRequest>(payload);
            var response = await handler(request, cancellationToken).ConfigureAwait(false);
            return Serialize(response);
        };
    }

    public void MapNotification<TPayload>(
        string route,
        Func<TPayload, CancellationToken, Task> handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(route);
        ArgumentNullException.ThrowIfNull(handler);

        _notificationHandlers[route] = (payload, cancellationToken) =>
        {
            var notification = Deserialize<TPayload>(payload);
            return handler(notification, cancellationToken);
        };
    }

    public async Task<JsonElement?> HandleRequestAsync(
        string route,
        JsonElement? payload,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(route);

        if (!_requestHandlers.TryGetValue(route, out var handler))
        {
            throw new InvalidOperationException($"No IPC request handler is registered for route '{route}'.");
        }

        return await handler(payload, cancellationToken).ConfigureAwait(false);
    }

    public Task HandleNotificationAsync(
        string route,
        JsonElement? payload,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(route);

        if (!_notificationHandlers.TryGetValue(route, out var handler))
        {
            throw new InvalidOperationException($"No IPC notification handler is registered for route '{route}'.");
        }

        return handler(payload, cancellationToken);
    }

    private JsonElement Serialize<T>(T payload)
    {
        return JsonSerializer.SerializeToElement(payload, SerializerOptions);
    }

    private T Deserialize<T>(JsonElement? payload)
    {
        if (payload is null)
        {
            if (default(T) is null)
            {
                return default!;
            }

            throw new InvalidOperationException(
                $"IPC payload is required for '{typeof(T).FullName}', but the caller provided no payload.");
        }

        var value = payload.Value.Deserialize<T>(SerializerOptions);
        if (value is null && default(T) is not null)
        {
            throw new InvalidOperationException(
                $"Failed to deserialize IPC payload to '{typeof(T).FullName}'.");
        }

        return value!;
    }
}
