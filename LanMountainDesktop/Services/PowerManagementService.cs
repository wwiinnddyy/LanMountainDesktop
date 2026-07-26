using System;
using System.Diagnostics;
using System.Threading.Tasks;
using LanMountainDesktop.Platform.Abstractions;
using LanMountainDesktop.Platform.Windows;

namespace LanMountainDesktop.Services;

/// <summary>
/// 电源管理服务工厂。接口与实现位于平台层：
/// 接口 <see cref="IPowerManagementService"/> 在 Platform.Abstractions，
/// Windows 实现（P/Invoke）在 Platform.Windows。
/// Linux 实现基于 systemctl/loginctl 命令行，无平台专属 API，保留在宿主内。
/// </summary>
public static class PowerManagementServiceFactory
{
    private static IPowerManagementService? _instance;
    private static readonly object _lock = new();

    public static IPowerManagementService GetOrCreate()
    {
        lock (_lock)
        {
            return _instance ??= CreatePlatformService();
        }
    }

    private static IPowerManagementService CreatePlatformService()
    {
        if (OperatingSystem.IsWindows())
            return new WindowsPowerManagementService();
        if (OperatingSystem.IsLinux())
            return new LinuxPowerManagementService();
        return new NullPowerManagementService();
    }
}

internal sealed class LinuxPowerManagementService : IPowerManagementService
{
    public bool IsShutdownSupported => true;
    public bool IsRestartSupported => true;
    public bool IsLogoutSupported => true;
    public bool IsLockSupported => true;
    public bool IsSleepSupported => true;

    public async Task ShutdownAsync()
    {
        await RunSystemctlCommand("poweroff -i");
    }

    public async Task RestartAsync()
    {
        await RunSystemctlCommand("reboot -i");
    }

    public async Task LogoutAsync()
    {
        await RunLoginctlCommand("terminate-session $XDG_SESSION_ID");
    }

    public async Task LockAsync()
    {
        await RunLoginctlCommand("lock-session");
    }

    public async Task SleepAsync()
    {
        await RunSystemctlCommand("suspend -i");
    }

    public void ShowNativePowerUI(PowerAction action)
    {
        switch (action)
        {
            case PowerAction.Shutdown:
                RunProcess("systemctl", "poweroff -i");
                break;
            case PowerAction.Restart:
                RunProcess("systemctl", "reboot -i");
                break;
        }
    }

    private static async Task RunSystemctlCommand(string args)
    {
        await RunProcess("systemctl", args);
    }

    private static async Task RunLoginctlCommand(string args)
    {
        await RunProcess("loginctl", args);
    }

    private static async Task RunProcess(string command, string args)
    {
        await Task.Run(() =>
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = command,
                    Arguments = args,
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                })?.WaitForExit(5000);
            }
            catch (Exception ex)
            {
                AppLogger.Error("LinuxPowerManagement", $"Failed to execute {command} {args}: {ex.Message}");
            }
        });
    }
}
