using System.Buffers;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using LanMountainDesktop.Shared.Contracts.Update;

namespace LanMountainDesktop.Services.Update;

internal sealed class IpcLauncherUpdateBridge : ILauncherUpdateBridge, IDisposable
{
    private const int LengthPrefixSize = 4;
    private const int MaxPayloadLength = 1024 * 1024;
    private static readonly TimeSpan PipeConnectTimeout = TimeSpan.FromSeconds(5);

    private readonly UpdateProgressSubject _progressSubject = new();
    private readonly CancellationTokenSource _cts = new();
    private int? _launcherPid;

    public IObservable<InstallProgressReport> ProgressStream => _progressSubject;

    public async Task<LaunchResult> LaunchInstallerAsync(InstallRequest request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        try
        {
            var launcherPath = LauncherPathResolver.ResolveLauncherExecutablePath();
            if (string.IsNullOrWhiteSpace(launcherPath) || !File.Exists(launcherPath))
            {
                return new LaunchResult(false, "Launcher executable not found.", null);
            }

            var resolvedLauncherRoot = Path.GetDirectoryName(launcherPath)!;

            var startInfo = new ProcessStartInfo
            {
                FileName = launcherPath,
                Arguments = $"apply-update --app-root \"{resolvedLauncherRoot}\" --launch-source {request.LaunchSource ?? "apply-update"}",
                UseShellExecute = false,
                WorkingDirectory = resolvedLauncherRoot
            };

            var process = Process.Start(startInfo);
            if (process is null)
            {
                return new LaunchResult(false, "Failed to start Launcher process.", null);
            }

            _launcherPid = process.Id;

            _ = Task.Run(() => ConnectAndReadProgressAsync(process.Id, ct), ct);

            return new LaunchResult(true, null, process.Id);
        }
        catch (Exception ex)
        {
            return new LaunchResult(false, ex.Message, null);
        }
    }

    public Task<bool> SupportsIpcAsync()
    {
        return Task.FromResult(true);
    }

    private async Task ConnectAndReadProgressAsync(int launcherPid, CancellationToken ct)
    {
        var pipeName = $"LanMountainDesktop_Update_{launcherPid}";

        try
        {
            using var pipe = new NamedPipeClientStream(
                ".",
                pipeName,
                PipeDirection.In,
                PipeOptions.Asynchronous);

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, _cts.Token);
            using var timeoutCts = new CancellationTokenSource(PipeConnectTimeout);
            using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(linkedCts.Token, timeoutCts.Token);

            await pipe.ConnectAsync(combinedCts.Token).ConfigureAwait(false);

            await ReadProgressFromPipeAsync(pipe, linkedCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (TimeoutException)
        {
        }
        catch (IOException)
        {
        }
        catch (Exception ex)
        {
            AppLogger.Warn("IpcLauncherUpdateBridge", $"Progress pipe connection failed (fire-and-forget): {ex.Message}");
        }
    }

    private async Task ReadProgressFromPipeAsync(NamedPipeClientStream pipe, CancellationToken ct)
    {
        var lengthBuffer = ArrayPool<byte>.Shared.Rent(LengthPrefixSize);
        try
        {
            while (pipe.IsConnected && !ct.IsCancellationRequested)
            {
                var totalRead = 0;
                while (totalRead < LengthPrefixSize)
                {
                    var read = await pipe.ReadAsync(lengthBuffer.AsMemory(totalRead, LengthPrefixSize - totalRead), ct).ConfigureAwait(false);
                    if (read == 0)
                    {
                        return;
                    }

                    totalRead += read;
                }

                var payloadLength = BitConverter.ToInt32(lengthBuffer, 0);
                if (payloadLength <= 0 || payloadLength > MaxPayloadLength)
                {
                    return;
                }

                var payloadBuffer = ArrayPool<byte>.Shared.Rent(payloadLength);
                try
                {
                    totalRead = 0;
                    while (totalRead < payloadLength)
                    {
                        var read = await pipe.ReadAsync(payloadBuffer.AsMemory(totalRead, payloadLength - totalRead), ct).ConfigureAwait(false);
                        if (read == 0)
                        {
                            return;
                        }

                        totalRead += read;
                    }

                    var json = Encoding.UTF8.GetString(payloadBuffer, 0, payloadLength);
                    var report = JsonSerializer.Deserialize(json, UpdateJsonContext.Default.InstallProgressReport);
                    if (report is not null)
                    {
                        _progressSubject.OnNext(report);
                    }
                }
                catch (JsonException)
                {
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

    public void Dispose()
    {
        _cts.Cancel();
        _progressSubject.OnCompleted();
        _cts.Dispose();
    }
}
