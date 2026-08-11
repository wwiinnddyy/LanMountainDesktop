using dotnetCampus.Ipc.CompilerServices.Attributes;

namespace LanMountainDesktop.Shared.IPC.Abstractions.Services;

[IpcPublic(IgnoresIpcException = true)]
public interface IAirAppRuntimeControlService
{
    Task<AirAppRuntimeControlResult> AttachHostAsync(int hostProcessId);

    Task<AirAppRuntimeStatus> GetStatusAsync();
}

public sealed record AirAppRuntimeControlResult(
    bool Accepted,
    string Code,
    string Message,
    AirAppRuntimeStatus Status);

public sealed record AirAppRuntimeStatus(
    int ProcessId,
    int LauncherProcessId,
    int HostProcessId,
    bool LauncherProcessAlive,
    bool HostProcessAlive,
    bool HasLiveAirApps,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset UpdatedAtUtc);
