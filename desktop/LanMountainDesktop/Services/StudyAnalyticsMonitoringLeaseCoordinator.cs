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
{
    private readonly object _syncRoot = new();
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
