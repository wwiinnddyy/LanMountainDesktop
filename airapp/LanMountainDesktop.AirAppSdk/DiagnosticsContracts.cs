namespace LanMountainDesktop.AirAppIsolation.Contracts;

public sealed record AirAppHeartbeatPing(
    string SessionId,
    DateTimeOffset SentAtUtc);

public sealed record AirAppHeartbeatPong(
    string SessionId,
    DateTimeOffset ReceivedAtUtc);

public sealed record AirAppLogEntry(
    string Level,
    string Category,
    string Message,
    DateTimeOffset TimestampUtc,
    string? Exception = null);

public static class AirAppLogLevels
{
    public const string Trace = "trace";
    public const string Debug = "debug";
    public const string Information = "information";
    public const string Warning = "warning";
    public const string Error = "error";
    public const string Critical = "critical";
}

public sealed record AirAppFaultReport(
    string SessionId,
    string FaultKind,
    bool IsFatal,
    string Message,
    string? StackTrace = null,
    int? WorkerProcessId = null,
    int? ExitCode = null,
    DateTimeOffset? OccurredAtUtc = null);

public static class AirAppFaultKinds
{
    public const string ManagedException = "managed-exception";
    public const string NativeCrash = "native-crash";
    public const string WatchdogTimeout = "watchdog-timeout";
    public const string StartupFailure = "startup-failure";
    public const string ForcedTermination = "forced-termination";
}
