namespace LanMountainDesktop.Platform.Abstractions;

/// <summary>
/// 电源管理服务接口。桌面平台提供关机/重启/注销/锁定/睡眠能力；
/// 移动平台不提供此能力（使用 <see cref="NullPowerManagementService"/>）。
/// </summary>
public interface IPowerManagementService
{
    bool IsShutdownSupported { get; }
    bool IsRestartSupported { get; }
    bool IsLogoutSupported { get; }
    bool IsLockSupported { get; }
    bool IsSleepSupported { get; }

    Task ShutdownAsync();
    Task RestartAsync();
    Task LogoutAsync();
    Task LockAsync();
    Task SleepAsync();

    void ShowNativePowerUI(PowerAction action);
}

public enum PowerAction
{
    Shutdown,
    Restart
}

/// <summary>
/// 无操作实现。用于不支持电源管理的平台（如移动端）。
/// </summary>
public sealed class NullPowerManagementService : IPowerManagementService
{
    public bool IsShutdownSupported => false;
    public bool IsRestartSupported => false;
    public bool IsLogoutSupported => false;
    public bool IsLockSupported => false;
    public bool IsSleepSupported => false;

    public Task ShutdownAsync() => Task.CompletedTask;
    public Task RestartAsync() => Task.CompletedTask;
    public Task LogoutAsync() => Task.CompletedTask;
    public Task LockAsync() => Task.CompletedTask;
    public Task SleepAsync() => Task.CompletedTask;

    public void ShowNativePowerUI(PowerAction action) { }
}
