namespace LanMountainDesktop.AirAppIsolation.Ipc;

/// <summary>
/// 进程内 IPC 传输。将 <see cref="AirAppIpcClient"/> 的调度委托直接绑定到
/// <see cref="AirAppIpcServer"/> 的处理入口，不经过命名管道。
/// 用于不支持子进程/命名管道的平台（如 Android），插件契约与序列化路径保持不变：
/// 请求仍会经过 JsonElement 序列化/反序列化边界，与管道传输行为一致。
/// </summary>
public sealed class InProcAirAppIpcTransport : IDisposable
{
    private readonly AirAppIpcClient _client;
    private volatile bool _disposed;

    private InProcAirAppIpcTransport(AirAppIpcClient client, AirAppIpcServer server)
    {
        _client = client;
        Server = server;

        client.RequestDispatcher = (route, payload, cancellationToken) =>
        {
            ThrowIfDisposed();
            return server.HandleRequestAsync(route, payload, cancellationToken);
        };

        client.NotificationDispatcher = (route, payload, cancellationToken) =>
        {
            ThrowIfDisposed();
            return server.HandleNotificationAsync(route, payload, cancellationToken);
        };
    }

    /// <summary>
    /// 绑定后的服务端。
    /// </summary>
    public AirAppIpcServer Server { get; }

    /// <summary>
    /// 绑定后的客户端。请求/通知将直接分发到 <see cref="Server"/>。
    /// </summary>
    public AirAppIpcClient Client => _client;

    /// <summary>
    /// 将客户端与服务端以进程内直通方式连接。
    /// </summary>
    /// <param name="client">插件侧客户端</param>
    /// <param name="server">宿主侧服务端</param>
    /// <returns>传输句柄；Dispose 后客户端调度将抛出 <see cref="ObjectDisposedException"/>。</returns>
    public static InProcAirAppIpcTransport Connect(AirAppIpcClient client, AirAppIpcServer server)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(server);
        return new InProcAirAppIpcTransport(client, server);
    }

    /// <summary>
    /// 断开传输。之后客户端的请求/通知将失败。
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _client.RequestDispatcher = null;
        _client.NotificationDispatcher = null;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
