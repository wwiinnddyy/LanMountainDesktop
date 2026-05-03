using System.Buffers;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using LanMountainDesktop.Launcher.Services;
using LanMountainDesktop.Shared.Contracts.Update;

namespace LanMountainDesktop.Launcher.Services.Ipc;

internal sealed class LauncherUpdateProgressIpcServer : IUpdateProgressReporter, IDisposable
{
    private const int LengthPrefixSize = 4;

    private readonly string _pipeName;
    private readonly CancellationTokenSource _cts = new();
    private NamedPipeServerStream? _pipe;
    private Task? _listenTask;
    private volatile bool _clientConnected;

    public LauncherUpdateProgressIpcServer(int launcherPid)
    {
        _pipeName = $"LanMountainDesktop_Update_{launcherPid}";
    }

    public string PipeName => _pipeName;

    public void Start()
    {
        _listenTask = Task.Run(AcceptConnectionAsync, _cts.Token);
    }

    private async Task AcceptConnectionAsync()
    {
        while (!_cts.Token.IsCancellationRequested)
        {
            try
            {
                _pipe = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.Out,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await _pipe.WaitForConnectionAsync(_cts.Token).ConfigureAwait(false);
                _clientConnected = true;
                return;
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Logger.Warn($"Update progress IPC listen error: {ex.Message}");
                try
                {
                    await Task.Delay(200, _cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    public void ReportProgress(InstallProgressReport report)
    {
        if (!_clientConnected || _pipe is null || !_pipe.IsConnected)
        {
            return;
        }

        try
        {
            WriteMessage(_pipe, JsonSerializer.Serialize(report, AppJsonContext.Default.InstallProgressReport));
        }
        catch (Exception ex)
        {
            Logger.Warn($"Failed to report progress via IPC: {ex.Message}");
        }
    }

    public void ReportComplete(InstallCompleteReport report)
    {
        if (!_clientConnected || _pipe is null || !_pipe.IsConnected)
        {
            return;
        }

        try
        {
            WriteMessage(_pipe, JsonSerializer.Serialize(report, AppJsonContext.Default.InstallCompleteReport));
        }
        catch (Exception ex)
        {
            Logger.Warn($"Failed to report completion via IPC: {ex.Message}");
        }
    }

    private static void WriteMessage(Stream stream, string json)
    {
        var payload = Encoding.UTF8.GetBytes(json);
        var lengthPrefix = BitConverter.GetBytes(payload.Length);
        stream.Write(lengthPrefix, 0, LengthPrefixSize);
        stream.Write(payload, 0, payload.Length);
        stream.Flush();
    }

    public void Dispose()
    {
        _cts.Cancel();
        try
        {
            _pipe?.Dispose();
        }
        catch
        {
        }

        try
        {
            _listenTask?.Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
        }

        _cts.Dispose();
    }
}
