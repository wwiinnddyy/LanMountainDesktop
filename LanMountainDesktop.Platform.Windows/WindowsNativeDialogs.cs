using System.Runtime.InteropServices;

namespace LanMountainDesktop.Platform.Windows;

/// <summary>
/// Windows 原生消息框（MessageBoxW）封装。
/// 供宿主在 UI 框架尚不可用（启动早期）时显示诊断信息。
/// </summary>
public static class WindowsNativeDialogs
{
    public const uint Ok = 0x00000000;
    public const uint IconInformation = 0x00000040;
    public const uint IconWarning = 0x00000030;

    /// <summary>
    /// 显示原生消息框。仅在 Windows 上有效，其他平台为空操作。
    /// </summary>
    public static void Show(string caption, string message, uint type)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        _ = MessageBoxW(IntPtr.Zero, message, caption, type);
    }

    [DllImport("user32.dll", EntryPoint = "MessageBoxW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);
}
