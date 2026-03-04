using System;
using System.Collections.Generic;
using System.Linq;
using LanMountainDesktop.Models;

namespace LanMountainDesktop.Services;

internal readonly record struct NoisePipelineTickResult(
    NoiseRealtimePoint RealtimePoint,
    NoiseSliceSummary? ClosedSlice);

internal sealed class NoiseFramePipeline
{
    private StudyAnalyticsConfig _config;
    private readonly Queue<NoiseRealtimePoint> _realtimeBuffer = new();
    private readonly List<NoiseRealtimePoint> _slicePoints = [];

    private DateTimeOffset _sliceStartAt;
    private DateTimeOffset _lastFrameAt;
    private DateTimeOffset _lastOverThresholdAt;

    private int _overThresholdFrameCount;
    private int _segmentCount;
    private bool _segmentOpen;
    private int _gapCount;
    private double _maxGapMs;

    public NoiseFramePipeline(StudyAnalyticsConfig config)
    {
        _config = NormalizeConfig(config);
    }

    public void UpdateConfig(StudyAnalyticsConfig config)
    {
        _config = NormalizeConfig(config);
        Reset();
    }

    public void Reset()
    {
        _realtimeBuffer.Clear();
        _slicePoints.Clear();
        _sliceStartAt = default;
        _lastFrameAt = default;
        _lastOverThresholdAt = default;
        _overThresholdFrameCount = 0;
        _segmentCount = 0;
        _segmentOpen = false;
        _gapCount = 0;
        _maxGapMs = 0;
    }

    public IReadOnlyList<NoiseRealtimePoint> GetRealtimeBufferSnapshot()
    {
        return _realtimeBuffer.ToArray();
    }

    public NoisePipelineTickResult AddFrame(DateTimeOffset timestamp, double rms, double dbfs, double displayDb, double peak)
    {
        if (_sliceStartAt == default)
        {
            _sliceStartAt = timestamp;
        }

        if (_lastFrameAt != default)
        {
            var actualGapMs = (timestamp - _lastFrameAt).TotalMilliseconds;
            var expectedGapMs = _config.FrameMs;
            var jitterMs = Math.Max(0, actualGapMs - expectedGapMs);
            if (jitterMs > Math.Max(12, expectedGapMs * 0.8))
            {
                _gapCount++;
                _maxGapMs = Math.Max(_maxGapMs, jitterMs);
            }
        }

        _lastFrameAt = timestamp;

        var isOverThreshold = dbfs > _config.ScoreThresholdDbfs;
        if (isOverThreshold)
        {
            _overThresholdFrameCount++;
            if (_segmentOpen)
            {
                _lastOverThresholdAt = timestamp;
            }
            else
            {
                var canMergeToPrevious = _lastOverThresholdAt != default &&
                                         (timestamp - _lastOverThresholdAt).TotalMilliseconds <= _config.SegmentMergeGapMs;
                if (!canMergeToPrevious)
                {
                    _segmentCount++;
                }

                _segmentOpen = true;
                _lastOverThresholdAt = timestamp;
            }
        }
        else if (_segmentOpen && _lastOverThresholdAt != default)
        {
            var silentGapMs = (timestamp - _lastOverThresholdAt).TotalMilliseconds;
            if (silentGapMs > _config.SegmentMergeGapMs)
            {
                _segmentOpen = false;
            }
        }

        var point = new NoiseRealtimePoint(
            timestamp,
            rms,
            dbfs,
            displayDb,
            peak,
            isOverThreshold);
        _slicePoints.Add(point);
        _realtimeBuffer.Enqueue(point);

        while (_realtimeBuffer.Count > _config.RealtimeBufferCapacity)
        {
            _realtimeBuffer.Dequeue();
        }

        var elapsedSeconds = (timestamp - _sliceStartAt).TotalSeconds;
        if (elapsedSeconds + 1e-6 < _config.SliceSec)
        {
            return new NoisePipelineTickResult(point, null);
        }

        var slice = BuildClosedSlice(timestamp);
        ResetSliceState(timestamp);
        return new NoisePipelineTickResult(point, slice);
    }

