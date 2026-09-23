using System;

using Avalonia.Controls;
using Avalonia.Headless.XUnit;

using LanMountainDesktop.Platform.Windows;

using Xunit;

namespace LanMountainDesktop.Tests;

/// <summary>
/// 窗口句柄这一家的行为钉。两个平台服务此前各抄一份同样的 8 行：
/// 拿不到可用句柄时要给 0（调用方一律是"0 就跳过这次 Win32 调用"），不许把异常抛给装饰/穿透刷新。
/// 两格的期望值是 headless 上量出来的：窗口即使 Show 过，句柄值也是 0（没有真 HWND）。
///
/// 说清覆盖边界（两次变异都试过，实测都不红）：把 <c>?? IntPtr.Zero</c> 改成 <c>-1</c>、
/// 或把 <c>?.</c> 改成 <c>!.</code> 都不红——因为 headless 给的是"有平台句柄对象、Handle 值为 0"，
/// <c>?? 0</c> 与 <c>catch</c> 这两条支路在这里根本不走，只有真机 Windows 或非 Windows 宿主才会走到。
/// 所以这两格钉的是**能观察到的那半条契约**（"没有可用句柄 ⇒ 返回 0，且不抛"；
/// 把返回值改成别的数两格立刻红），不是整段实现。真要钉住 <c>catch</c> 那条，
/// 得给家开一个可注入句柄来源的口子——为一个 8 行助手改形状不值，先记在这儿。
/// </summary>
public sealed class WindowHandlesTests
{
    [AvaloniaFact]
    public void OfWindow_OnAnUnshownWindow_YieldsTheNoHandleSentinel()
    {
        Assert.Equal(IntPtr.Zero, WindowHandles.OfWindow(new Window()));
    }

    /// <summary>
    /// headless 上即使 Show 过也拿不到可用句柄（没有真 HWND），实测返回 0——
    /// 这一格钉的不是"框架给不给句柄"，而是**这条路径不许抛**：
    /// 窗口已实化却没有可用句柄时，必须还是回 0，让调用方按"0 就跳过"处理。
    /// </summary>
    [AvaloniaFact]
    public void OfWindow_OnAWindowWithoutAPlatformHandle_KeepsReturningTheSentinel()
    {
        var window = new Window { Width = 200, Height = 120 };
        window.Show();

        try
        {
            Assert.Equal(IntPtr.Zero, WindowHandles.OfWindow(window));
        }
        finally
        {
            window.Close();
        }
    }
}
