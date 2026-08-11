namespace LanMountainDesktop.AirAppIsolation.Contracts;

public sealed record AirAppInitializeRequest(
    string AirAppId,
    string SessionId,
    string HostPipeName,
    string DataDirectory,
    IReadOnlyDictionary<string, string>? StartupProperties = null);

public sealed record AirAppInitializeResponse(
    bool Succeeded,
    string? ErrorCode = null,
    string? ErrorMessage = null);

public sealed record AirAppStopRequest(
    string Reason,
    bool RestartRequested = false);

public sealed record AirAppRestartRequest(string Reason);

public sealed record AirAppLifecycleStateChanged(
    string State,
    string? Detail = null);

public static class AirAppLifecycleStates
{
    public const string Starting = "starting";
    public const string Ready = "ready";
    public const string Degraded = "degraded";
    public const string Stopping = "stopping";
    public const string Stopped = "stopped";
    public const string Faulted = "faulted";
}
