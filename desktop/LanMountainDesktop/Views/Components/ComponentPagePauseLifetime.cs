using System;

using Avalonia.Threading;

namespace LanMountainDesktop.Views.Components;

/// <summary>
/// 组件对"桌面页可见性"（<see cref="IDesktopPageVisibilityAwareComponentWidget"/>）响应的唯一家。
/// 多页桌面里非当前页的组件<b>仍挂在视觉树上</b>（只是被平移出屏），DetachedFromVisualTree 不会来，
/// 组件必须按这一推送自己停表；不接的话 4 页桌面 × 每页一只钟就是 4 倍看不见的逐秒空转。
///
/// 判据两条，写错都不报错：落位必须最先（重入的相同推送直接跳过）；停表不问挂树、
/// 恢复必须确认还挂着（推送可能先于挂树到达：组件建在非当前页上时，恢复要等真正 Attach 那次自己做）。
/// 默认值取 true：推送不到的宿主（组件库预览等）维持"一直在当前页"的现状。
/// 计时器由家统一起停（与 <see cref="ComponentRefreshLifetime.Reschedule"/> 同一形状）：
/// 起表前先跑 <paramref name="onActive"/>，让恢复的第一帧就是新内容；
/// 这也是 ComponentLifecyclePairingRatchetTests 认这个家的形状——计时器名与 Apply 同行。
/// </summary>
internal static class ComponentPagePauseLifetime
{
    public static void Apply(
        ref bool isOnActivePage,
        bool next,
        bool isAttached,
        DispatcherTimer?[] timers,
        Action? onActive = null)
    {
        ArgumentNullException.ThrowIfNull(timers);

        if (isOnActivePage == next)
        {
            return;
        }

        isOnActivePage = next;
        if (!next)
        {
            foreach (var timer in timers)
            {
                timer?.Stop();
            }

            return;
        }

        if (!isAttached)
        {
            return;
        }

        onActive?.Invoke();
        foreach (var timer in timers)
        {
            timer?.Start();
        }
    }

    /// <summary>
    /// <see cref="Apply"/> 的另一半：离开视觉树时先落"已分离"位再停表。
    /// 顺序同样是判据——反了会让一次迟到的 tick 拿到错误的状态位。
    /// </summary>
    public static void Detach(ref bool isAttached, params DispatcherTimer?[] timers)
    {
        isAttached = false;
        foreach (var timer in timers)
        {
            timer?.Stop();
        }
    }
}
