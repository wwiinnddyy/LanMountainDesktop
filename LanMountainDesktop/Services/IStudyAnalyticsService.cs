using System;
using System.Collections.Generic;
using LanMountainDesktop.Models;

namespace LanMountainDesktop.Services;

public sealed class StudyAnalyticsSnapshotChangedEventArgs(StudyAnalyticsSnapshot snapshot) : EventArgs
{
    public StudyAnalyticsSnapshot Snapshot { get; } = snapshot;
}

public sealed class NoiseSliceClosedEventArgs(NoiseSliceSummary slice) : EventArgs
{
    public NoiseSliceSummary Slice { get; } = slice;
}

public sealed class StudySessionCompletedEventArgs(StudySessionReport report) : EventArgs
{
    public StudySessionReport Report { get; } = report;
}

public interface IStudyAnalyticsService : IDisposable
{
    StudyAnalyticsSnapshot GetSnapshot();

    StudyAnalyticsConfig GetConfig();

    void UpdateConfig(StudyAnalyticsConfig config);

    bool StartOrResumeMonitoring();

    bool PauseMonitoring();

    bool StopMonitoring();

    bool StartStudySession(StudySessionOptions? options = null);

    bool StopStudySession();

    bool CancelStudySession();

    void ClearLastSessionReport();

    bool SelectSessionReport(string sessionId);

    bool RenameSessionReport(string sessionId, string label);

    bool DeleteSessionReport(string sessionId);

    IReadOnlyList<NoiseSliceTimelineEntry> QueryNoiseSliceTimeline(
        DateTimeOffset? startAt = null,
        DateTimeOffset? endAt = null,
        int limit = 720,
        bool includeRealtimeSlices = true,
        bool includeSessionSlices = true);

    void ClearNoiseSliceTimeline(DateTimeOffset? olderThan = null);

    event EventHandler<StudyAnalyticsSnapshotChangedEventArgs>? SnapshotUpdated;

    event EventHandler<NoiseSliceClosedEventArgs>? SliceClosed;

    event EventHandler<StudySessionCompletedEventArgs>? SessionCompleted;
}

