using LanMountainDesktop.Shared.IPC;
using LanMountainDesktop.Shared.IPC.Abstractions.Services;

namespace LanMountainDesktop.Launcher.Shell;

internal sealed class AirAppRuntimeBridge
{
    private const int ConnectAttempts = 8;
    private const int AttachAttempts = 4;

    private readonly string _appRoot;
    private readonly string? _dataRoot;
    private readonly IAirAppRuntimeBridgeBackend _backend;

    public AirAppRuntimeBridge(string appRoot, string? dataRoot)
        : this(appRoot, dataRoot, new AirAppRuntimeBridgeBackend())
    {
    }

    internal AirAppRuntimeBridge(
        string appRoot,
        string? dataRoot,
        IAirAppRuntimeBridgeBackend backend)
    {
        _appRoot = appRoot;
        _dataRoot = dataRoot;
        _backend = backend;
    }

    public async Task<AirAppRuntimeAvailabilityResult> EnsureStartedAsync()
    {
        Logger.Info($"AIRAPP: Checking if AirApp Runtime is available. AppRoot='{_appRoot}'");

        var status = await TryGetStatusAsync().ConfigureAwait(false);
        if (status is not null)
        {
            Logger.Info("AIRAPP: AirApp Runtime is already available.");
            return new AirAppRuntimeAvailabilityResult(
                true,
                "already_available",
                "AirApp Runtime is already available.",
                status);
        }

        Logger.Info("AIRAPP: Starting AirApp Runtime...");
        int? processId;
        try
        {
            processId = _backend.Start(new AirAppRuntimeStartRequest(
                _appRoot,
                Environment.ProcessId,
                0,
                _dataRoot));
        }
        catch (Exception ex)
        {
            Logger.Warn($"AIRAPP: AirApp Runtime start request failed. AppRoot='{_appRoot}'; Error='{ex.Message}'");
            return new AirAppRuntimeAvailabilityResult(
                false,
                "start_failed",
                $"AirApp Runtime start request failed: {ex.Message}",
                null);
        }

        Logger.Info($"AIRAPP: AirApp Runtime start requested. Pid={processId ?? -1}; AppRoot='{_appRoot}'.");
        if (processId is null)
        {
            Logger.Warn("AIRAPP: AirApp Runtime process was not created; Host fallback remains available.");
            return new AirAppRuntimeAvailabilityResult(
                false,
                "process_not_created",
                "AirApp Runtime process was not created.",
                null);
        }

        for (var attempt = 1; attempt <= ConnectAttempts; attempt++)
        {
            Logger.Info($"AIRAPP: Attempt {attempt}/{ConnectAttempts} - Checking IPC connection...");

            status = await TryGetStatusAsync().ConfigureAwait(false);
            if (status is not null)
            {
                Logger.Info("AIRAPP: AirApp Runtime IPC is ready.");
                return new AirAppRuntimeAvailabilityResult(
                    true,
                    "started",
                    "AirApp Runtime IPC is ready.",
                    status);
            }

            if (attempt < ConnectAttempts)
            {
                var delay = TimeSpan.FromMilliseconds(250 * attempt);
                Logger.Info($"AIRAPP: IPC not ready, waiting {delay.TotalMilliseconds:0}ms before retry...");
                await _backend.DelayAsync(delay).ConfigureAwait(false);
            }
        }

        Logger.Warn("AIRAPP: AirApp Runtime did not become ready after pre-start; Host fallback remains available.");
        return new AirAppRuntimeAvailabilityResult(
            false,
            "runtime_unavailable",
            "AirApp Runtime did not become ready after pre-start.",
            null);
    }