    private NoiseSliceSummary BuildClosedSlice(DateTimeOffset endAt)
    {
        var sampledDurationMs = _slicePoints.Count * _config.FrameMs;
        if (_slicePoints.Count == 0 || sampledDurationMs <= 0)
        {
            var emptyRaw = new NoiseSliceRawStats(
                AvgDbfs: _config.SilenceFloorDbfs,
                MaxDbfs: _config.SilenceFloorDbfs,
                P50Dbfs: _config.SilenceFloorDbfs,
                P95Dbfs: _config.SilenceFloorDbfs,
                OverRatioDbfs: 0,
                SegmentCount: 0,
                SampledDurationMs: 0,
                GapCount: _gapCount,
                MaxGapMs: _maxGapMs);
            var emptyDisplay = new NoiseSliceDisplayStats(_config.BaselineDb, _config.BaselineDb);
            var emptyScore = ScoreCalculator.Calculate(
                p50Dbfs: emptyRaw.P50Dbfs,
                overRatioDbfs: emptyRaw.OverRatioDbfs,
                segmentCount: emptyRaw.SegmentCount,
                sampledDurationMs: 0,
                scoreThresholdDbfs: _config.ScoreThresholdDbfs,
                maxSegmentsPerMin: _config.MaxSegmentsPerMin);
            return new NoiseSliceSummary(
                _sliceStartAt,
                endAt,
                0,
                emptyRaw,
                emptyDisplay,
                emptyScore.Score,
                emptyScore);
        }

        var dbfsList = _slicePoints.Select(p => p.Dbfs).OrderBy(v => v).ToArray();
        var displayList = _slicePoints.Select(p => p.DisplayDb).OrderBy(v => v).ToArray();

        var avgDbfs = ScoreCalculator.ComputeAverageDbfs(dbfsList);
        var maxDbfs = dbfsList[^1];
        var p50Dbfs = Percentile(sortedValues: dbfsList, percentile: 0.50);
        var p95Dbfs = Percentile(sortedValues: dbfsList, percentile: 0.95);
        var overRatio = _overThresholdFrameCount / (double)_slicePoints.Count;

        var raw = new NoiseSliceRawStats(
            AvgDbfs: avgDbfs,
            MaxDbfs: maxDbfs,
            P50Dbfs: p50Dbfs,
            P95Dbfs: p95Dbfs,
            OverRatioDbfs: Math.Clamp(overRatio, 0, 1),
            SegmentCount: _segmentCount,
            SampledDurationMs: sampledDurationMs,
            GapCount: _gapCount,
            MaxGapMs: _maxGapMs);

        var display = new NoiseSliceDisplayStats(
            AvgDb: Math.Round(displayList.Average(), 2),
            P95Db: Math.Round(Percentile(displayList, 0.95), 2));

        var score = ScoreCalculator.Calculate(
            p50Dbfs: raw.P50Dbfs,
            overRatioDbfs: raw.OverRatioDbfs,
            segmentCount: raw.SegmentCount,
            sampledDurationMs: raw.SampledDurationMs,
            scoreThresholdDbfs: _config.ScoreThresholdDbfs,
            maxSegmentsPerMin: _config.MaxSegmentsPerMin);

        return new NoiseSliceSummary(
            _sliceStartAt,
            endAt,
            _slicePoints.Count,
            raw,
            display,
            score.Score,
            score);
    }

    private void ResetSliceState(DateTimeOffset nextSliceStartAt)
    {
        _slicePoints.Clear();
        _sliceStartAt = nextSliceStartAt;
        _lastOverThresholdAt = default;
        _overThresholdFrameCount = 0;
        _segmentCount = 0;
        _segmentOpen = false;
        _gapCount = 0;
        _maxGapMs = 0;
    }

    private static double Percentile(double[] sortedValues, double percentile)
    {
        if (sortedValues.Length == 0)
        {
            return 0;
        }

        if (sortedValues.Length == 1)
        {
            return sortedValues[0];
        }

        var clamped = Math.Clamp(percentile, 0, 1);
        var position = (sortedValues.Length - 1) * clamped;
        var lower = (int)Math.Floor(position);
        var upper = (int)Math.Ceiling(position);
        if (lower == upper)
        {
            return sortedValues[lower];
        }

        var factor = position - lower;
        return sortedValues[lower] + ((sortedValues[upper] - sortedValues[lower]) * factor);
    }

