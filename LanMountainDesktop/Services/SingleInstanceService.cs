using System;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace LanMountainDesktop.Services;

public sealed class SingleInstanceService : IDisposable
{
    private const byte ActivationRequestCode = 0x41; // 'A'
    private const byte ActivationAckCode = 0x4B; // 'K'
    private const byte ActivationNackCode = 0x4E; // 'N'

    private readonly Mutex _mutex;
    private readonly string _pipeName;
    private readonly CancellationTokenSource _listenCts = new();
    private readonly ManualResetEventSlim _listenerReady = new(false);
    private bool _ownsMutex;
    private bool _disposed;
    private Task? _listenTask;

    private SingleInstanceService(string mutexName, string pipeName)
    {
        _mutex = new Mutex(initiallyOwned: false, mutexName);
        _pipeName = pipeName;
        try
        {
            _ownsMutex = _mutex.WaitOne(TimeSpan.Zero, exitContext: false);
        }
        catch (AbandonedMutexException)
        {
            _ownsMutex = true;
        }
    }

    public bool IsPrimaryInstance => _ownsMutex;

    public static SingleInstanceService CreateDefault()
    {
        const string appId = "LanMountainDesktop";
        var userName = Environment.UserName;
        var scopeSeed = $"{appId}:{userName}";
        var scopeHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(scopeSeed)));
        var suffix = scopeHash[..16];
        var mutexName = OperatingSystem.IsWindows()
            ? $"Local\\{appId}.SingleInstance.{suffix}"
            : $"{appId}.SingleInstance.{suffix}";
        return new SingleInstanceService(
            mutexName,
            $"{appId}.Activate.{suffix}");
    }

    public void StartActivationListener(Action onActivationRequested)
    {
        ArgumentNullException.ThrowIfNull(onActivationRequested);

        if (!_ownsMutex || _disposed || _listenTask is not null)
        {
            return;
        }

        AppLogger.Info(
            "SingleInstance",
            $"Starting activation listener. Pipe='{_pipeName}'; Pid={Environment.ProcessId}; OwnsMutex={_ownsMutex}.");
        _listenTask = Task.Run(() => ListenForActivationAsync(onActivationRequested, _listenCts.Token));
        _listenerReady.Wait(TimeSpan.FromMilliseconds(500));
    }

    public bool TryNotifyPrimaryInstance(TimeSpan timeout)
    {
        return TryNotifyPrimaryInstance(timeout, out _);
    }

    public bool TryNotifyPrimaryInstance(TimeSpan timeout, out string? failureReason)
    {
        if (_ownsMutex || _disposed)
        {
            failureReason = _ownsMutex
                ? "current_instance_is_primary"
                : "single_instance_service_disposed";
            return false;
        }

        try
        {
            using var client = new NamedPipeClientStream(
                serverName: ".",
                pipeName: _pipeName,
                direction: PipeDirection.InOut,
                options: PipeOptions.Asynchronous);

            client.Connect((int)Math.Max(1, timeout.TotalMilliseconds));
            client.WriteByte(ActivationRequestCode);
            client.Flush();

            var ack = client.ReadByte();
            var acknowledged = ack == ActivationAckCode;
            if (!acknowledged)
            {
                failureReason = ack switch
                {
                    ActivationNackCode => "primary_rejected_activation",
                    -1 => "ack_not_received",
                    _ => $"unexpected_ack_code_{ack}"
                };
                AppLogger.Warn(
                    "SingleInstance",
                    $"Primary activation handshake failed. AckCode={ack}; Reason='{failureReason}'; Pipe='{_pipeName}'; Pid={Environment.ProcessId}.");
                return false;
            }

            failureReason = null;
            AppLogger.Info(
                "SingleInstance",
                $"Primary activation acknowledged. Pipe='{_pipeName}'; Pid={Environment.ProcessId}.");
            return true;
        }
        catch (Exception ex)
        {
            failureReason = "primary_activation_handshake_exception";
            AppLogger.Warn("SingleInstance", "Failed to notify the primary instance.", ex);
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _listenCts.Cancel();
        try
        {
            _listenTask?.Wait(TimeSpan.FromSeconds(1));
        }
        catch
        {
            // Ignore listener shutdown races during process exit.
        }

        _listenCts.Dispose();
        _listenerReady.Dispose();
        if (_ownsMutex)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // Ownership may already be lost during shutdown.
            }
        }

        _mutex.Dispose();
    }

    private async Task ListenForActivationAsync(Action onActivationRequested, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.InOut,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                _listenerReady.Set();
                await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                var buffer = new byte[1];
                var readBytes = await server.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                var isActivationRequest = readBytes == 1 && buffer[0] == ActivationRequestCode;
                var ackCode = ActivationAckCode;

                if (!isActivationRequest)
                {
                    ackCode = ActivationNackCode;
                    AppLogger.Warn(
                        "SingleInstance",
                        $"Received malformed activation request. ReadBytes={readBytes}; Value={(readBytes == 1 ? buffer[0] : -1)}; Pipe='{_pipeName}'.");
                }
                else
                {
                    try
                    {
                        onActivationRequested();
                    }
                    catch (Exception ex)
                    {
                        ackCode = ActivationNackCode;
                        AppLogger.Warn("SingleInstance", "Activation callback failed.", ex);
                    }
                }

                var ackBuffer = new[] { ackCode };
                await server.WriteAsync(ackBuffer, cancellationToken).ConfigureAwait(false);
                await server.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                AppLogger.Warn("SingleInstance", "Activation listener failed.", ex);
                await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
