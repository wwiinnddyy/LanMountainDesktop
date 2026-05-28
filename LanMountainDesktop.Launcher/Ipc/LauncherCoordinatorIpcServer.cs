using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.IO.Pipes;
using LanMountainDesktop.Launcher.Models;

namespace LanMountainDesktop.Launcher.Ipc;

internal sealed class LauncherCoordinatorIpcServer : IDisposable
{
    private const int LengthPrefixSize = 4;
    private const int MaxPayloadLength = 1024 * 1024;
    private const int BackoffBaseMs = 250;
    private const int BackoffMaxMs = 8000;
    private const int BackoffJitterMs = 150;
    private readonly string _pipeName;
    private readonly Func<LauncherCoordinatorRequest, LauncherCoordinatorStatus, Task<LauncherCoordinatorResponse>> _requestHandler;
    private readonly Action<LauncherCoordinatorStatus> _heartbeatHandler;
    private readonly CancellationTokenSource _cts = new();
    private readonly object _statusGate = new();
    private LauncherCoordinatorStatus _status;
    private Task? _listenTask;
    private Task? _heartbeatTask;

    public LauncherCoordinatorIpcServer(
        string pipeName,
        LauncherCoordinatorStatus initialStatus,
        Func<LauncherCoordinatorRequest, LauncherCoordinatorStatus, Task<LauncherCoordinatorResponse>> requestHandler,
        Action<LauncherCoordinatorStatus> heartbeatHandler)
    {
        _pipeName = pipeName;
        _status = initialStatus;
        _requestHandler = requestHandler;
        _heartbeatHandler = heartbeatHandler;
    }

    public static string CreatePipeName()
    {
        var seed = $"{Environment.UserName}:{Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(seed.ToLowerInvariant()));
        return $"LanMountainDesktop_Launcher_Coordinator_{Convert.ToHexString(bytes[..8])}";
    }

    public void Start()
    {
        _listenTask ??= Task.Run(ListenLoopAsync);
        _heartbeatTask ??= Task.Run(HeartbeatLoopAsync);
    }

    public LauncherCoordinatorStatus GetStatus()
    {
        lock (_statusGate)
        {
            return _status;
        }
    }

    public void UpdateStatus(LauncherCoordinatorStatus status)
    {
        lock (_statusGate)
        {
            _status = status;
        }
    }

    public void Dispose()
    {
        _cts.Cancel();

        try
        {
            _listenTask?.Wait(TimeSpan.FromSeconds(1));
            _heartbeatTask?.Wait(TimeSpan.FromSeconds(1));
        }
        catch
        {
        }

        _cts.Dispose();
    }

    private async Task ListenLoopAsync()
    {
        var consecutiveErrors = 0;

        while (!_cts.IsCancellationRequested)
        {
            NamedPipeServerStream? server = null;
            try
            {
                server = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.InOut,
                    8,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await server.WaitForConnectionAsync(_cts.Token).ConfigureAwait(false);
                var connectedServer = server;
                _ = Task.Run(() => HandleConnectionAsync(connectedServer, _cts.Token), _cts.Token);
                server = null;
                consecutiveErrors = 0;
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                consecutiveErrors++;
                var delay = ComputeBackoff(consecutiveErrors);
                Logger.Warn($"Launcher coordinator IPC listener failed (attempt {consecutiveErrors}), retrying in {delay}ms: {ex.Message}");
                try
                {
                    await Task.Delay(delay, _cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
            finally
            {
                server?.Dispose();
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

    private async Task HeartbeatLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                _heartbeatHandler(GetStatus());
                await Task.Delay(TimeSpan.FromSeconds(2), _cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Logger.Warn($"Launcher coordinator heartbeat failed: {ex.Message}");
            }
        }
    }

    private async Task HandleConnectionAsync(NamedPipeServerStream server, CancellationToken cancellationToken)
    {
        try
        {
            var request = await ReadRequestAsync(server, cancellationToken).ConfigureAwait(false);
            var status = GetStatus();
            var response = request is null
                ? new LauncherCoordinatorResponse
                {
                    Accepted = false,
                    Code = "invalid_request",
                    Message = "Launcher coordinator request was invalid.",
                    Status = status
                }
                : await _requestHandler(request, status).ConfigureAwait(false);

            await WriteResponseAsync(server, response, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.Warn($"Launcher coordinator IPC request failed: {ex.Message}");
        }
        finally
        {
            try
            {
                server.Dispose();
            }
            catch
            {
            }
        }
    }

    private static async Task<LauncherCoordinatorRequest?> ReadRequestAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        var lengthBuffer = new byte[LengthPrefixSize];
        if (!await ReadExactAsync(stream, lengthBuffer, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var payloadLength = BitConverter.ToInt32(lengthBuffer, 0);
        if (payloadLength <= 0 || payloadLength > MaxPayloadLength)
        {
            return null;
        }

        var payload = new byte[payloadLength];
        if (!await ReadExactAsync(stream, payload, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return JsonSerializer.Deserialize(
            Encoding.UTF8.GetString(payload),
            AppJsonContext.Default.LauncherCoordinatorRequest);
    }

    private static async Task WriteResponseAsync(
        Stream stream,
        LauncherCoordinatorResponse response,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(response, AppJsonContext.Default.LauncherCoordinatorResponse);
        var payload = Encoding.UTF8.GetBytes(json);
        await stream.WriteAsync(BitConverter.GetBytes(payload.Length), cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<bool> ReadExactAsync(
        Stream stream,
        byte[] buffer,
        CancellationToken cancellationToken)
    {
        var totalRead = 0;
        while (totalRead < buffer.Length)
        {
            var read = await stream
                .ReadAsync(buffer.AsMemory(totalRead, buffer.Length - totalRead), cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
            {
                return false;
            }

            totalRead += read;
        }

        return true;
    }
}
