using LanMountainDesktop.Services;

namespace LanMountainDesktop.Views.Components;

/// <summary>
/// 学习组件生命周期里被各抄一遍的不变量，目前两条：detach 的固定四步、活跃页上下文的固定三步。
/// 第一件是 detach 的固定四步：落"未挂载"状态位 → 放监测租约 → 清渲染门 → 退订快照事件。
/// 顺序是有意的：租约与退订都发生在 _isAttached=false 之后，反过会让协调器以为还有活着的页面。
/// 这四步此前在 6 个学习组件里逐字各抄一份——漏抄最后一行就是一个还在被回调的事件泄漏
/// （本仓真发生过两次：组件库预览换选中项、组件浮窗关停）。
/// </summary>
internal static class StudyComponentLifecycle
{
    internal static void Detach(
        ref bool isAttached,
        ref IDisposable? monitoringLease,
        ref bool isSubscribed,
        IStudyAnalyticsService studyAnalyticsService,
        StudySnapshotRenderGate renderGate)
    {
        isAttached = false;
        StudyMonitoringLease.Release(ref monitoringLease);
        renderGate.Clear();
        StudySnapshotSubscription.Unsubscribe(ref isSubscribed, studyAnalyticsService, renderGate.HandleSnapshotUpdated);
    }

    /// <summary>
    /// 学习组件"在不在活跃页"的固定三步：落状态位 → 重算监测租约 → 只在"从不在到在"的那一刻补一次刷新。
    /// 这六份各抄一遍时唯一的差别就是最后那一下刷新（4 个重画视觉、2 个把最新快照排进渲染门），
    /// 中间的"先重算租约、再判首次进入"顺序没人抄错，也没人测过。
    /// <paramref name="isEditMode"/> 学习组件一律不看，集中在这里吃掉：
    /// 调用方就无需各写一句 <c>_ = isEditMode;</c>，一处能看出"编辑态对学习组件无意义"这个口径。
    /// </summary>
    internal static void ApplyPageContext(
        ref bool cachedIsOnActivePage,
        bool isOnActivePage,
        bool isEditMode,
        Action updateMonitoringLeaseState,
        Action refreshOnFirstEntry)
    {
        _ = isEditMode;

        var wasOnActivePage = cachedIsOnActivePage;
        cachedIsOnActivePage = isOnActivePage;
        updateMonitoringLeaseState();

        if (isOnActivePage && !wasOnActivePage)
        {
            refreshOnFirstEntry();
        }
    }
}
