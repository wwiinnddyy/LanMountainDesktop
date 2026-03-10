using System;
using System.Runtime.InteropServices;

namespace LanMountainDesktop.Services;

internal static class WindowsNativeDialogService
{
    private const uint Ok = 0x00000000;
    private const uint IconWarning = 0x00000030;

    public static void ShowWarning(string caption, string message)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            _ = MessageBoxW(IntPtr.Zero, message, caption, Ok | IconWarning);
        }
        catch (Exception ex)
        {
            AppLogger.Warn("StartupDiagnostics", "Failed to show legacy executable warning dialog.", ex);
        }
    }

    [DllImport("user32.dll", EntryPoint = "MessageBoxW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);
}
