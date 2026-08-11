using System;
using LanMountainDesktop.Platform.Windows;

namespace LanMountainDesktop.Services;

/// <summary>
/// 原生对话框门面。P/Invoke 实现位于 Platform.Windows（WindowsNativeDialogs）。
/// </summary>
internal static class WindowsNativeDialogService
{
    public static void ShowInformation(string caption, string message)
    {
        Show(caption, message, WindowsNativeDialogs.Ok | WindowsNativeDialogs.IconInformation, "NativeDialog");
    }

    public static void ShowWarning(string caption, string message)
    {
        Show(caption, message, WindowsNativeDialogs.Ok | WindowsNativeDialogs.IconWarning, "StartupDiagnostics");
    }

    private static void Show(string caption, string message, uint type, string logCategory)
    {
        try
        {
            WindowsNativeDialogs.Show(caption, message, type);
        }
        catch (Exception ex)
        {
            AppLogger.Warn(logCategory, "Failed to show native dialog.", ex);
        }
    }
}
