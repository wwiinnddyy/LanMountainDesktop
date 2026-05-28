namespace LanMountainDesktop.Launcher.Startup;

internal static class StartupTimeoutPolicy
{
    public static readonly TimeSpan SoftTimeout = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan HardTimeout = TimeSpan.FromSeconds(120);

    /// <summary>Initial Public IPC connect attempt (AOT cold start may be slower).</summary>
    public static readonly TimeSpan InitialIpcConnectTimeout = TimeSpan.FromMilliseconds(1200);

    /// <summary>Subsequent reconnect attempts use increasing per-try timeouts.</summary>
    public static readonly TimeSpan[] IpcReconnectAttemptTimeouts =
    [
        TimeSpan.FromMilliseconds(800),
        TimeSpan.FromMilliseconds(1500),
        TimeSpan.FromMilliseconds(3000),
        TimeSpan.FromMilliseconds(5000)
    ];

    public static readonly TimeSpan ExistingHostProbeTimeout = TimeSpan.FromMilliseconds(900);
    public static readonly TimeSpan ShellStatusPollInterval = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan IpcReconnectInterval = TimeSpan.FromSeconds(2);
}
