using System.Buffers;
using System.IO.Pipes;
using System.Text.Json;
using LanMountainDesktop.Shared.Contracts.Launcher;

namespace LanMountainDesktop.Launcher.Services.Ipc;

/// <summary>
/// Launcher IPC 服务端 - 接收主程序的启动进度报告
/// 采用持久连接 + 长度前缀协议，支持客户端在同一连接上多次发送消息。
/// 跨平台实现：Windows 使用命名管道，Linux/macOS 使用 Unix 域套接字
/// </summary>
public class LauncherIpcServer : IDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private readonly Action<StartupProgressMessage> _onProgress;
    private Task? _listenTask;
    private NamedPipeServerStream? _currentPipe;

    /// <summary>
    /// 协议：每条消息以 4 字节小端 int32 长度前缀开头，后跟 UTF-8 JSON 正文。
    /// 这在 Windows Message 模式和 unix Byte 模式下均能可靠工作。
    /// </summary>
    private const int LengthPrefixSize = 4;

    private const int BackoffBaseMs = 200;
    private const int BackoffMaxMs = 5000;
    private const int BackoffJitterMs = 100;

    public LauncherIpcServer(Action<StartupProgressMessage> onProgress)
    {
        _onProgress = onProgress;
    }

    /// <summary>
    /// 启动 IPC 服务端监听
    /// </summary>
    public void Start()
    {
        _listenTask = Task.Run(ListenLoopAsync, _cts.Token);
    }

    private async Task ListenLoopAsync()
    {
        var consecutiveErrors = 0;

        while (!_cts.Token.IsCancellationRequested)
        {
            NamedPipeServerStream? pipe = null;
            try
            {
                pipe = new NamedPipeServerStream(
                    LauncherIpcConstants.PipeName,
                    PipeDirection.In,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                _currentPipe = pipe;
                await pipe.WaitForConnectionAsync(_cts.Token);

                consecutiveErrors = 0;

                await ReadMessagesFromConnectionAsync(pipe, _cts.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (IOException)
            {
                consecutiveErrors = 0;
                continue;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception ex)
            {
                consecutiveErrors++;
                var delay = ComputeBackoff(consecutiveErrors);
                Console.Error.WriteLine($"IPC listen error (attempt {consecutiveErrors}), retrying in {delay}ms: {ex.Message}");
                try
                {
                    await Task.Delay(delay, _cts.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
            finally
            {
                try
                {
                    pipe?.Dispose();
                }
                catch { }

                if (ReferenceEquals(_currentPipe, pipe))
                {
                    _currentPipe = null;
                }
            }
        }
    }

    private int ComputeBackoff(int attempt)
    {
        var exponential = BackoffBaseMs * (1 << Math.Min(attempt - 1, 5));
        var capped = Math.Min(exponential, BackoffMaxMs);
        var jitter = Random.Shared.Next(0, BackoffJitterMs);
        return capped + jitter;
    }

    /// <summary>
    /// 从已连接的管道中持续读取消息，直到连接断开或取消
    /// </summary>
    private async Task ReadMessagesFromConnectionAsync(NamedPipeServerStream pipe, CancellationToken cancellationToken)
    {
        var lengthBuffer = ArrayPool<byte>.Shared.Rent(LengthPrefixSize);
        try
        {
            while (pipe.IsConnected && !cancellationToken.IsCancellationRequested)
            {
                // 1. 读取 4 字节长度前缀
                var totalRead = 0;
                while (totalRead < LengthPrefixSize)
                {
                    var read = await pipe.ReadAsync(lengthBuffer.AsMemory(totalRead, LengthPrefixSize - totalRead), cancellationToken);
                    if (read == 0)
                    {
                        // 连接已关闭
                        return;
                    }
                    totalRead += read;
                }

                var payloadLength = BitConverter.ToInt32(lengthBuffer, 0);
                if (payloadLength <= 0 || payloadLength > 1024 * 1024) // 最大 1MB 单条消息
                {
                    // 无效长度，跳过此连接
                    return;
                }

                // 2. 读取消息正文
                var payloadBuffer = ArrayPool<byte>.Shared.Rent(payloadLength);
                try
                {
                    totalRead = 0;
                    while (totalRead < payloadLength)
                    {
                        var read = await pipe.ReadAsync(payloadBuffer.AsMemory(totalRead, payloadLength - totalRead), cancellationToken);
                        if (read == 0)
                        {
                            return;
                        }
                        totalRead += read;
                    }

                    // 3. 反序列化并回调
                    var json = System.Text.Encoding.UTF8.GetString(payloadBuffer, 0, payloadLength);
                    var message = JsonSerializer.Deserialize(json, AppJsonContext.Default.StartupProgressMessage);
                    if (message is not null)
                    {
                        _onProgress(message);
                    }
                }
                catch (JsonException)
                {
                    // 忽略解析错误，继续读取下一条消息
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(payloadBuffer);
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(lengthBuffer);
        }
    }

    /// <summary>
    /// 停止 IPC 服务端
    /// </summary>
    public void Stop()
    {
        _cts.Cancel();
        try
        {
            _currentPipe?.Dispose();
        }
        catch { }
    }

    public void Dispose()
    {
        Stop();
        _cts.Dispose();

        try
        {
            _listenTask?.Wait(TimeSpan.FromSeconds(2));
        }
        catch { }
    }
}
