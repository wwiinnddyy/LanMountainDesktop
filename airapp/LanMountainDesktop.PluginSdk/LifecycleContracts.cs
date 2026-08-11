namespace LanMountainDesktop.PluginIsolation.Contracts;

public sealed record PluginInitializeRequest(
    string PluginId,
    string SessionId,
    string HostPipeName,
    string DataDirectory,
    IReadOnlyDictionary<string, string>? StartupProperties = null);

public sealed record PluginInitializeResponse(
    bool Succeeded,
    string? ErrorCode = null,
    string? ErrorMessage = null);

public sealed record PluginStopRequest(
    string Reason,
    bool RestartRequested = false);

public sealed record PluginRestartRequest(string Reason);

public sealed record PluginLifecycleStateChanged(
    string State,
    string? Detail = null);

public static class PluginLifecycleStates
{
    public const string Starting = "starting";
    public const string Ready = "ready";
    public const string Degraded = "degraded";
    public const string Stopping = "stopping";
    public const string Stopped = "stopped";
    public const string Faulted = "faulted";
}
