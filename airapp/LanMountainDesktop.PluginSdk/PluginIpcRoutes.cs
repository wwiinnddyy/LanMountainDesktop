namespace LanMountainDesktop.PluginIsolation.Contracts;

public static class PluginIpcRoutes
{
    public static class Session
    {
        public const string Handshake = "session/handshake";
        public const string Capabilities = "session/capabilities";
        public const string Ready = "session/ready";
    }

    public static class Lifecycle
    {
        public const string Initialize = "lifecycle/initialize";
        public const string Stop = "lifecycle/stop";
        public const string RestartRequest = "lifecycle/restart-request";
        public const string StateChanged = "lifecycle/state-changed";
    }

    public static class Settings
    {
        public const string GetSnapshot = "settings/get-snapshot";
        public const string Write = "settings/write";
        public const string Changed = "settings/changed";
    }

    public static class Appearance
    {
        public const string GetSnapshot = "appearance/get-snapshot";
        public const string Changed = "appearance/changed";
    }

    public static class Ui
    {
        public const string Attach = "ui/attach";
        public const string Detach = "ui/detach";
        public const string Command = "ui/command";
        public const string StateChanged = "ui/state-changed";
    }

    public static class Heartbeat
    {
        public const string Ping = "heartbeat/ping";
        public const string Pong = "heartbeat/pong";
    }

    public static class Log
    {
        public const string Write = "log/write";
    }

    public static class Fault
    {
        public const string Report = "fault/report";
    }
}
