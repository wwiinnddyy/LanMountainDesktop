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
    private const int ConnectTimeoutMs = 5000;
    private const int ConnectRetryCount = 3;
    private const int ConnectRetryBaseDelayMs = 300;

    private NamedPipeClientStream? _pipeClient;
    private bool _isConnected;
    private readonly object _writeLock = new();

    public bool IsConnected => _isConnected && _pipeClient?.IsConnected == true;

    public async Task<bool> ConnectAsync(CancellationToken cancellationToken = default)
    {
        for (var attempt = 1; attempt <= ConnectRetryCount; attempt++)
        {
            try
            {
                var client = new NamedPipeClientStream(
                    ".",
                    LauncherIpcConstants.PipeName,
                    PipeDirection.Out,
                    PipeOptions.Asynchronous);

                await client.ConnectAsync(ConnectTimeoutMs, cancellationToken);
                _pipeClient = client;
                _isConnected = true;
                return true;
            }
            catch (TimeoutException)
            {
                _pipeClient?.Dispose();
                _pipeClient = null;

                if (attempt < ConnectRetryCount)
                {
                    var delay = ConnectRetryBaseDelayMs * attempt + Random.Shared.Next(0, 100);
                    try
                    {
                        await Task.Delay(delay, cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        return false;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch (Exception ex)
            {
                _pipeClient?.Dispose();
                _pipeClient = null;

                if (attempt < ConnectRetryCount)
                {
                    AppLogger.Warn("LauncherIpc", $"Connect attempt {attempt} failed: {ex.Message}, retrying...");
                    var delay = ConnectRetryBaseDelayMs * attempt + Random.Shared.Next(0, 100);
                    try
                    {
                        await Task.Delay(delay, cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        return false;
                    }
                }
                else
                {
                    AppLogger.Warn("LauncherIpc", $"Failed to connect to Launcher IPC after {ConnectRetryCount} attempts: {ex.Message}");
                }
            }
        }

        return false;
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

            var buffer = ArrayPool<byte>.Shared.Rent(LengthPrefixSize + payload.Length);
            try
            {
                Buffer.BlockCopy(lengthPrefix, 0, buffer, 0, LengthPrefixSize);
                Buffer.BlockCopy(payload, 0, buffer, LengthPrefixSize, payload.Length);

                await _pipeClient.WriteAsync(buffer.AsMemory(0, LengthPrefixSize + payload.Length)).ConfigureAwait(false);
                await _pipeClient.FlushAsync().ConfigureAwait(false);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
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
