using System;
using System.Threading;
using LanMountainDesktop.Models;

namespace LanMountainDesktop.Services;

public static class StudyAnalyticsMonitoringLeaseCoordinatorFactory
{
    private static readonly Lazy<StudyAnalyticsMonitoringLeaseCoordinator> SharedCoordinator = new(
        () => new StudyAnalyticsMonitoringLeaseCoordinator(),
        isThreadSafe: true);

    public static StudyAnalyticsMonitoringLeaseCoordinator CreateDefault()
    {
        return SharedCoordinator.Value;
    }
}

public sealed class StudyAnalyticsMonitoringLeaseCoordinator
{    private readonly object _syncRoot = new();
    private readonly IStudyAnalyticsService _studyAnalyticsService;
    private int _activeLeaseCount;

    public StudyAnalyticsMonitoringLeaseCoordinator(IStudyAnalyticsService? studyAnalyticsService = null)
    {
        _studyAnalyticsService = studyAnalyticsService ?? StudyAnalyticsServiceFactory.CreateDefault();
    }

    public IDisposable AcquireLease()
    {
        var shouldStartMonitoring = false;
        lock (_syncRoot)
        {
            _activeLeaseCount++;
            if (_activeLeaseCount == 1)
            {
                shouldStartMonitoring = true;
            }
        }

        if (shouldStartMonitoring)
        {
            _ = _studyAnalyticsService.StartOrResumeMonitoring();
        }

        return new MonitoringLease(this);
    }

    private void ReleaseLease()
    {
        var shouldPauseMonitoring = false;
        lock (_syncRoot)
        {
            if (_activeLeaseCount <= 0)
            {
                return;
            }

            _activeLeaseCount--;
            if (_activeLeaseCount == 0)
            {
                shouldPauseMonitoring = true;
            }
        }

        if (!shouldPauseMonitoring)
        {
            return;
        }

        var snapshot = _studyAnalyticsService.GetSnapshot();
        if (snapshot.Session.State != StudySessionRuntimeState.Running)
        {
            _ = _studyAnalyticsService.PauseMonitoring();
        }
    }

    private sealed class MonitoringLease : IDisposable
    {
        private StudyAnalyticsMonitoringLeaseCoordinator? _owner;

        public MonitoringLease(StudyAnalyticsMonitoringLeaseCoordinator owner)
        {
            _owner = owner;
        }

        public void Dispose()
        {
            var owner = Interlocked.Exchange(ref _owner, null);
            owner?.ReleaseLease();
        }
    }
}

/// <summary>
/// 学习组件什么时候持有监测租约，只认这一处判定：学习开关开着、组件挂在桌面上、
/// 且它所在页是当前页——三条缺一就释放。此前 7 个学习组件各抄了一份一模一样的
/// 判定和"<c>Dispose</c> 再置空"序列，改一处就会漏改六处。
/// </summary>
public static class StudyMonitoringLease
{
    public static void Sync(
        ref IDisposable? lease,
        StudyAnalyticsMonitoringLeaseCoordinator coordinator,
        bool studyEnabled,
        bool isAttached,
        bool isOnActivePage)
    {
        if (!studyEnabled || !isAttached || !isOnActivePage)
        {
            Release(ref lease);
            return;
        }

        lease ??= coordinator.AcquireLease();
    }

    public static void Release(ref IDisposable? lease)
    {
        lease?.Dispose();
        lease = null;
    }
}
