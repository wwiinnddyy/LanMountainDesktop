using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using LanMountainDesktop.Launcher.Models;

namespace LanMountainDesktop.Launcher.Services.Ipc;

internal sealed class LauncherCoordinatorIpcClient
{
    private const int LengthPrefixSize = 4;
    private const int MaxPayloadLength = 1024 * 1024;

    public async Task<LauncherCoordinatorResponse?> SendAsync(
        string pipeName,
        LauncherCoordinatorRequest request,
        TimeSpan timeout)
    {
        if (string.IsNullOrWhiteSpace(pipeName))
        {
            return null;
        }

        using var timeoutCts = new CancellationTokenSource(timeout);
        try
        {
            await using var client = new NamedPipeClientStream(
                ".",
                pipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous);

            await client.ConnectAsync(timeoutCts.Token).ConfigureAwait(false);
            await WriteRequestAsync(client, request, timeoutCts.Token).ConfigureAwait(false);
            return await ReadResponseAsync(client, timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (TimeoutException)
        {
            return null;
        }
        catch (Exception ex)
        {
            Logger.Warn($"Failed to send launcher coordinator IPC request: {ex.Message}");
            return null;
        }
    }

    private static async Task WriteRequestAsync(
        Stream stream,
        LauncherCoordinatorRequest request,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(request, AppJsonContext.Default.LauncherCoordinatorRequest);
        var payload = Encoding.UTF8.GetBytes(json);
        await stream.WriteAsync(BitConverter.GetBytes(payload.Length), cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<LauncherCoordinatorResponse?> ReadResponseAsync(
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
            AppJsonContext.Default.LauncherCoordinatorResponse);
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
