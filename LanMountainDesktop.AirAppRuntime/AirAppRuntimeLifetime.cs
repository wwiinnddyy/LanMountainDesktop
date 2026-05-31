using LanMountainDesktop.Shared.IPC.Abstractions.Services;

namespace LanMountainDesktop.AirAppRuntime;

internal sealed class AirAppRuntimeLifetime
{
    private readonly object _gate = new();
    private readonly DateTimeOffset _startedAtUtc = DateTimeOffset.UtcNow;
    private readonly AirAppLifecycleService _lifecycleService;
    private readonly int _launcherProcessId;
    private readonly int _requesterProcessId;
    private int _hostProcessId;
    private DateTimeOffset _updatedAtUtc;

    public AirAppRuntimeLifetime(AirAppRuntimeOptions options, AirAppLifecycleService lifecycleService)
    {
        _lifecycleService = lifecycleService;
        _launcherProcessId = options.LauncherProcessId;
        _requesterProcessId = options.RequesterProcessId;
        _hostProcessId = options.RequesterProcessId;
        _updatedAtUtc = _startedAtUtc;
    }

    public void AttachHost(int hostProcessId)
    {
        if (hostProcessId <= 0)
        {
            return;
        }

        lock (_gate)
        {
            _hostProcessId = hostProcessId;
            _updatedAtUtc = DateTimeOffset.UtcNow;
        }

        AirAppRuntimeLogger.Info($"Attached host process. HostPid={hostProcessId}.");
    }

    public bool ShouldKeepAlive()
    {
        var status = GetStatus();
        return status.LauncherProcessAlive ||
               status.HostProcessAlive ||
               IsProcessAlive(_requesterProcessId) ||
               status.HasLiveAirApps;
    }

    public AirAppRuntimeStatus GetStatus()
    {
        int hostPid;
        DateTimeOffset updatedAt;
        lock (_gate)
        {
            hostPid = _hostProcessId;
            updatedAt = _updatedAtUtc;
        }

        var launcherAlive = IsProcessAlive(_launcherProcessId);
        var hostAlive = IsProcessAlive(hostPid);
        var hasLiveAirApps = _lifecycleService.HasLiveAirApps();
        return new AirAppRuntimeStatus(
            Environment.ProcessId,
            _launcherProcessId,
            hostPid,
            launcherAlive,
            hostAlive,
            hasLiveAirApps,
            _startedAtUtc,
            updatedAt);
    }

    internal static bool IsProcessAlive(int processId)
    {
        return AirAppLifecycleService.IsProcessAlive(processId);
    }
}
