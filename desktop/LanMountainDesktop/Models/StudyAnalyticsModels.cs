using System;
using System.Collections.Generic;

namespace LanMountainDesktop.Models;

public enum StudyAnalyticsRuntimeState
{
    Unsupported = 0,
    Ready = 1,
    Running = 2,
    Paused = 3,
    Error = 4
}

public enum NoiseStreamStatus
{
    Initializing = 0,
    Quiet = 1,
    Noisy = 2,
    Error = 3
}

public enum StudySessionRuntimeState
{
    Idle = 0,
    Running = 1,
    Completed = 2,
    Error = 3
}

public enum StudyDataMode
{
    Realtime = 0,
    SessionRunning = 1,
    SessionReport = 2
}

public sealed record StudyAnalyticsConfig(
    int FrameMs = 50,
    int UiPublishIntervalMs = 125,
    int SliceSec = 30,
    double ScoreThresholdDbfs = -50,
    int SegmentMergeGapMs = 500,
    int MaxSegmentsPerMin = 6,
    double SilenceFloorDbfs = -90,
    double BaselineDb = 45,
    bool ShowRelativeDb = true,
    bool AlertSoundEnabled = false,
    int AvgWindowSec = 1,
    int RealtimeBufferCapacity = 240)
{
    /// <summary>
    /// 把这组参数按各自合法区间钳一遍——**合法区间归这个类型自己管**，唯一一份。
    /// 此前 <c>NoiseFramePipeline</c> 与 <c>StudyAnalyticsService</c> 各抄一份逐字相同的 23 行：
    /// 两处在不同文件、同一个二进制里，改一边忘一边是必然，而症状不报错——
    /// 同一份 <c>settings.json</c> 里的学习分析参数，流水线按一套区间跑、服务层按另一套区间显示与保存，
    /// 于是"设了没生效"或者"下一次保存把用户的值改得像被谁动过"。
    /// 区间本身没在这笔里改（要改的是策略，得拍板），只是把"谁能说这几个数合法"收成一处。
    /// </summary>
    public StudyAnalyticsConfig ClampedToLegalRanges()
    {
        var frameMs = Math.Clamp(FrameMs, 20, 250);
        var uiPublishIntervalMs = Math.Clamp(UiPublishIntervalMs, 50, 500);
        var sliceSec = Math.Clamp(SliceSec, 5, 600);
        var threshold = Math.Clamp(ScoreThresholdDbfs, -100, -5);
        var mergeGapMs = Math.Clamp(SegmentMergeGapMs, 100, 4000);
        var maxSegments = Math.Clamp(MaxSegmentsPerMin, 1, 40);
        var silenceFloor = Math.Clamp(SilenceFloorDbfs, -100, -20);
        var baselineDb = Math.Clamp(BaselineDb, 20, 90);
        var avgWindowSec = Math.Clamp(AvgWindowSec, 1, 8);
        var ringCapacity = Math.Clamp(RealtimeBufferCapacity, 60, 1200);

        return this with
        {
            FrameMs = frameMs,
            UiPublishIntervalMs = uiPublishIntervalMs,
            SliceSec = sliceSec,
            ScoreThresholdDbfs = threshold,
            SegmentMergeGapMs = mergeGapMs,
            MaxSegmentsPerMin = maxSegments,
            SilenceFloorDbfs = silenceFloor,
            BaselineDb = baselineDb,
            AvgWindowSec = avgWindowSec,
            RealtimeBufferCapacity = ringCapacity
        };
    }
}

public sealed record NoiseRealtimePoint(
    DateTimeOffset Timestamp,
    double Rms,
    double Dbfs,
    double DisplayDb,
    double Peak,
    bool IsOverThreshold);

public sealed record NoiseSliceRawStats(
    double AvgDbfs,
    double MaxDbfs,
    double P50Dbfs,
    double P95Dbfs,
    double OverRatioDbfs,
    int SegmentCount,
    double SampledDurationMs,
    int GapCount,
    double MaxGapMs);

public sealed record NoiseSliceDisplayStats(
    double AvgDb,
    double P95Db);

public sealed record NoiseScoreBreakdown(
    double SustainedPenalty,
    double TimePenalty,
    double SegmentPenalty,
    double TotalPenalty,
    double Score,
    double SustainedLevelDbfs,
    double OverRatioDbfs,
    int SegmentCount,
    double Minutes,
    double DurationMs);

public sealed record NoiseSliceSummary(
    DateTimeOffset StartAt,
    DateTimeOffset EndAt,
    int FrameCount,
    NoiseSliceRawStats Raw,
    NoiseSliceDisplayStats Display,
    double Score,
    NoiseScoreBreakdown ScoreDetail);

public enum NoiseSliceSourceType
{
    Realtime = 0,
    Session = 1
}

public sealed record NoiseSliceTimelineEntry(
    long TimelineId,
    NoiseSliceSourceType SourceType,
    string? SessionId,
    NoiseSliceSummary Slice);

public sealed record StudySessionOptions(
    string? Label = null,
    DateTimeOffset? PlannedEndAt = null);

public sealed record StudySessionMetrics(
    double CurrentScore,
    double AvgScore,
    double MinScore,
    double MaxScore,
    double WeightedOverRatioDbfs,
    int TotalSegmentCount,
    TimeSpan EffectiveDuration,
    int SliceCount);

public sealed record StudySessionSnapshot(
    StudySessionRuntimeState State,
    string? SessionId,
    string Label,
    DateTimeOffset? StartedAt,
    DateTimeOffset? EndedAt,
    TimeSpan Elapsed,
    StudySessionMetrics Metrics,
    string LastError);

public sealed record StudySessionReport(
    string SessionId,
    string Label,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    TimeSpan Duration,
    StudySessionMetrics Metrics,
    IReadOnlyList<NoiseSliceSummary> Slices);

public sealed record StudySessionHistoryEntry(
    string SessionId,
    string Label,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    TimeSpan Duration,
    double AverageScore,
    int SliceCount);

public sealed record StudyAnalyticsSnapshot(
    StudyAnalyticsRuntimeState State,
    NoiseStreamStatus StreamStatus,
    StudyDataMode DataMode,
    StudyAnalyticsConfig Config,
    NoiseRealtimePoint? LatestRealtimePoint,
    NoiseSliceSummary? LatestSlice,
    IReadOnlyList<NoiseRealtimePoint> RealtimeBuffer,
    StudySessionSnapshot Session,
    StudySessionReport? LastSessionReport,
    string? SelectedSessionReportId,
    IReadOnlyList<StudySessionHistoryEntry> SessionHistory,
    string LastError);
