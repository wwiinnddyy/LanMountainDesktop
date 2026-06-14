namespace LanMountainDesktop.Launcher.Startup;

internal static class StartupTimeoutPolicy
{
    public static readonly TimeSpan SoftTimeout = TimeSpan.FromSeconds(45);
    public static readonly TimeSpan HardTimeout = TimeSpan.FromSeconds(180);

    /// <summary>Initial Public IPC connect attempt (AOT cold start is significantly slower).</summary>
    public static readonly TimeSpan InitialIpcConnectTimeout = TimeSpan.FromMilliseconds(3000);

    /// <summary>Subsequent reconnect attempts use increasing per-try timeouts.</summary>
    public static readonly TimeSpan[] IpcReconnectAttemptTimeouts =
    [
        TimeSpan.FromMilliseconds(1500),
        TimeSpan.FromMilliseconds(3000),
        TimeSpan.FromMilliseconds(5000),
        TimeSpan.FromMilliseconds(8000),
        TimeSpan.FromMilliseconds(10000)
    ];

    public static readonly TimeSpan ExistingHostProbeTimeout = TimeSpan.FromMilliseconds(1500);
    public static readonly TimeSpan ShellStatusPollInterval = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan IpcReconnectInterval = TimeSpan.FromSeconds(3);

    /// <summary>Maximum time to wait for host process exit after it starts (for early-exit detection).</summary>
    public static readonly TimeSpan HostEarlyExitWindow = TimeSpan.FromSeconds(5);
}
