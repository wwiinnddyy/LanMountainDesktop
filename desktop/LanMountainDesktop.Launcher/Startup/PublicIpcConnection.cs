using LanMountainDesktop.Shared.IPC;

namespace LanMountainDesktop.Launcher.Startup;

internal static class PublicIpcConnection
{
    public static async Task<bool> TryConnectAsync(
        LanMountainDesktopIpcClient ipcClient,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (ipcClient.IsConnected)
        {
            return true;
        }

        try
        {
            var connectTask = ipcClient.ConnectAsync();
            var completedTask = await Task.WhenAny(connectTask, Task.Delay(timeout, cancellationToken)).ConfigureAwait(false);
            if (completedTask != connectTask)
            {
                return false;
            }

            await connectTask.ConfigureAwait(false);
            return ipcClient.IsConnected;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logger.Info($"Public IPC is not ready yet: {ex.Message}");
            return false;
        }
    }

    public static async Task<bool> TryConnectWithBackoffAsync(
        LanMountainDesktopIpcClient ipcClient,
        IReadOnlyList<TimeSpan> attemptTimeouts,
        CancellationToken cancellationToken = default)
    {
        if (ipcClient.IsConnected)
        {
            return true;
        }

        foreach (var timeout in attemptTimeouts)
        {
            if (await TryConnectAsync(ipcClient, timeout, cancellationToken).ConfigureAwait(false))
            {
                return true;
            }
        }

        return ipcClient.IsConnected;
    }
}
