using System.Diagnostics;
using System.Runtime.InteropServices;
using LanMountainDesktop.Platform.Abstractions;

namespace LanMountainDesktop.Platform.Windows;

/// <summary>
/// Windows 电源管理实现（P/Invoke user32/powrprof）。
/// 自 LanMountainDesktop.Services.PowerManagementService 迁移，行为不变。
/// </summary>
public sealed class WindowsPowerManagementService : IPowerManagementService
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
        // SlideToShutDown.exe 只支持关机，不支持重启
        // 重启操作应该通过 RestartAsync() 使用 shutdown /r 命令
        if (action != PowerAction.Shutdown)
            return;

        var slideToShutDownPath = Environment.ExpandEnvironmentVariables(@"%windir%\System32\SlideToShutDown.exe");
        if (File.Exists(slideToShutDownPath))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = slideToShutDownPath,
                UseShellExecute = true
            });
            return;
        }

        // 回退到标准关机命令
        Process.Start(new ProcessStartInfo
        {
            FileName = "shutdown",
            Arguments = "/s /t 5 /c \"LanMountainDesktop: Shutting down...\"",
            UseShellExecute = true
        });
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ExitWindowsEx(uint uFlags, uint dwReason);

    [DllImport("user32.dll")]
    private static extern void LockWorkStation();

    [DllImport("powrprof.dll", SetLastError = true)]
    private static extern bool SetSuspendState(bool hibernate, bool forceCritical, bool disableWakeEvent);
}
