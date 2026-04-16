using System.IO.Pipes;
using System.Text.Json;
using LanMountainDesktop.Shared.Contracts.Launcher;

namespace LanMountainDesktop.Services.Launcher;

/// <summary>
/// Launcher IPC 客户端 - 向 Launcher 报告启动进度
/// </summary>
public class LauncherIpcClient : IDisposable
{
    private NamedPipeClientStream? _pipeClient;
    private bool _isConnected;
    
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
    /// 报告启动进度
    /// </summary>
    public async Task ReportProgressAsync(StartupProgressMessage message)
    {
        if (!_isConnected || _pipeClient?.IsConnected != true)
            return;
        
        try
        {
            var json = JsonSerializer.Serialize(message);
            using var writer = new StreamWriter(_pipeClient, leaveOpen: true);
            await writer.WriteAsync(json);
            await writer.FlushAsync();
        }
        catch (IOException)
        {
            // 管道断开
            _isConnected = false;
        }
        catch (Exception ex)
        {
            AppLogger.Warn("LauncherIpc", $"Failed to report progress: {ex.Message}");
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
        _pipeClient?.Dispose();
    }
}
