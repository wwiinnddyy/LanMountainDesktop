using LanMountainDesktop.Services;

namespace LanMountainDesktop.Views.Components;

/// <summary>
/// 学习组件 detach 的固定四步：落"未挂载"状态位 → 放监测租约 → 清渲染门 → 退订快照事件。
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
}
