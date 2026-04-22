using System.Text.Json;
using System.Text.Json.Serialization;

namespace LanMountainDesktop.PluginIsolation.Ipc;

public sealed class PluginIpcClient
{
    public PluginIpcClient(PluginIpcClientOptions options)
    {
        Options = options ?? throw new ArgumentNullException(nameof(options));
        SerializerContext = options.SerializerContext ?? throw new ArgumentNullException(nameof(options.SerializerContext));
        SerializerOptions = SerializerContext.Options;
    }

    public PluginIpcClientOptions Options { get; }

    public JsonSerializerContext SerializerContext { get; }

    public JsonSerializerOptions SerializerOptions { get; }

    public PluginIpcRequestDispatcher? RequestDispatcher { get; set; }

    public PluginIpcNotificationDispatcher? NotificationDispatcher { get; set; }

    public Task<TResponse?> RequestAsync<TRequest, TResponse>(
        string route,
        TRequest payload,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(route);
        return RequestCoreAsync<TRequest, TResponse>(route, payload, cancellationToken);
    }

    public Task NotifyAsync<TPayload>(
        string route,
        TPayload payload,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(route);
        return NotifyCoreAsync(route, Serialize(payload), cancellationToken);
    }

    private async Task<TResponse?> RequestCoreAsync<TRequest, TResponse>(
        string route,
        TRequest payload,
        CancellationToken cancellationToken)
    {
        if (RequestDispatcher is null)
        {
            throw new NotSupportedException(
                "PluginIpcClient is not yet bound to a dotnetCampus.Ipc transport dispatcher. " +
                "Wire RequestDispatcher during host/worker transport integration.");
        }

        var response = await RequestDispatcher(route, Serialize(payload), cancellationToken).ConfigureAwait(false);
        if (response is null)
        {
            return default;
        }

        return Deserialize<TResponse>(response);
    }

    private async Task NotifyCoreAsync(string route, JsonElement? payload, CancellationToken cancellationToken)
    {
        if (NotificationDispatcher is null)
        {
            throw new NotSupportedException(
                "PluginIpcClient is not yet bound to a dotnetCampus.Ipc transport dispatcher. " +
                "Wire NotificationDispatcher during host/worker transport integration.");
        }

        await NotificationDispatcher(route, payload, cancellationToken).ConfigureAwait(false);
    }

    private JsonElement Serialize<T>(T payload)
    {
        return JsonSerializer.SerializeToElement(payload, SerializerOptions);
    }

    private T? Deserialize<T>(JsonElement? payload)
    {
        if (payload is null)
        {
            return default;
        }

        return payload.Value.Deserialize<T>(SerializerOptions);
    }
}
