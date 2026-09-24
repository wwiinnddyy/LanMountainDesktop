using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;

using LanMountainDesktop.Views.Components;

using Xunit;

namespace LanMountainDesktop.Tests;

/// <summary>
/// 学习组件生命周期家的行为钉，目前两条不变量各一组：
/// "在不在活跃页"（六个组件各抄一遍时从没被测过）与"尺寸变了要重算哪两样"（五个组件逐字各抄一遍）。
/// 前者要钉的是那个**只在首次进入时**才做的补刷新：少了守卫，翻回页面会重复排一次渲染/重画；
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

    /// <summary>
    /// 钉两条：<b>两件事都做</b>（先重排、再重绘），以及交给重绘的底色是<b>重排之后</b>现读的那一份。
    /// 后一条今天换不出行为差别（这五个组件的重排都不写面板底色），钉它是要把抄本原本的读点固定住：
    /// 家收的是 <see cref="Border"/> 本身而不是画刷，调用方就没法在重排之前把旧画刷读走。
    /// 此前这五行在 5 个学习组件里逐字各抄一份。
    /// </summary>
    [AvaloniaFact]
    public void RefreshOnResize_ReflowsFirst_AndHandsTheColorReadAfterwards()
    {
        var panel = new Border { Background = Brushes.Black };
        var order = new List<string>();
        Color? seen = null;

        StudyComponentLifecycle.RefreshOnResize(
            panel,
            () =>
            {
                order.Add("reflow");
                panel.Background = Brushes.White;
            },
            color =>
            {
                order.Add("repaint");
                seen = color;
            });

        Assert.Equal(["reflow", "repaint"], order);
        Assert.Equal(Colors.White, seen);
    }

    /// <summary>
    /// 挂载那五步里唯一有后果的一条：<b>状态位必须最先落</b>。
    /// <c>StudyMonitoringLease.Sync(…, isAttached, isOnActivePage)</c> 在 <c>!isAttached</c> 时走的是 Release
    /// （<c>Services/StudyAnalyticsMonitoringLeaseCoordinator.cs:110</c>），所以状态位落在重算租约之后，
    /// 组件"从桌面摘掉再放回来"那一次就拿不到租约——症状是学习监测不再采数，且不报错。
    /// 假服务是省掉的：传 <c>isSubscribed = true</c> 让家走进"已经订过"那一支，<c>null!</c> 就不会被碰。
    /// </summary>
    [Fact]
    public void Attach_FlipsTheAttachedFlagBeforeEveryStepThatReadsIt()
    {
        var isAttached = false;
        var isSubscribed = true;
        var order = new List<string>();
        var seenByReload = false;
        bool? seenByLease = null;
        bool? seenByRefresh = null;
        var renderGate = new StudySnapshotRenderGate(() => isAttached, _ => { });

        StudyComponentLifecycle.Attach(
            ref isAttached,
            ref isSubscribed,
            null!,
            renderGate,
            () =>
            {
                order.Add("reload");
                seenByReload = isAttached;
            },
            () =>
            {
                order.Add("lease");
                seenByLease = isAttached;
            },
            () =>
            {
                order.Add("refresh");
                seenByRefresh = isAttached;
            });

        Assert.Equal(["reload", "lease", "refresh"], order);
        Assert.True(seenByReload);
        Assert.True(seenByLease);
        Assert.True(seenByRefresh);
    }
}
