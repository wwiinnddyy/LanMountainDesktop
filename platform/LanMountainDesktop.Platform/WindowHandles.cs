using System;

using Avalonia.Controls;

namespace LanMountainDesktop.Platform.Windows;

/// <summary>
/// "从这个窗口拿一个能交给 Win32 的句柄"——唯一一份实现。
/// 此前主窗体层级服务与穿透服务各抄了一份逐字相同的 8 行（7 个调用点）。
///
/// 拿不到句柄时回 <see cref="IntPtr.Zero"/> 而不是抛：这两家的调用方全是
/// "拿到 0 就跳过这次 Win32 调用"的形状（窗口还没实化、正在关闭都会走到这里），
/// 在这里抛出去只会把一次装饰/穿透刷新变成崩溃。catch 也是同一层意思——
/// <c>TryGetPlatformHandle</c> 在非 Windows 宿主或已销毁的窗口上会抛，不是我们的错误。
/// </summary>
public static class WindowHandles
{
    public static IntPtr OfWindow(Window window)
    {
        try
        {
            return window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        }
        catch
        {
            return IntPtr.Zero;
        }
    }
}
