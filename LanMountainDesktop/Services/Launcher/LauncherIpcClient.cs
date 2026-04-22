using System.Buffers;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text.Json;
using LanMountainDesktop.Shared.Contracts.Launcher;

namespace LanMountainDesktop.Services.Launcher;

/// <summary>
/// Launcher IPC 客户端，用于向 Launcher 报告启动进度。
/// </summary>
public class LauncherIpcClient : IDisposable
{
    private static readonly JsonSerializerOptions StartupProgressJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private const int LengthPrefixSize = 4;

    private NamedPipeClientStream? _pipeClient;
    private bool _isConnected;
    private readonly object _writeLock = new();

    public bool IsConnected => _isConnected && _pipeClient?.IsConnected == true;

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
            return false;
        }
        catch (Exception ex)
        {
            AppLogger.Warn("LauncherIpc", $"Failed to connect to Launcher IPC: {ex.Message}");
            return false;
        }
    }

    public async Task ReportProgressAsync(StartupProgressMessage message)
    {
        if (!_isConnected || _pipeClient?.IsConnected != true)
        {
            return;
        }

        try
        {
            var json = JsonSerializer.Serialize(message, StartupProgressJsonOptions);
            var payload = System.Text.Encoding.UTF8.GetBytes(json);
            var lengthPrefix = BitConverter.GetBytes(payload.Length);
            Debug.Assert(lengthPrefix.Length == LengthPrefixSize);

            lock (_writeLock)
            {
                _pipeClient.Write(lengthPrefix, 0, LengthPrefixSize);
                _pipeClient.Write(payload, 0, payload.Length);
                _pipeClient.Flush();
            }

            await Task.CompletedTask;
        }
        catch (IOException)
        {
            _isConnected = false;
        }
        catch (Exception ex)
        {
            AppLogger.Warn("LauncherIpc", $"Failed to report progress: {ex.Message}");
            _isConnected = false;
        }
    }

    public static bool IsLaunchedByLauncher()
    {
        return LauncherRuntimeMetadata.GetLauncherProcessId(Environment.GetCommandLineArgs()) is not null;
    }

    public void Dispose()
    {
        _isConnected = false;
        _pipeClient?.Dispose();
    }
}
