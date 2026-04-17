using System.Buffers;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text.Json;
using LanMountainDesktop.Shared.Contracts.Launcher;

namespace LanMountainDesktop.Services.Launcher;

/// <summary>
/// Launcher IPC 客户端 - 向 Launcher 报告启动进度
/// 采用持久连接 + 长度前缀协议，在同一连接上可多次发送消息。
/// 跨平台实现：Windows 使用命名管道，Linux/macOS 使用 Unix 域套接字
/// </summary>
public class LauncherIpcClient : IDisposable
{
    private NamedPipeClientStream? _pipeClient;
    private bool _isConnected;
    private readonly object _writeLock = new();

    /// <summary>
    /// 协议：每条消息以 4 字节小端 int32 长度前缀开头，后跟 UTF-8 JSON 正文。
    /// </summary>
    private const int LengthPrefixSize = 4;

    /// <summary>
    /// 连接到 Launcher 的 IPC 服务端
    /// </summary>
    public async Task<bool> ConnectAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _pipeClient = new NamedPipeClientStream(
                ".",
                LauncherIpcConstants.PipeName,
                PipeDirection.Out);

            await _pipeClient.ConnectAsync(5000, cancellationToken);
            _isConnected = true;
            return true;
        }
        catch (TimeoutException)
        {
            // Launcher 可能没有启动 IPC 服务端，这是正常的
            return false;
        }
        catch (Exception ex)
        {
            AppLogger.Warn("LauncherIpc", $"Failed to connect to Launcher IPC: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 报告启动进度（在同一连接上可多次调用）
    /// </summary>
    public async Task ReportProgressAsync(StartupProgressMessage message)
    {
        if (!_isConnected || _pipeClient?.IsConnected != true)
            return;

        try
        {
            var json = JsonSerializer.Serialize(message);
            var payload = System.Text.Encoding.UTF8.GetBytes(json);

            // 长度前缀协议：[4字节长度][消息正文]
            var lengthPrefix = BitConverter.GetBytes(payload.Length);
            Debug.Assert(lengthPrefix.Length == LengthPrefixSize);

            // 加锁保证单条消息的长度前缀和正文原子写入
            lock (_writeLock)
            {
                _pipeClient.Write(lengthPrefix, 0, LengthPrefixSize);
                _pipeClient.Write(payload, 0, payload.Length);
                _pipeClient.Flush();
            }

            // 将同步写入包装为已完成的 Task
            await Task.CompletedTask;
        }
        catch (IOException)
        {
            // 管道断开
            _isConnected = false;
        }
        catch (Exception ex)
        {
            AppLogger.Warn("LauncherIpc", $"Failed to report progress: {ex.Message}");
            _isConnected = false;
        }
    }

    /// <summary>
    /// 检查是否从 Launcher 启动
    /// </summary>
    public static bool IsLaunchedByLauncher()
    {
        return !string.IsNullOrEmpty(
            Environment.GetEnvironmentVariable(LauncherIpcConstants.LauncherPidEnvVar));
    }

    public void Dispose()
    {
        _isConnected = false;
        _pipeClient?.Dispose();
    }
}
