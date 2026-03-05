using System;
using Microsoft.Win32;

namespace LanMountainDesktop.Services;

public sealed class WindowsStartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "LanMountainDesktop";
    private readonly string _startupCommand;

    public WindowsStartupService()
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
        catch
        {
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
        catch
        {
            return false;
        }
    }
}
