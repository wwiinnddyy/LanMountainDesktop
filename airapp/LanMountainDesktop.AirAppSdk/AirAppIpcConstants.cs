using LanMountainDesktop.AirAppIsolation.Contracts;

namespace LanMountainDesktop.AirAppIsolation.Ipc;

public static class AirAppIpcConstants
{
    // 以下 "PLUGIN" 字样的常量值是冻结的，不是清理时漏掉的：
    // 它们是宿主与进程外 AirApp 之间的线协议（环境变量名 / 命令行开关）。改名会让已缓存的
    // AirAppRuntime / AirAppHost 二进制与新版宿主互不识别，要改必须宿主与运行时同时支持新旧两名，
    // 属于协议演进决策而不是重命名。常量标识符本身已统一为 AirApp 命名。
    public const string EnvironmentAirAppId = "LANMOUNTAIN_PLUGIN_ID";
    public const string EnvironmentSessionId = "LANMOUNTAIN_PLUGIN_SESSION_ID";
    public const string EnvironmentHostPipeName = "LANMOUNTAIN_PLUGIN_HOST_PIPE";
    public const string EnvironmentProtocolVersion = "LANMOUNTAIN_PLUGIN_PROTOCOL_VERSION";
    public const string EnvironmentRuntimeMode = "LANMOUNTAIN_PLUGIN_RUNTIME_MODE";

    public const string CommandLineAirAppId = "--plugin-id";
    public const string CommandLineSessionId = "--session-id";
    public const string CommandLineHostPipeName = "--host-pipe-name";
    public const string CommandLineProtocolVersion = "--protocol-version";
    public const string CommandLineRuntimeMode = "--runtime-mode";

    public static readonly TimeSpan DefaultConnectTimeout = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan DefaultHeartbeatInterval = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan DefaultHeartbeatTimeout = TimeSpan.FromSeconds(15);

    public const string DefaultProtocolVersion = AirAppIsolationProtocolVersion.Current;
}
