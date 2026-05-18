using System;
using Microsoft.Win32;

namespace LanMountainDesktop.Launcher.Services;

/// <summary>
/// 将当前 Windows 用户登录时自启动项指向<strong>本 Launcher 进程</strong>（与正式入口一致）。
/// Host 内 WindowsStartupService 使用 Host 进程路径；
/// OOBE 在 Launcher 内执行时应使用本类型，以便开机后仍走更新/版本协调流程。
/// </summary>
public sealed class LauncherWindowsStartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "LanMountainDesktop";
    private readonly string _startupCommand;

    public LauncherWindowsStartupService()
    {
        var processPath = Environment.ProcessPath;
        _startupCommand = string.IsNullOrWhiteSpace(processPath)
            ? string.Empty
            : $"\"{processPath}\"";
    }

    public bool IsEnabled()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            using var runKey = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return runKey?.GetValue(ValueName) is string value &&
                   !string.IsNullOrWhiteSpace(value);
        }
        catch (Exception ex)
        {
            Logger.Warn($"LauncherWindowsStartup: failed to read Run key. {ex.Message}");
            return false;
        }
    }

    public bool SetEnabled(bool enabled)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        if (enabled && string.IsNullOrWhiteSpace(_startupCommand))
        {
            return false;
        }

        try
        {
            using var runKey = Registry.CurrentUser.CreateSubKey(RunKeyPath);
            if (runKey is null)
            {
                return false;
            }

            if (enabled)
            {
                runKey.SetValue(ValueName, _startupCommand, RegistryValueKind.String);
            }
            else
            {
                runKey.DeleteValue(ValueName, throwOnMissingValue: false);
            }

            return IsEnabled() == enabled;
        }
        catch (Exception ex)
        {
            Logger.Warn($"LauncherWindowsStartup: failed to set Run key. Enabled={enabled}. {ex.Message}");
            return false;
        }
    }
}
