using System.IO.Pipes;
using System.Text.Json;
using LanMountainDesktop.Shared.Contracts.Launcher;

namespace LanMountainDesktop.Launcher.Services.Ipc;

/// <summary>
/// Launcher IPC 服务端 - 接收主程序的启动进度报告
/// </summary>
public class LauncherIpcServer : IDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private NamedPipeServerStream? _pipeServer;
    private readonly Action<StartupProgressMessage> _onProgress;
    private Task? _listenTask;
    
    public LauncherIpcServer(Action<StartupProgressMessage> onProgress)
    {
        _onProgress = onProgress;
    }
    
    /// <summary>
    /// 启动 IPC 服务端监听
    /// </summary>
    public void Start()
    {
        _listenTask = Task.Run(async () =>
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                try
                {
                    _pipeServer = new NamedPipeServerStream(
                        LauncherIpcConstants.PipeName,
                        PipeDirection.In,
                        1,
                        PipeTransmissionMode.Message);
                    
                    await _pipeServer.WaitForConnectionAsync(_cts.Token);
                    
                    using var reader = new StreamReader(_pipeServer);
                    var json = await reader.ReadToEndAsync(_cts.Token);
                    
                    if (!string.IsNullOrEmpty(json))
                    {
                        try
                        {
                            var message = JsonSerializer.Deserialize<StartupProgressMessage>(json);
                            if (message != null)
                            {
                                _onProgress(message);
                            }
                        }
                        catch (JsonException)
                        {
                            // 忽略解析错误
                        }
                    }
                    
                    try
                    {
                        _pipeServer.Disconnect();
                    }
                    catch { }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (IOException)
                {
                    // 管道断开，继续监听
                    continue;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"IPC error: {ex.Message}");
                    await Task.Delay(100, _cts.Token);
                }
            }
        }, _cts.Token);
    }
    
    /// <summary>
    /// 停止 IPC 服务端
    /// </summary>
    public void Stop()
    {
        _cts.Cancel();
        try
        {
            _pipeServer?.Disconnect();
        }
        catch { }
    }
    
    public void Dispose()
    {
        Stop();
        _pipeServer?.Dispose();
        _cts.Dispose();
        
        try
        {
            _listenTask?.Wait(TimeSpan.FromSeconds(2));
        }
        catch { }
    }
}
