using System.Threading;

using Avalonia.Threading;

using LanMountainDesktop.Shared.Threading;

namespace LanMountainDesktop.Views.Components;

/// <summary>
/// 组件"离开视觉树"时要收回的三件事唯一的家：附着标志、刷新计时器、还在飞的刷新请求。
/// 实测 10 个组件各写一遍这个三连（其中 6 处整段逐字相同，另 4 处把各自的位图释放夹在后面）。
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
}
