using System.Threading;

using Avalonia.Headless.XUnit;
using Avalonia.Threading;

using LanMountainDesktop.Views.Components;

using Xunit;

namespace LanMountainDesktop.Tests;

/// <summary>
/// 组件 detach 三连（置附着标志 + 停刷新表 + 取消并释放刷新请求）的家唯一的证据。
///
/// 为什么收口的同时要补这一条：11 个组件各写一遍时，正确性靠"抄得对"；收进家之后，
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
}
