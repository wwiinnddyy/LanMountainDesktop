using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace LanMountainDesktop.Platform;

/// <summary>
/// "桌面图标层是哪个窗口"这件事的唯一判据：先在各顶层窗口的 <c>WorkerW</c> 里找
/// <c>SHELLDLL_DefView</c>，找不到再到顶层窗口自己底下找，两边都没有就给 <see cref="IntPtr.Zero"/>。
///
/// 此前 <c>WindowsMainWindowDesktopLayerService</c> 与 <c>WindowsWindowPassthroughServices</c>
/// 各抄一份逐字相同的 28 行（连带 <c>EnumWindows</c>／<c>FindWindowEx</c> 两套 P/Invoke）。
/// 两份各自一条命的后果是这两种错法都不报错：
/// 只留第一趟，壁纸窗没开 <c>WorkerW</c> 的那台机器上图标层找不到，窗口挂不到桌面上；
/// 两趟顺序反了，会先把"顶层直挂"的那个当成宿主，而它并不是画图标的那一层——
/// 症状是窗口确实挂上了、桌面图标却盖在它上面（或者被它盖住）。
///
/// 判据与 Win32 取数分开：<see cref="ResolveFrom(IReadOnlyList{IntPtr}, Func{IntPtr, string?, IntPtr})"/>
/// 是纯的两趟查找、可注入可测；<see cref="Resolve()"/> 只负责真调 <c>EnumWindows</c>。
/// </summary>
public static class DesktopIconHost
{
    private const string WorkerWClassName = "WorkerW";
    private const string DefViewClassName = "SHELLDLL_DefView";

    /// <summary>遍历当前所有顶层窗口，按两趟判据取图标层宿主；拿不到就是 <see cref="IntPtr.Zero"/>。</summary>
    public static IntPtr Resolve()
    {
        var topLevelWindows = new List<IntPtr>();
        EnumWindows(
            (handle, _) =>
            {
                topLevelWindows.Add(handle);
                return true;
            },
            IntPtr.Zero);

        return ResolveFrom(topLevelWindows, FindChildByClass);
    }

    /// <summary>
    /// 两趟判据本体。<paramref name="findChild"/> 只按类名取第一个子窗口，
    /// 所以这里能离线钉住"先看 WorkerW 嵌套、再看顶层直挂、都没有给 0"这条顺序。
    /// </summary>
    public static IntPtr ResolveFrom(IReadOnlyList<IntPtr> topLevelWindows, Func<IntPtr, string?, IntPtr> findChild)
    {
        foreach (var topLevelWindow in topLevelWindows)
        {
            var worker = findChild(topLevelWindow, WorkerWClassName);
            if (worker == IntPtr.Zero)
            {
                continue;
            }

            var defView = findChild(worker, DefViewClassName);
            if (defView != IntPtr.Zero)
            {
                return defView;
            }
        }

        foreach (var topLevelWindow in topLevelWindows)
        {
            var defView = findChild(topLevelWindow, DefViewClassName);
            if (defView != IntPtr.Zero)
            {
                return defView;
            }
        }

        return IntPtr.Zero;
    }

    private static IntPtr FindChildByClass(IntPtr parent, string? className) =>
        FindWindowEx(parent, IntPtr.Zero, className, null);

    private delegate bool EnumWindowsProc(IntPtr handle, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr FindWindowEx(IntPtr hParent, IntPtr hChildAfter, string? lpszClass, string? lpszWindow);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
}
