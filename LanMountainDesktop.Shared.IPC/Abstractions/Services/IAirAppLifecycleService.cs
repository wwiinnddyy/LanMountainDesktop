using dotnetCampus.Ipc.CompilerServices.Attributes;

namespace LanMountainDesktop.Shared.IPC.Abstractions.Services;

[IpcPublic(IgnoresIpcException = true)]
public interface IAirAppLifecycleService
{
    Task<AirAppOperationResult> OpenAsync(AirAppOpenRequest request);

    Task<AirAppOperationResult> ActivateAsync(string instanceKey);

    Task<AirAppOperationResult> CloseAsync(string instanceKey);

    Task<AirAppInstanceInfo[]> GetInstancesAsync();

    Task<AirAppOperationResult> RegisterAsync(AirAppRegistrationRequest request);

    Task<AirAppOperationResult> UnregisterAsync(string instanceKey, int processId);
}

public sealed record AirAppOpenRequest(
    string AppId,
    string? SourceComponentId,
    string? SourcePlacementId,
    int RequesterProcessId);

public sealed record AirAppRegistrationRequest(
    string InstanceKey,
    string AppId,
    string SessionId,
    int ProcessId,
    string WindowTitle,
    string? SourceComponentId,
    string? SourcePlacementId);

public sealed record AirAppInstanceInfo(
    string InstanceKey,
    string AppId,
    string SessionId,
    int ProcessId,
    string WindowTitle,
    string? SourceComponentId,
    string? SourcePlacementId,
    bool ProcessAlive,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record AirAppOperationResult(
    bool Accepted,
    string Code,
    string Message,
    AirAppInstanceInfo? Instance);
