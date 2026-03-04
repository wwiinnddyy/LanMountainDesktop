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
    int SliceSec = 30,
    double ScoreThresholdDbfs = -50,
    int SegmentMergeGapMs = 500,
    int MaxSegmentsPerMin = 6,
    double SilenceFloorDbfs = -90,
    double BaselineDb = 45,
    bool ShowRelativeDb = true,
    bool AlertSoundEnabled = false,
    int AvgWindowSec = 1,
    int RealtimeBufferCapacity = 240);

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
    string LastError);
