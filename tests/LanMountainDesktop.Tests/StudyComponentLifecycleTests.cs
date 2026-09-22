using LanMountainDesktop.Views.Components;

using Xunit;

namespace LanMountainDesktop.Tests;

/// <summary>
/// 学习组件"在不在活跃页"这条不变量的行为钉（六个组件各抄一遍时从没被测过）。
/// 要钉的是那个**只在首次进入时**才做的补刷新：少了守卫，翻回页面会重复排一次渲染/重画；
/// 写反了（比如用 was 而不是 !was），组件切回活跃页就永远不重画。
/// </summary>
public sealed class StudyComponentLifecycleTests
{
    [Fact]
    public void FirstTimeBecomingActive_RecomputesLeaseAndRefreshesOnce()
    {
        var isOnActivePage = false;
        var leaseUpdates = 0;
        var refreshes = 0;

        StudyComponentLifecycle.ApplyPageContext(
            ref isOnActivePage, true, false, () => leaseUpdates++, () => refreshes++);

        Assert.True(isOnActivePage);
        Assert.Equal(1, leaseUpdates);
        Assert.Equal(1, refreshes);
    }

    [Fact]
    public void StayingActive_RecomputesLeaseButDoesNotRefreshAgain()
    {
        var isOnActivePage = true;
        var leaseUpdates = 0;
        var refreshes = 0;

        StudyComponentLifecycle.ApplyPageContext(
            ref isOnActivePage, true, true, () => leaseUpdates++, () => refreshes++);

        Assert.Equal(1, leaseUpdates);
        Assert.Equal(0, refreshes);
    }

    [Fact]
    public void LeavingTheActivePage_RecomputesLeaseAndDoesNotRefresh()
    {
        var isOnActivePage = true;
        var refreshes = 0;

        StudyComponentLifecycle.ApplyPageContext(
            ref isOnActivePage, false, false, () => { }, () => refreshes++);

        Assert.False(isOnActivePage);
        Assert.Equal(0, refreshes);
    }

    [Fact]
    public void ComingBackLater_RefreshesAgain()
    {
        var isOnActivePage = false;
        var refreshes = 0;

        StudyComponentLifecycle.ApplyPageContext(
            ref isOnActivePage, true, false, () => { }, () => refreshes++);
        StudyComponentLifecycle.ApplyPageContext(
            ref isOnActivePage, false, false, () => { }, () => refreshes++);
        StudyComponentLifecycle.ApplyPageContext(
            ref isOnActivePage, true, false, () => { }, () => refreshes++);

        Assert.Equal(2, refreshes);
    }
}
