using LanMountainDesktop.Shared.IPC.Abstractions.Services;

namespace LanMountainDesktop.AirAppRuntime;

internal sealed class AirAppRuntimeControlService : IAirAppRuntimeControlService
{
    private readonly AirAppRuntimeLifetime _lifetime;

    public AirAppRuntimeControlService(AirAppRuntimeLifetime lifetime)
    {
        _lifetime = lifetime;
    }

    public Task<AirAppRuntimeControlResult> AttachHostAsync(int hostProcessId)
    {
        _lifetime.AttachHost(hostProcessId);
        var status = _lifetime.GetStatus();
        return Task.FromResult(new AirAppRuntimeControlResult(
            hostProcessId > 0,
            hostProcessId > 0 ? "host_attached" : "invalid_host_pid",
            hostProcessId > 0 ? "AirApp runtime host process attached." : "Host process id must be positive.",
            status));
    }

    public Task<AirAppRuntimeStatus> GetStatusAsync()
    {
        return Task.FromResult(_lifetime.GetStatus());
    }
}
