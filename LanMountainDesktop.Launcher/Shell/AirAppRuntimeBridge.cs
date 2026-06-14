using System.Diagnostics;
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
        Logger.Info($"AIRAPP: Checking if AirApp Runtime is available. AppRoot='{_appRoot}'");

        if (await TryGetStatusAsync().ConfigureAwait(false) is not null)
        {
            Logger.Info("AIRAPP: AirApp Runtime is already available.");
            return;
        }

        Logger.Info("AIRAPP: Starting AirApp Runtime...");
        Process? process;
        try
        {
            process = AirAppRuntimeProcessStarter.Start(new AirAppRuntimeStartRequest(
                _appRoot,
                Environment.ProcessId,
                0,
                _dataRoot));
        }
        catch (Exception ex)
        {
            Logger.Warn($"AIRAPP: AirApp Runtime start request failed. AppRoot='{_appRoot}'; Error='{ex.Message}'");
            return;
        }

        Logger.Info($"AIRAPP: AirApp Runtime start requested. Pid={(process is null ? -1 : process.Id)}; AppRoot='{_appRoot}'.");

        for (var attempt = 1; attempt <= ConnectAttempts; attempt++)
        {
            Logger.Info($"AIRAPP: Attempt {attempt}/{ConnectAttempts} - Checking IPC connection...");

            if (await TryGetStatusAsync().ConfigureAwait(false) is not null)
            {
                Logger.Info("AIRAPP: AirApp Runtime IPC is ready.");
                return;
            }

            var delayMs = 250 * attempt;
            Logger.Info($"AIRAPP: IPC not ready, waiting {delayMs}ms before retry...");
            await Task.Delay(TimeSpan.FromMilliseconds(delayMs)).ConfigureAwait(false);
        }

        Logger.Warn("AIRAPP: AirApp Runtime did not become ready after pre-start; Host fallback remains available.");
    }

    public async Task AttachHostAsync(int hostProcessId)
    {
        if (hostProcessId <= 0)
        {
            return;
        }

        try
        {
            using var cts = new CancellationTokenSource();
            using var client = new LanMountainDesktopIpcClient();

            var connectTask = client.ConnectAsync(IpcConstants.AirAppRuntimePipeName);
            await connectTask.WaitAsync(TimeSpan.FromSeconds(3), cts.Token).ConfigureAwait(false);

            var proxy = client.CreateProxy<IAirAppRuntimeControlService>();
            var attachTask = proxy.AttachHostAsync(hostProcessId);
            var result = await attachTask.WaitAsync(TimeSpan.FromSeconds(3), cts.Token).ConfigureAwait(false);
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
            using var cts = new CancellationTokenSource();
            using var client = new LanMountainDesktopIpcClient();

            var connectTask = client.ConnectAsync(IpcConstants.AirAppRuntimePipeName);
            await connectTask.WaitAsync(TimeSpan.FromSeconds(2), cts.Token).ConfigureAwait(false);

            var proxy = client.CreateProxy<IAirAppRuntimeControlService>();
            var statusTask = proxy.GetStatusAsync();
            return await statusTask.WaitAsync(TimeSpan.FromSeconds(2), cts.Token).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            Logger.Info("AIRAPP: TryGetStatusAsync timed out (2s).");
            return null;
        }
        catch (OperationCanceledException)
        {
            Logger.Info("AIRAPP: TryGetStatusAsync cancelled.");
            return null;
        }
        catch (Exception ex)
        {
            Logger.Info($"AIRAPP: TryGetStatusAsync failed: {ex.GetType().Name} - {ex.Message}");
            return null;
        }
    }
}
