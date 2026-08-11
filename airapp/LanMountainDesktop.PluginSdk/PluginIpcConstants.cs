using LanMountainDesktop.PluginIsolation.Contracts;

namespace LanMountainDesktop.PluginIsolation.Ipc;

public static class PluginIpcConstants
{
    public const string EnvironmentPluginId = "LANMOUNTAIN_PLUGIN_ID";
    public const string EnvironmentSessionId = "LANMOUNTAIN_PLUGIN_SESSION_ID";
    public const string EnvironmentHostPipeName = "LANMOUNTAIN_PLUGIN_HOST_PIPE";
    public const string EnvironmentProtocolVersion = "LANMOUNTAIN_PLUGIN_PROTOCOL_VERSION";
    public const string EnvironmentRuntimeMode = "LANMOUNTAIN_PLUGIN_RUNTIME_MODE";

    public const string CommandLinePluginId = "--plugin-id";
    public const string CommandLineSessionId = "--session-id";
    public const string CommandLineHostPipeName = "--host-pipe-name";
    public const string CommandLineProtocolVersion = "--protocol-version";
    public const string CommandLineRuntimeMode = "--runtime-mode";

    public static readonly TimeSpan DefaultConnectTimeout = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan DefaultHeartbeatInterval = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan DefaultHeartbeatTimeout = TimeSpan.FromSeconds(15);

    public const string DefaultProtocolVersion = PluginIsolationProtocolVersion.Current;
}