    private static StudyAnalyticsConfig NormalizeConfig(StudyAnalyticsConfig config)
    {
        var frameMs = Math.Clamp(config.FrameMs, 20, 250);
        var sliceSec = Math.Clamp(config.SliceSec, 5, 600);
        var threshold = Math.Clamp(config.ScoreThresholdDbfs, -100, -5);
        var mergeGapMs = Math.Clamp(config.SegmentMergeGapMs, 100, 4000);
        var maxSegments = Math.Clamp(config.MaxSegmentsPerMin, 1, 40);
        var silenceFloor = Math.Clamp(config.SilenceFloorDbfs, -100, -20);
        var baselineDb = Math.Clamp(config.BaselineDb, 20, 90);
        var avgWindowSec = Math.Clamp(config.AvgWindowSec, 1, 8);
        var ringCapacity = Math.Clamp(config.RealtimeBufferCapacity, 60, 1200);

        return config with
        {
            FrameMs = frameMs,
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

internal static class ScoreCalculator
{
    public static NoiseScoreBreakdown Calculate(
        double p50Dbfs,
        double overRatioDbfs,
        int segmentCount,
        double sampledDurationMs,
        double scoreThresholdDbfs,
        int maxSegmentsPerMin)
    {
        var minutes = Math.Max(1d / 60d, sampledDurationMs / 60000d);
        var sustainedPenalty = Clamp01((p50Dbfs - scoreThresholdDbfs) / 6d);
        var timePenalty = Clamp01(overRatioDbfs / 0.30d);
        var segmentRatePerMin = segmentCount / minutes;
        var segmentPenalty = Clamp01(segmentRatePerMin / Math.Max(1, maxSegmentsPerMin));
        var totalPenalty = (0.40d * sustainedPenalty) + (0.30d * timePenalty) + (0.30d * segmentPenalty);
        var score = Math.Clamp(100d * (1d - totalPenalty), 0, 100);

        return new NoiseScoreBreakdown(
            SustainedPenalty: Math.Round(sustainedPenalty, 4),
            TimePenalty: Math.Round(timePenalty, 4),
            SegmentPenalty: Math.Round(segmentPenalty, 4),
            TotalPenalty: Math.Round(totalPenalty, 4),
            Score: Math.Round(score, 2),
            SustainedLevelDbfs: Math.Round(p50Dbfs, 3),
            OverRatioDbfs: Math.Round(Math.Clamp(overRatioDbfs, 0, 1), 4),
            SegmentCount: Math.Max(0, segmentCount),
            Minutes: Math.Round(minutes, 4),
            DurationMs: Math.Max(0, sampledDurationMs));
    }

    public static double ComputeAverageDbfs(double[] dbfsValues)
    {
        if (dbfsValues.Length == 0)
        {
            return -100;
        }

        // Average in energy domain then convert back to dBFS.
        var avgPower = dbfsValues
            .Select(DbfsToPower)
            .Average();
        return Math.Round(PowerToDbfs(avgPower), 3);
    }

    public static double DbfsToPower(double dbfs)
    {
        return Math.Pow(10d, dbfs / 10d);
    }

    public static double PowerToDbfs(double power)
    {
        if (power <= 1e-12)
        {
            return -100;
        }

        return Math.Clamp(10d * Math.Log10(power), -100, 0);
    }

    private static double Clamp01(double value)
    {
        return Math.Clamp(value, 0, 1);
    }
}

internal sealed class SessionAccumulator
{
    private readonly List<NoiseSliceSummary> _slices = [];

    private StudySessionRuntimeState _state = StudySessionRuntimeState.Idle;
    private string? _sessionId;
    private string _label = string.Empty;
    private DateTimeOffset? _startedAt;
    private DateTimeOffset? _endedAt;
    private string _lastError = string.Empty;

    private double _sumEffectiveMs;
    private double _sumWeightedScore;
    private double _sumWeightedOverRatio;
    private int _totalSegments;
    private double _minScore = 100;
    private double _maxScore;
    private double _currentScore;

    public bool IsRunning => _state == StudySessionRuntimeState.Running;

    public string? CurrentSessionId => _sessionId;

    public bool Start(DateTimeOffset now, StudySessionOptions options)
    {
        if (IsRunning)
        {
            return false;
        }

        _state = StudySessionRuntimeState.Running;
        _sessionId = Guid.NewGuid().ToString("N");
        _label = string.IsNullOrWhiteSpace(options.Label) ? "Study Session" : options.Label.Trim();
        _startedAt = now;
        _endedAt = null;
        _lastError = string.Empty;
        _slices.Clear();
        _sumEffectiveMs = 0;
        _sumWeightedScore = 0;
        _sumWeightedOverRatio = 0;
        _totalSegments = 0;
        _minScore = 100;
        _maxScore = 0;
        _currentScore = 0;
        return true;
    }

    public void AddSlice(NoiseSliceSummary slice)
    {
        if (!IsRunning)
        {
            return;
        }

        _slices.Add(slice);
        var effectiveMs = Math.Max(0, slice.Raw.SampledDurationMs);
        _sumEffectiveMs += effectiveMs;
        _sumWeightedScore += slice.Score * effectiveMs;
        _sumWeightedOverRatio += slice.Raw.OverRatioDbfs * effectiveMs;
        _totalSegments += Math.Max(0, slice.Raw.SegmentCount);
        _currentScore = slice.Score;
        _minScore = Math.Min(_minScore, slice.Score);
        _maxScore = Math.Max(_maxScore, slice.Score);
    }

    public StudySessionReport? Stop(DateTimeOffset now)
    {
        if (!IsRunning || _startedAt is null || string.IsNullOrWhiteSpace(_sessionId))
        {
            return null;
        }

        _state = StudySessionRuntimeState.Completed;
        _endedAt = now;

        var metrics = BuildMetrics();
        return new StudySessionReport(
            SessionId: _sessionId,
            Label: _label,
            StartedAt: _startedAt.Value,
            EndedAt: _endedAt.Value,
            Duration: _endedAt.Value - _startedAt.Value,
            Metrics: metrics,
            Slices: _slices.ToArray());
    }

    public bool Cancel()
    {
        if (!IsRunning)
        {
            return false;
        }

        ResetToIdle();
        return true;
    }

    public StudySessionSnapshot GetSnapshot(DateTimeOffset now)
    {
        var startedAt = _startedAt;
        var endedAt = _endedAt;
        var elapsed = startedAt is null
            ? TimeSpan.Zero
            : (endedAt ?? now) - startedAt.Value;
        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }

        return new StudySessionSnapshot(
            State: _state,
            SessionId: _sessionId,
            Label: _label,
            StartedAt: startedAt,
            EndedAt: endedAt,
            Elapsed: elapsed,
            Metrics: BuildMetrics(),
            LastError: _lastError);
    }

    public void ResetToIdle()
    {
        _state = StudySessionRuntimeState.Idle;
        _sessionId = null;
        _label = string.Empty;
        _startedAt = null;
        _endedAt = null;
        _lastError = string.Empty;
        _slices.Clear();
        _sumEffectiveMs = 0;
        _sumWeightedScore = 0;
        _sumWeightedOverRatio = 0;
        _totalSegments = 0;
        _minScore = 100;
        _maxScore = 0;
        _currentScore = 0;
    }

    private StudySessionMetrics BuildMetrics()
    {
        var avgScore = _sumEffectiveMs <= 0 ? 0 : _sumWeightedScore / _sumEffectiveMs;
        var avgOverRatio = _sumEffectiveMs <= 0 ? 0 : _sumWeightedOverRatio / _sumEffectiveMs;
        var minScore = _slices.Count == 0 ? 0 : _minScore;
        var maxScore = _slices.Count == 0 ? 0 : _maxScore;
        return new StudySessionMetrics(
            CurrentScore: Math.Round(_currentScore, 2),
            AvgScore: Math.Round(avgScore, 2),
            MinScore: Math.Round(minScore, 2),
            MaxScore: Math.Round(maxScore, 2),
            WeightedOverRatioDbfs: Math.Round(avgOverRatio, 4),
            TotalSegmentCount: _totalSegments,
            EffectiveDuration: TimeSpan.FromMilliseconds(_sumEffectiveMs),
            SliceCount: _slices.Count);
    }
}
