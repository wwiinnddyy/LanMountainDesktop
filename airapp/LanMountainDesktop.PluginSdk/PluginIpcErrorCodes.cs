namespace LanMountainDesktop.PluginIsolation.Contracts;

public static class PluginIpcErrorCodes
{
    public const string ProtocolMismatch = "protocol_mismatch";
    public const string SessionRejected = "session_rejected";
    public const string CapabilityDenied = "capability_denied";
    public const string InvalidRequest = "invalid_request";
    public const string UnsupportedRoute = "unsupported_route";
    public const string SettingsConflict = "settings_conflict";
    public const string UiAttachRejected = "ui_attach_rejected";
    public const string WorkerFaulted = "worker_faulted";
    public const string WorkerExited = "worker_exited";
    public const string HeartbeatTimeout = "heartbeat_timeout";
}
