using LanMountainDesktop.Services;
using LanMountainDesktop.Views.Components;
using Xunit;

namespace LanMountainDesktop.Tests;

/// <summary>
/// 组件与应用级时区服务之间那对订阅/退订的口径（10 个组件原来各抄一份，抄漏 <c>-=</c> 就把整棵
/// 已分离的 visual tree 挂在服务上）。这里既钉 binding 本身，也钉"预览丢掉时确实退订了"。
/// </summary>
public sealed class TimeZoneServiceBindingTests
{
    private static readonly TimeZoneInfo OtherZone =
        TimeZoneInfo.CreateCustomTimeZone("LanDesktopTestZone", TimeSpan.FromHours(5), "test", "test");

    [Fact]
    public void Replace_SubscribesAndRoutesChangesToTheHandler()
    {
        var service = new TimeZoneService();
        var hits = 0;
        EventHandler handler = (_, _) => hits++;

        var current = TimeZoneServiceBinding.Replace(null, service, handler);

        Assert.Same(service, current);
        service.CurrentTimeZone = OtherZone;
        Assert.Equal(1, hits);
    }

    [Fact]
    public void Replace_LeavesNoStaleSubscriptionOnThePreviousService()
    {
        var first = new TimeZoneService();
        var second = new TimeZoneService();
        object? raisedBy = null;
        EventHandler handler = (sender, _) => raisedBy = sender;

        var current = TimeZoneServiceBinding.Replace(null, first, handler);
        current = TimeZoneServiceBinding.Replace(current, second, handler);

        Assert.Same(second, current);
        first.CurrentTimeZone = OtherZone;
        Assert.Null(raisedBy);

        second.CurrentTimeZone = OtherZone;
        Assert.Same(second, raisedBy);
    }

    [Fact]
    public void Clear_UnsubscribesAndIsSafeToCallTwice()
    {
        var service = new TimeZoneService();
        var hits = 0;
        EventHandler handler = (_, _) => hits++;

        var current = TimeZoneServiceBinding.Replace(null, service, handler);
        current = TimeZoneServiceBinding.Clear(current, handler);

        Assert.Null(current);
        // detach 与 dispose 两条路都会调 Clear，第二次不能炸、也不能留下订阅
        Assert.Null(TimeZoneServiceBinding.Clear(current, handler));

        service.CurrentTimeZone = OtherZone;
        Assert.Equal(0, hits);
    }

    [Fact]
    public void Replace_WithNull_ReleasesWithoutThrowing()
    {
        var service = new TimeZoneService();
        EventHandler handler = (_, _) => { };

        var current = TimeZoneServiceBinding.Replace(null, service, handler);

        // 收口前 SetTimeZoneService(null) 会在 += 那行抛 NullReferenceException
        Assert.Null(TimeZoneServiceBinding.Replace(current, null, handler));
        service.CurrentTimeZone = OtherZone;
    }
}
