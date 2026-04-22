namespace LanMountainDesktop.PluginIsolation.Contracts;

public sealed record PluginHeartbeatPing(
    string SessionId,
    DateTimeOffset SentAtUtc);

public sealed record PluginHeartbeatPong(
    string SessionId,
    DateTimeOffset ReceivedAtUtc);

public sealed record PluginLogEntry(
    string Level,
    string Category,
    string Message,
    DateTimeOffset TimestampUtc,
    string? Exception = null);

public static class PluginLogLevels
{
    public const string Trace = "trace";
    public const string Debug = "debug";
    public const string Information = "information";
    public const string Warning = "warning";
    public const string Error = "error";
    public const string Critical = "critical";
}

public sealed record PluginFaultReport(
    string SessionId,
    string FaultKind,
    bool IsFatal,
    string Message,
    string? StackTrace = null,
    int? WorkerProcessId = null,
    int? ExitCode = null,
    DateTimeOffset? OccurredAtUtc = null);

public static class PluginFaultKinds
{
    public const string ManagedException = "managed-exception";
    public const string NativeCrash = "native-crash";
    public const string WatchdogTimeout = "watchdog-timeout";
    public const string StartupFailure = "startup-failure";
    public const string ForcedTermination = "forced-termination";
}
