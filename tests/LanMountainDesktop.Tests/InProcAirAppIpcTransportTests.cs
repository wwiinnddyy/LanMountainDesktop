using System.Text.Json;
using LanMountainDesktop.AirAppIsolation.Ipc;
using Xunit;

namespace LanMountainDesktop.Tests;

/// <summary>
/// 进程内 IPC 传输测试：验证与管道传输相同的契约语义
/// （请求/响应往返、通知投递、错误传播、释放语义）。
/// </summary>
public sealed class InProcAirAppIpcTransportTests
{
    private static (AirAppIpcClient Client, AirAppIpcServer Server) CreatePair()
    {
        var server = new AirAppIpcServer(new AirAppIpcServerOptions
        {
            PipeName = "inproc-test"
        });
        var client = new AirAppIpcClient(new AirAppIpcClientOptions
        {
            PipeName = "inproc-test"
        });
        return (client, server);
    }

    [Fact]
    public async Task RequestAsync_RoundTripsThroughServerHandler()
    {
        var (client, server) = CreatePair();
        server.MapRequest<string, string>("test/echo", (payload, _) => Task.FromResult($"echo:{payload}"));
        using var transport = InProcAirAppIpcTransport.Connect(client, server);

        var response = await client.RequestAsync<string, string>("test/echo", "hello");

        Assert.Equal("echo:hello", response);
    }

    [Fact]
    public async Task NotifyAsync_DeliversPayloadToServerHandler()
    {
        var (client, server) = CreatePair();
        var received = new TaskCompletionSource<string?>();
        server.MapNotification<string>("test/notify", (payload, _) =>
        {
            received.TrySetResult(payload);
            return Task.CompletedTask;
        });
        using var transport = InProcAirAppIpcTransport.Connect(client, server);

        await client.NotifyAsync("test/notify", "ping");

        var payload = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("ping", payload);
    }

    [Fact]
    public async Task RequestAsync_WhenHandlerThrows_PropagatesException()
    {
        var (client, server) = CreatePair();
        server.MapRequest<string, string>("test/fail", (_, _) => throw new InvalidOperationException("handler failed"));
        using var transport = InProcAirAppIpcTransport.Connect(client, server);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.RequestAsync<string, string>("test/fail", "x"));
        Assert.Equal("handler failed", ex.Message);
    }

    [Fact]
    public async Task RequestAsync_WhenRouteNotRegistered_Throws()
    {
        var (client, server) = CreatePair();
        using var transport = InProcAirAppIpcTransport.Connect(client, server);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.RequestAsync<string, string>("test/missing", "x"));
    }

    [Fact]
    public async Task RequestAsync_AfterDispose_ThrowsObjectDisposed()
    {
        var (client, server) = CreatePair();
        server.MapRequest<string, string>("test/echo", (payload, _) => Task.FromResult(payload));
        var transport = InProcAirAppIpcTransport.Connect(client, server);
        transport.Dispose();

        // Dispose 后调度委托被摘除，客户端回落到未绑定状态。
        await Assert.ThrowsAsync<NotSupportedException>(
            () => client.RequestAsync<string, string>("test/echo", "x"));
    }

    [Fact]
    public async Task RequestAsync_WithCancellation_PassesTokenToHandler()
    {
        var (client, server) = CreatePair();
        using var cts = new CancellationTokenSource();
        server.MapRequest<string, string>("test/cancel", async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return "never";
        });
        using var transport = InProcAirAppIpcTransport.Connect(client, server);

        var task = client.RequestAsync<string, string>("test/cancel", "x", cts.Token);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
    }
}