    public async Task<AirAppRuntimeHandoffResult> AttachHostAsync(int hostProcessId)
    {
        if (hostProcessId <= 0)
        {
            Logger.Warn($"AIRAPP: Cannot hand off runtime ownership because HostPid={hostProcessId} is invalid.");
            return new AirAppRuntimeHandoffResult(
                false,
                "invalid_host_pid",
                "Host process id must be positive.",
                hostProcessId,
                0,
                null);
        }

        AirAppRuntimeControlResult? lastControlResult = null;
        Exception? lastException = null;
        var attemptsCompleted = 0;

        for (var attempt = 1; attempt <= AttachAttempts; attempt++)
        {
            // Re-check availability before every attempt. If a pre-started runtime exits
            // between discovery and attach, this starts a replacement while Launcher is alive.
            var availability = await EnsureStartedAsync().ConfigureAwait(false);
            if (!availability.Available)
            {
                return new AirAppRuntimeHandoffResult(
                    false,
                    availability.Code,
                    availability.Message,
                    hostProcessId,
                    attemptsCompleted,
                    availability.Status);
            }

            try
            {
                attemptsCompleted = attempt;
                lastControlResult = await _backend.AttachHostAsync(hostProcessId).ConfigureAwait(false);
                if (IsConfirmedHostAttach(lastControlResult, hostProcessId))
                {
                    Logger.Info(
                        $"AIRAPP: Runtime ownership handed off to Host. HostPid={hostProcessId}; " +
                        $"RuntimePid={lastControlResult.Status.ProcessId}; Attempts={attemptsCompleted}.");
                    return new AirAppRuntimeHandoffResult(
                        true,
                        "host_attached",
                        "AirApp Runtime confirmed the live Host attachment.",
                        hostProcessId,
                        attemptsCompleted,
                        lastControlResult.Status);
                }

                Logger.Warn(
                    $"AIRAPP: Runtime Host attach was not confirmed. Attempt={attempt}/{AttachAttempts}; " +
                    $"Accepted={lastControlResult.Accepted}; Code='{lastControlResult.Code}'; " +
                    $"ReturnedHostPid={lastControlResult.Status.HostProcessId}; " +
                    $"HostAlive={lastControlResult.Status.HostProcessAlive}.");
            }
            catch (Exception ex)
            {
                attemptsCompleted = attempt;
                lastException = ex;
                Logger.Warn(
                    $"AIRAPP: Runtime Host attach attempt failed. Attempt={attempt}/{AttachAttempts}; " +
                    $"HostPid={hostProcessId}; Error='{ex.Message}'.");
            }

            if (attempt < AttachAttempts)
            {
                await _backend.DelayAsync(TimeSpan.FromMilliseconds(250 * attempt)).ConfigureAwait(false);
            }
        }

        var code = lastControlResult is null ? "host_attach_failed" : "host_attach_unconfirmed";
        var message = lastControlResult is null
            ? $"AirApp Runtime Host attach failed: {lastException?.Message ?? "unknown error"}"
            : $"AirApp Runtime did not confirm Host attachment. LastCode='{lastControlResult.Code}'.";
        Logger.Warn(
            $"AIRAPP: Runtime ownership handoff failed; Host fallback remains available. " +
            $"HostPid={hostProcessId}; Attempts={attemptsCompleted}; Code='{code}'.");
        return new AirAppRuntimeHandoffResult(
            false,
            code,
            message,
            hostProcessId,
            attemptsCompleted,
            lastControlResult?.Status);
    }

    private static bool IsConfirmedHostAttach(AirAppRuntimeControlResult result, int hostProcessId)
    {
        return result.Accepted &&
               result.Status.HostProcessId == hostProcessId &&
               result.Status.HostProcessAlive;
    }

    private async Task<AirAppRuntimeStatus?> TryGetStatusAsync()
    {
        try
        {
            return await _backend.GetStatusAsync().ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            Logger.Info("AIRAPP: TryGetStatusAsync timed out.");
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

internal sealed record AirAppRuntimeAvailabilityResult(
    bool Available,
    string Code,
    string Message,
    AirAppRuntimeStatus? Status);

internal sealed record AirAppRuntimeHandoffResult(
    bool Accepted,
    string Code,
    string Message,
    int HostProcessId,
    int Attempts,
    AirAppRuntimeStatus? Status);

internal interface IAirAppRuntimeBridgeBackend
{
    int? Start(AirAppRuntimeStartRequest request);

    Task<AirAppRuntimeStatus> GetStatusAsync();

    Task<AirAppRuntimeControlResult> AttachHostAsync(int hostProcessId);

    Task DelayAsync(TimeSpan delay);
}

internal sealed class AirAppRuntimeBridgeBackend : IAirAppRuntimeBridgeBackend
{
    public int? Start(AirAppRuntimeStartRequest request)
    {
        using var process = AirAppRuntimeProcessStarter.Start(request);
        return process?.Id;
    }

    public async Task<AirAppRuntimeStatus> GetStatusAsync()
    {
        using var client = new LanMountainDesktopIpcClient();
        await client.ConnectAsync(IpcConstants.AirAppRuntimePipeName)
            .WaitAsync(TimeSpan.FromSeconds(2))
            .ConfigureAwait(false);

        var proxy = client.CreateProxy<IAirAppRuntimeControlService>();
        return await proxy.GetStatusAsync()
            .WaitAsync(TimeSpan.FromSeconds(2))
            .ConfigureAwait(false);
    }

    public async Task<AirAppRuntimeControlResult> AttachHostAsync(int hostProcessId)
    {
        using var client = new LanMountainDesktopIpcClient();
        await client.ConnectAsync(IpcConstants.AirAppRuntimePipeName)
            .WaitAsync(TimeSpan.FromSeconds(3))
            .ConfigureAwait(false);

        var proxy = client.CreateProxy<IAirAppRuntimeControlService>();
        return await proxy.AttachHostAsync(hostProcessId)
            .WaitAsync(TimeSpan.FromSeconds(3))
            .ConfigureAwait(false);
    }

    public Task DelayAsync(TimeSpan delay) => Task.Delay(delay);
}
