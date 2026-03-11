using System;
using System.Runtime.InteropServices;

namespace LanMountainDesktop.Services;

internal static class WindowsNativeDialogService
{
    private const uint Ok = 0x00000000;
    private const uint IconInformation = 0x00000040;
    private const uint IconWarning = 0x00000030;

    public static void ShowInformation(string caption, string message)
    {
        Show(caption, message, Ok | IconInformation, "NativeDialog");
    }

    public static void ShowWarning(string caption, string message)
    {
        Show(caption, message, Ok | IconWarning, "StartupDiagnostics");
    }

    private static void Show(string caption, string message, uint type, string logCategory)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            _ = MessageBoxW(IntPtr.Zero, message, caption, type);
        }
        catch (Exception ex)
        {
            AppLogger.Warn(logCategory, "Failed to show native dialog.", ex);
        }
    }

    [DllImport("user32.dll", EntryPoint = "MessageBoxW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);
}
