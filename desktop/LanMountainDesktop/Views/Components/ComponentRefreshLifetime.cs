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
    /// <see cref="Detach"/> 的反向：<b>挂上台面</b>那三步——落"已附着"位 → 按设置把表起停 → 立刻起一次取数，
    /// 中间留一个 <paramref name="afterArming"/> 口子给各自的控件收尾（画刷新按钮态、刷模式视觉）。
    /// 顺序两处都是判据，且都只有一种症状："不报错，但那件事不发生"：
    /// <list type="bullet">
    /// <item><description>状态位必须最先落。各组件的 <c>Refresh…Async</c> 第一句就是
    /// <c>if (!_isAttached || _isRefreshing) return;</c>（实测 9 个组件逐字如此），
    /// 把落位放到后面，这次取数<b>整个不发</b>——面板放上台面是空的，要等第一次 tick。
    /// 同一句也被 <see cref="Reschedule"/> 用（不附着就只写间隔、不起表）。</description></item>
    /// <item><description>取数必须排在收尾之后：按钮态是先画的，取数回来失败时才会被画成失败态；
    /// 反过来就出现"按钮还是正常的、内容已经报错"。</description></item>
    /// </list>
    /// "发出去不管"（<c>_ = …</c>）也收在这里：那一步抛出去会把 attach 变成崩溃。
    /// </summary>
    public static void Attach(
        ref bool isAttached,
        Action armRefreshTimer,
        Action? afterArming,
        Func<Task> kickFirstLoad)
    {
        isAttached = true;
        armRefreshTimer();
        afterArming?.Invoke();
        _ = kickFirstLoad();
    }

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
