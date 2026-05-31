using LanMountainDesktop.Shared.IPC;
using LanMountainDesktop.Shared.IPC.Abstractions.Services;

namespace LanMountainDesktop.Launcher.Shell;

internal sealed class AirAppRuntimeBridge
{
    private const int ConnectAttempts = 8;

    private readonly string _appRoot;
    private readonly string? _dataRoot;

    public AirAppRuntimeBridge(string appRoot, string? dataRoot)
    {
        _appRoot = appRoot;
        _dataRoot = dataRoot;
    }

    public async Task EnsureStartedAsync()
    {
        if (await TryGetStatusAsync().ConfigureAwait(false) is not null)
        {
            Logger.Info("AirApp Runtime is already available.");
            return;
        }

        var process = AirAppRuntimeProcessStarter.Start(new AirAppRuntimeStartRequest(
            _appRoot,
            Environment.ProcessId,
            0,
            _dataRoot));
        Logger.Info($"AirApp Runtime start requested. Pid={(process is null ? -1 : process.Id)}; AppRoot='{_appRoot}'.");

        for (var attempt = 1; attempt <= ConnectAttempts; attempt++)
        {
            if (await TryGetStatusAsync().ConfigureAwait(false) is not null)
            {
                Logger.Info("AirApp Runtime IPC is ready.");
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt)).ConfigureAwait(false);
        }

        Logger.Warn("AirApp Runtime did not become ready after pre-start; Host fallback remains available.");
    }

    public async Task AttachHostAsync(int hostProcessId)
    {
        if (hostProcessId <= 0)
        {
            return;
        }

        try
        {
            using var client = new LanMountainDesktopIpcClient();
            await client.ConnectAsync(IpcConstants.AirAppRuntimePipeName).ConfigureAwait(false);
            var proxy = client.CreateProxy<IAirAppRuntimeControlService>();
            var result = await proxy.AttachHostAsync(hostProcessId).ConfigureAwait(false);
            Logger.Info($"AirApp Runtime host attach completed. Accepted={result.Accepted}; Code='{result.Code}'; HostPid={hostProcessId}.");
        }
        catch (Exception ex)
        {
            Logger.Warn($"Failed to attach Host to AirApp Runtime: {ex.Message}");
        }
    }

    private static async Task<AirAppRuntimeStatus?> TryGetStatusAsync()
    {
        try
        {
            using var client = new LanMountainDesktopIpcClient();
            await client.ConnectAsync(IpcConstants.AirAppRuntimePipeName).ConfigureAwait(false);
            var proxy = client.CreateProxy<IAirAppRuntimeControlService>();
            return await proxy.GetStatusAsync().ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }
}
