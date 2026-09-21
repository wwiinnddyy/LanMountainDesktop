using System;

namespace LanMountainDesktop.Services;

/// <summary>
/// 学习组件订阅快照事件的开关：挂上桌面时订一次，摘掉桌面或销毁时必须解掉，
/// 否则已经卸载的组件还会被回调。此前 8 个学习组件各自维护 <c>_isSubscribed</c> 标志，
/// 同样的四行判断写了 21 遍，漏掉任何一处就是一个隐形的事件泄漏。
/// </summary>
public static class StudySnapshotSubscription
{
    public static void Subscribe(
        ref bool subscribed,
        IStudyAnalyticsService service,
        EventHandler<StudyAnalyticsSnapshotChangedEventArgs> handler)
    {
        if (subscribed)
        {
            return;
        }

        service.SnapshotUpdated += handler;
        subscribed = true;
    }

    public static void Unsubscribe(
        ref bool subscribed,
        IStudyAnalyticsService service,
        EventHandler<StudyAnalyticsSnapshotChangedEventArgs> handler)
    {
        if (!subscribed)
        {
            return;
        }

        service.SnapshotUpdated -= handler;
        subscribed = false;
    }
}
