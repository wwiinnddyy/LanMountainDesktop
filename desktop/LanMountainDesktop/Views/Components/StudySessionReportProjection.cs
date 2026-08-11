using System;
using System.Collections.Generic;
using System.Linq;
using LanMountainDesktop.Models;

namespace LanMountainDesktop.Views.Components;

internal readonly record struct StudySessionReportAggregate(
    TimeSpan Duration,
    double AverageDisplayDb,
    double AverageDbfs,
    double P95DisplayDb,
    double P50Dbfs,
    double OverRatio,
    int SegmentCount,
    double SegmentsPerMin,
    double SustainedPenalty,
    double TimePenalty,
    double SegmentPenalty,
    double TotalPenalty,
    double Score);

internal static class StudySessionReportProjection
{
    public static IReadOnlyList<NoiseRealtimePoint> BuildSyntheticRealtimePoints(
        StudySessionReport report,
        StudyAnalyticsConfig config,
        int maxPoints = 360)
    {
        if (report.Slices.Count == 0)
        {
            return Array.Empty<NoiseRealtimePoint>();
        }

        var ordered = report.Slices
            .OrderBy(slice => slice.StartAt)
            .ToList();

        var synthetic = new List<NoiseRealtimePoint>(ordered.Count * 3);
        var previousTimestamp = ordered[0].StartAt;

        for (var i = 0; i < ordered.Count; i++)
        {
            var slice = ordered[i];
            var start = slice.StartAt;
            var end = slice.EndAt > start
                ? slice.EndAt
                : start.AddMilliseconds(Math.Max(1, ResolveSliceDurationMs(slice)));

            if (start <= previousTimestamp)
            {
                start = previousTimestamp.AddMilliseconds(1);
            }

            if (end <= start)
            {
                end = start.AddMilliseconds(1);
            }

            var middle = start + TimeSpan.FromTicks(Math.Max(1, (end - start).Ticks / 2));
            var avgDisplay = Math.Clamp(slice.Display.AvgDb, 20, 100);
            var p95Display = Math.Clamp(slice.Display.P95Db, 20, 100);
            var avgDbfs = Math.Clamp(slice.Raw.AvgDbfs, -100, 0);
            var p95Dbfs = Math.Clamp(slice.Raw.P95Dbfs, -100, 0);

            synthetic.Add(CreatePoint(start, avgDisplay, avgDbfs, config.ScoreThresholdDbfs));
            synthetic.Add(CreatePoint(middle, p95Display, p95Dbfs, config.ScoreThresholdDbfs));
            synthetic.Add(CreatePoint(end, avgDisplay, avgDbfs, config.ScoreThresholdDbfs));

            previousTimestamp = end;
        }

        return DownsampleIfNeeded(synthetic, Math.Max(60, maxPoints));
    }

