using System;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace LanMountainDesktop.Services;

public sealed class SingleInstanceService : IDisposable
{
    private readonly Mutex _mutex;
    private readonly string _pipeName;
    private readonly CancellationTokenSource _listenCts = new();
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

        _listenTask = Task.Run(() => ListenForActivationAsync(onActivationRequested, _listenCts.Token));
    }

    public bool TryNotifyPrimaryInstance(TimeSpan timeout)
    {
        if (_ownsMutex || _disposed)
        {
            return false;
        }

        try
        {
            using var client = new NamedPipeClientStream(
                serverName: ".",
                pipeName: _pipeName,
                direction: PipeDirection.Out,
                options: PipeOptions.Asynchronous);

            client.Connect((int)Math.Max(1, timeout.TotalMilliseconds));
            client.WriteByte(1);
            client.Flush();
            return true;
        }
        catch (Exception ex)
        {
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
                    PipeDirection.In,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                await server.ReadAsync(new byte[1], cancellationToken).ConfigureAwait(false);
                onActivationRequested();
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
