using System.Runtime.InteropServices;

namespace LanMountainDesktop.Platform.Windows;

/// <summary>
/// DWM（桌面窗口管理器）互操作封装。
/// </summary>
public static class WindowsDwmInterop
{
    public const int WindowAttributeBorderColor = 34;
    public const uint ColorNone = 0xFFFFFFFE;

    /// <summary>
    /// 移除窗口原生边框颜色（Windows 11 22000+）。
    /// 失败静默忽略（DWM 属性为尽力而为语义）。
    /// </summary>
    public static void TryDisableWindowBorder(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero || !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            return;
        }

        try
        {
            var borderColor = ColorNone;
            _ = DwmSetWindowAttribute(
                windowHandle,
                WindowAttributeBorderColor,
                ref borderColor,
                sizeof(uint));
        }
        catch
        {
            // DWM attributes are best-effort and unavailable on older/unsupported Windows builds.
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr windowHandle,
        int attribute,
        ref uint attributeValue,
        int attributeSize);
}
