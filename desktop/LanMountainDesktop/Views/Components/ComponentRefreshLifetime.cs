using System.Threading;

using Avalonia.Threading;

using LanMountainDesktop.Shared.Threading;

namespace LanMountainDesktop.Views.Components;

/// <summary>
/// 组件那根 <see cref="DispatcherTimer"/> 唯一的家：起（按设置重排间隔并决定起停）与收
/// （"离开视觉树"时要收回的三件事：附着标志、刷新计时器、还在飞的刷新请求）。
/// 实测 10 个组件各写一遍那个三连（其中 6 处整段逐字相同，另 4 处把各自的位图释放夹在后面），
/// 另有 7 个组件各抄一遍那 14 行起停判据。
/// 顺序有意义：先停表再取消释放，反了会让下一次 tick 拿到已经 Dispose 的 token。
/// </summary>
internal static class ComponentRefreshLifetime
{
    /// <summary>
    /// <paramref name="afterDetach"/> 是给该组件自己的收尾（如刷新按钮态）留的口子：
    /// 不收进来会让"三连 + 一句 UI 收尾"这种最常见的组合继续各抄一份两行同文。
    /// </summary>
    public static void Detach(
        ref bool isAttached,
        DispatcherTimer refreshTimer,
        ref CancellationTokenSource? refreshCts,
        Action? afterDetach = null)
    {
        isAttached = false;
        refreshTimer.Stop();
        CancellationHelper.CancelAndDispose(ref refreshCts);
        afterDetach?.Invoke();
    }

    /// <summary>
    /// 按设置重排这根刷新/轮播表：先写间隔，再**只在已经附着时**决定起停。
    /// 实测 7 个组件各抄了一遍这 14 行（6 个自动刷新 + 1 个自动轮播），抄本之间的差异全在"读哪条设置"，
    /// 起停这一段一个字都没漂——所以起停规则只有这一处能说。
    /// 不附着就不起表：`Detach` 之后 `Start()` 会让一个已经离开视觉树的控件继续被 tick，
    /// 而它的回调会去碰已经没有父级的控件（组件浮窗关停那次泄漏就是这个形状）。
    /// </summary>
    public static void Reschedule(
        DispatcherTimer refreshTimer,
        bool isAttached,
        bool enabled,
        int intervalMinutes)
    {
        refreshTimer.Interval = TimeSpan.FromMinutes(intervalMinutes);

        if (!isAttached)
        {
            return;
        }

        if (enabled)
        {
            if (!refreshTimer.IsEnabled)
            {
                refreshTimer.Start();
            }
        }
        else if (refreshTimer.IsEnabled)
        {
            refreshTimer.Stop();
        }
    }
}
