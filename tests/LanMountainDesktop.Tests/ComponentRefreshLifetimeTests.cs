using System.Threading;

using Avalonia.Headless.XUnit;
using Avalonia.Threading;

using LanMountainDesktop.Views.Components;

using Xunit;

namespace LanMountainDesktop.Tests;

/// <summary>
/// 组件刷新计时器生命周期的家唯一的证据：收（detach 三连＝置附着标志 + 停表 + 取消并释放刷新请求）
/// 与起（<c>Reschedule</c>＝按设置重排间隔并决定起停）。
///
/// 为什么收口的同时要补这一条：11 个组件各写一遍 detach 三连、7 个组件各写一遍 14 行起停判据时，
/// 正确性靠"抄得对"；收进家之后，
/// 少一步（尤其漏 <c>Stop()</c>）就同时影响 11 个组件、而且不报错——组件控件短命、
/// 计时器与在飞的请求长命，停表这一步没做，分离后的控件会被回调继续拽着。
/// </summary>
public sealed class ComponentRefreshLifetimeTests
{
    [AvaloniaFact]
    public void Detach_StopsTimer_CancelsAndDisposesToken_ThenRunsTheComponentCleanup()
    {
        var isAttached = true;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
        timer.Start();
        Assert.True(timer.IsEnabled);

        var source = new CancellationTokenSource();
        var token = source.Token;
        CancellationTokenSource? held = source;
        var cleanupRan = false;

        ComponentRefreshLifetime.Detach(ref isAttached, timer, ref held, () => cleanupRan = true);

        Assert.False(isAttached);
        Assert.False(timer.IsEnabled);
        Assert.True(token.IsCancellationRequested);
        Assert.True(cleanupRan);
        // 字段必须被清空（否则调用点会拿同一个源再取消一次，抛 ObjectDisposedException）…
        Assert.Null(held);
        // …而这个源必须真的被释放过，不只是取消：再取消一次就红。
        Assert.Throws<ObjectDisposedException>(() => source.Cancel());
    }

    [AvaloniaFact]
    public void Detach_WithoutACleanupStep_StillDoesAllThree()
    {
        var isAttached = true;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
        timer.Start();
        CancellationTokenSource? held = new();

        ComponentRefreshLifetime.Detach(ref isAttached, timer, ref held);

        Assert.False(isAttached);
        Assert.False(timer.IsEnabled);
        Assert.Null(held);
    }

    [AvaloniaFact]
    public void Detach_BeforeAnyRefreshWasStarted_IsSafe()
    {
        // 真实路径：组件挂上视觉树后还没触发过刷新就被摘掉——此时 _refreshCts 一直是 null。
        var isAttached = true;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
        CancellationTokenSource? held = null;

        ComponentRefreshLifetime.Detach(ref isAttached, timer, ref held);

        Assert.False(isAttached);
        Assert.Null(held);
    }

    /// <summary>
    /// <c>Reschedule</c> 的四格判据。收口前这是 7 个组件各抄 14 行的内容，
    /// 抄本之间从没漂过——但"没漂"是靠人抄，不是靠有地方能红。
    /// </summary>
    [AvaloniaFact]
    public void Reschedule_WritesInterval_AndStartsTheTimerWhenAttachedAndEnabled()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };

        ComponentRefreshLifetime.Reschedule(timer, isAttached: true, enabled: true, intervalMinutes: 20);

        Assert.Equal(TimeSpan.FromMinutes(20), timer.Interval);
        Assert.True(timer.IsEnabled);
        timer.Stop();
    }

    [AvaloniaFact]
    public void Reschedule_StopsARunningTimer_WhenAttachedButDisabled()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        timer.Start();

        ComponentRefreshLifetime.Reschedule(timer, isAttached: true, enabled: false, intervalMinutes: 15);

        Assert.False(timer.IsEnabled);
        // 间隔照写：下一次附着时该按新间隔起表。
        Assert.Equal(TimeSpan.FromMinutes(15), timer.Interval);
    }

    [AvaloniaFact]
    public void Reschedule_WhileDetached_WritesTheIntervalButDoesNotStart()
    {
        // 这一格是收口的真正理由：detach 之后 Start() 会让一个不在视觉树里的控件继续被 tick，
        // 回调再去碰已经没有父级的控件——组件浮窗关停那次泄漏就是这个形状。
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };

        ComponentRefreshLifetime.Reschedule(timer, isAttached: false, enabled: true, intervalMinutes: 30);

        Assert.False(timer.IsEnabled);
        Assert.Equal(TimeSpan.FromMinutes(30), timer.Interval);
    }

    [AvaloniaFact]
    public void Reschedule_WhileDetached_LeavesARunningTimerAlone_AsBefore()
    {
        // 钉住"现状"而不是"应该是"：原样是提前 return，所以不附着时连停表都不做。
        // 真实路径上 _isAttached=false 只发生在 Detach 之后，而 Detach 已经停过表了，
        // 于是这一格平时看不见。要改成"不附着也一律停表"是行为变更，得单独拍。
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        timer.Start();

        ComponentRefreshLifetime.Reschedule(timer, isAttached: false, enabled: false, intervalMinutes: 15);

        Assert.True(timer.IsEnabled);
        timer.Stop();
    }
}