    public static bool TryAggregate(
        StudySessionReport report,
        StudyAnalyticsConfig config,
        out StudySessionReportAggregate aggregate)
    {
        aggregate = default;
        if (report.Slices.Count == 0)
        {
            return false;
        }

        var totalDurationMs = 0d;
        var weightedDisplay = 0d;
        var weightedDisplayP95 = 0d;
        var weightedDbfs = 0d;
        var weightedP50Dbfs = 0d;
        var weightedOverRatio = 0d;
        var totalSegments = 0;

        for (var i = 0; i < report.Slices.Count; i++)
        {
            var slice = report.Slices[i];
            var durationMs = ResolveSliceDurationMs(slice);
            if (durationMs <= 0)
            {
                continue;
            }

            totalDurationMs += durationMs;
            weightedDisplay += slice.Display.AvgDb * durationMs;
            weightedDisplayP95 += slice.Display.P95Db * durationMs;
            weightedDbfs += slice.Raw.AvgDbfs * durationMs;
            weightedP50Dbfs += slice.Raw.P50Dbfs * durationMs;
            weightedOverRatio += slice.Raw.OverRatioDbfs * durationMs;
            totalSegments += Math.Max(0, slice.Raw.SegmentCount);
        }

        if (totalDurationMs <= 0)
        {
            return false;
        }

        var averageDisplay = weightedDisplay / totalDurationMs;
        var p95Display = weightedDisplayP95 / totalDurationMs;
        var averageDbfs = weightedDbfs / totalDurationMs;
        var p50Dbfs = weightedP50Dbfs / totalDurationMs;
        var overRatio = Math.Clamp(weightedOverRatio / totalDurationMs, 0, 1);
        var minutes = Math.Max(1d / 60d, totalDurationMs / 60000d);
        var segmentsPerMin = totalSegments / minutes;

        var sustainedPenalty = Clamp01((p50Dbfs - config.ScoreThresholdDbfs) / 6d);
        var timePenalty = Clamp01(overRatio / 0.30d);
        var segmentPenalty = Clamp01(segmentsPerMin / Math.Max(1, config.MaxSegmentsPerMin));
        var totalPenalty = (0.40d * sustainedPenalty) + (0.30d * timePenalty) + (0.30d * segmentPenalty);
        var score = Math.Clamp(100d * (1d - totalPenalty), 0, 100);

        aggregate = new StudySessionReportAggregate(
            Duration: TimeSpan.FromMilliseconds(totalDurationMs),
            AverageDisplayDb: Math.Round(averageDisplay, 2),
            AverageDbfs: Math.Round(averageDbfs, 2),
            P95DisplayDb: Math.Round(p95Display, 2),
            P50Dbfs: Math.Round(p50Dbfs, 2),
            OverRatio: Math.Round(overRatio, 4),
            SegmentCount: Math.Max(0, totalSegments),
            SegmentsPerMin: Math.Round(segmentsPerMin, 3),
            SustainedPenalty: Math.Round(sustainedPenalty, 4),
            TimePenalty: Math.Round(timePenalty, 4),
            SegmentPenalty: Math.Round(segmentPenalty, 4),
            TotalPenalty: Math.Round(totalPenalty, 4),
            Score: Math.Round(score, 1));
        return true;
    }

    public static double ResolveSliceDurationMs(NoiseSliceSummary slice)
    {
        if (slice.ScoreDetail.DurationMs > 0)
        {
            return slice.ScoreDetail.DurationMs;
        }

        if (slice.Raw.SampledDurationMs > 0)
        {
            return slice.Raw.SampledDurationMs;
        }

        return Math.Max(1, (slice.EndAt - slice.StartAt).TotalMilliseconds);
    }

    private static NoiseRealtimePoint CreatePoint(
        DateTimeOffset timestamp,
        double displayDb,
        double dbfs,
        double scoreThresholdDbfs)
    {
        var clampedDbfs = Math.Clamp(dbfs, -100, 0);
        var rms = Math.Pow(10d, clampedDbfs / 20d);
        return new NoiseRealtimePoint(
            Timestamp: timestamp,
            Rms: rms,
            Dbfs: clampedDbfs,
            DisplayDb: Math.Clamp(displayDb, 20, 100),
            Peak: rms,
            IsOverThreshold: clampedDbfs >= scoreThresholdDbfs);
    }

    private static IReadOnlyList<NoiseRealtimePoint> DownsampleIfNeeded(List<NoiseRealtimePoint> points, int maxPoints)
    {
        if (points.Count <= maxPoints)
        {
            return points;
        }

        var result = new List<NoiseRealtimePoint>(maxPoints);
        result.Add(points[0]);

        var middleCount = maxPoints - 2;
        var sourceMiddleCount = points.Count - 2;
        for (var i = 0; i < middleCount; i++)
        {
            var sourceIndex = 1 + (int)Math.Round(i * (sourceMiddleCount - 1) / (double)Math.Max(1, middleCount - 1));
            sourceIndex = Math.Clamp(sourceIndex, 1, points.Count - 2);
            result.Add(points[sourceIndex]);
        }

        result.Add(points[^1]);
        return result;
    }

    private static double Clamp01(double value)
    {
        return Math.Clamp(value, 0, 1);
    }
}
