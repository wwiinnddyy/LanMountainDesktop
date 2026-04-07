using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace LanMountainDesktop.Services;

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

internal sealed class WindowsPowerManagementService : IPowerManagementService
{
    public bool IsShutdownSupported => true;
    public bool IsRestartSupported => true;
    public bool IsLogoutSupported => true;
    public bool IsLockSupported => true;
    public bool IsSleepSupported => true;

    public async Task ShutdownAsync()
    {
        await Task.Run(() =>
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "shutdown",
                Arguments = "/s /t 0",
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden
            });
        });
    }

    public async Task RestartAsync()
    {
        await Task.Run(() =>
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "shutdown",
                Arguments = "/r /t 0",
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden
            });
        });
    }

    public async Task LogoutAsync()
    {
        await Task.Run(() =>
        {
            ExitWindowsEx(0, 0);
        });
    }

    public async Task LockAsync()
    {
        await Task.Run(() =>
        {
            LockWorkStation();
        });
    }

    public async Task SleepAsync()
    {
        await Task.Run(() =>
        {
            SetSuspendState(false, false, false);
        });
    }

    public void ShowNativePowerUI(PowerAction action)
    {
        var slideToShutDownPath = Environment.ExpandEnvironmentVariables(@"%windir%\System32\SlideToShutDown.exe");
        if (System.IO.File.Exists(slideToShutDownPath))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = slideToShutDownPath,
                UseShellExecute = true
            });
            return;
        }

        switch (action)
        {
            case PowerAction.Shutdown:
                Process.Start(new ProcessStartInfo
                {
                    FileName = "shutdown",
                    Arguments = "/s /t 5 /c \"LanMountainDesktop: Shutting down...\"",
                    UseShellExecute = true
                });
                break;

            case PowerAction.Restart:
                Process.Start(new ProcessStartInfo
                {
                    FileName = "shutdown",
                    Arguments = "/r /t 5 /c \"LanMountainDesktop: Restarting...\"",
                    UseShellExecute = true
                });
                break;
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ExitWindowsEx(uint uFlags, uint dwReason);

    [DllImport("user32.dll")]
    private static extern void LockWorkStation();

    [DllImport("powrprof.dll", SetLastError = true)]
    private static extern bool SetSuspendState(bool hibernate, bool forceCritical, bool disableWakeEvent);
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

internal sealed class NullPowerManagementService : IPowerManagementService
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
