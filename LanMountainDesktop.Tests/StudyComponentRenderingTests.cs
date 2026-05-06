using System;
using System.Collections.Generic;
using Avalonia;
using LanMountainDesktop.Models;
using LanMountainDesktop.Views.Components;
using Xunit;

namespace LanMountainDesktop.Tests;

public sealed class StudyComponentRenderingTests
{
    [Fact]
    public void RenderGate_ProcessesOnlyLatestSnapshot()
    {
        var rendered = new List<string>();
        using var gate = new StudySnapshotRenderGate(
            canRender: () => true,
            renderSnapshot: snapshot => rendered.Add(snapshot.LastError));

        gate.Queue(CreateSnapshot("first"));
        gate.Queue(CreateSnapshot("second"));

        Assert.True(gate.ProcessPending());
        Assert.Equal(["second"], rendered);
        Assert.False(gate.HasPendingSnapshot);
    }

    [Fact]
    public void RenderGate_DropsPendingSnapshot_WhenRenderIsBlocked()
    {
        var renderCount = 0;
        using var gate = new StudySnapshotRenderGate(
            canRender: () => false,
            renderSnapshot: _ => renderCount++);

        gate.Queue(CreateSnapshot("blocked"));

        Assert.False(gate.ProcessPending());
        Assert.Equal(0, renderCount);
        Assert.False(gate.HasPendingSnapshot);
    }

    [Fact]
    public void CurveChart_SplitsStableHistoryFromDynamicTail()
    {
        var points = CreateRealtimePoints(count: 10, step: TimeSpan.FromSeconds(1));
        var counts = StudyNoiseCurveChartControl.ResolveLayerSourceCounts(points, TimeSpan.FromSeconds(4));

        Assert.Equal(5, StudyNoiseCurveChartControl.ResolveFirstTailIndex(points, TimeSpan.FromSeconds(4)));
        Assert.Equal(5, counts.StaticSourceCount);
        Assert.Equal(6, counts.DynamicSourceCount);
    }

    [Fact]
    public void CurveChart_UsesStableLogicalTimeCoordinates()
    {
        var origin = new DateTimeOffset(2026, 5, 6, 12, 0, 0, TimeSpan.Zero);

        var x = StudyNoiseCurveChartControl.MapTimestampToLogicalX(
            origin.AddSeconds(3),
            origin,
            pixelsPerSecond: 12);

        Assert.Equal(36, x);
    }

    [Fact]
    public void DistributionAreaChart_BuildsAreaPathCache()
    {
        var points = CreateRealtimePoints(count: 24, step: TimeSpan.FromMilliseconds(500));
        var control = new StudyNoiseDistributionAreaChartControl();

        control.UpdateSeries(points, baselineDb: 45);
        control.RebuildCacheForTesting(new Rect(1, 1, 320, 160));

        Assert.True(control.CachedPathCount > 0);
        Assert.True(control.CachedPathCount <= 4);
        Assert.True(control.StaticSourceCount > 0);
        Assert.True(control.DynamicSourceCount > 0);
    }

    [Fact]
    public void DistributionAreaChart_UsesStableLogicalTimeCoordinates_WhenNewPointArrives()
    {
        var origin = new DateTimeOffset(2026, 5, 6, 12, 0, 0, TimeSpan.Zero);
        var oldPointTimestamp = origin.AddSeconds(3);

        var before = StudyNoiseDistributionAreaChartControl.MapTimestampToLogicalX(
            oldPointTimestamp,
            origin,
            pixelsPerSecond: 20);
        var after = StudyNoiseDistributionAreaChartControl.MapTimestampToLogicalX(
            oldPointTimestamp,
            origin,
            pixelsPerSecond: 20);

        Assert.Equal(before, after);
        Assert.Equal(60, after);
    }

    [Fact]
    public void DistributionAreaChart_ReusesStaticAreaPath_WhenOnlyDynamicTailChanges()
    {
        var firstSeries = CreateRealtimePoints(
            new[]
            {
                (0d, 40d),
                (1d, 43d),
                (2d, 45d),
                (3d, 47d),
                (8d, 52d)
            });
        var secondSeries = CreateRealtimePoints(
            new[]
            {
                (0d, 40d),
                (1d, 43d),
                (2d, 45d),
                (3d, 47d),
                (8d, 52d),
                (8.05d, 54d)
            });
        var control = new StudyNoiseDistributionAreaChartControl();
        var plot = new Rect(1, 1, 320, 160);

        control.UpdateSeries(firstSeries, baselineDb: 45);
        control.RebuildCacheForTesting(plot);
        var staticBuildVersion = control.StaticPathBuildVersion;

        control.UpdateSeries(secondSeries, baselineDb: 45);
        control.RebuildCacheForTesting(plot);

        Assert.Equal(staticBuildVersion, control.StaticPathBuildVersion);
        Assert.True(control.DynamicPathBuildVersion > 1);
    }

    [Fact]
    public void DistributionAreaChart_SplitsStaticHistoryFromDynamicTail()
    {
        var points = CreateRealtimePoints(count: 10, step: TimeSpan.FromSeconds(1));
        var counts = StudyNoiseDistributionAreaChartControl.ResolveLayerSourceCounts(
            points,
            TimeSpan.FromSeconds(4));

        Assert.Equal(5, counts.StaticSourceCount);
        Assert.Equal(6, counts.DynamicSourceCount);
    }

    [Fact]
    public void DistributionAreaChart_StaticReportKeepsWholeSeriesStatic()
    {
        var points = CreateRealtimePoints(count: 10, step: TimeSpan.FromSeconds(1));
        var counts = StudyNoiseDistributionAreaChartControl.ResolveLayerSourceCounts(
            points,
            TimeSpan.FromSeconds(4),
            isStaticSeries: true);

        Assert.Equal(10, counts.StaticSourceCount);
        Assert.Equal(0, counts.DynamicSourceCount);
    }

    [Fact]
    public void DistributionAreaChart_ResolvesLevelsFromBaseline()
    {
        Assert.Equal(NoiseDistributionLevel.Quiet, StudyNoiseDistributionAreaChartControl.ResolveLevel(44.9, 45));
        Assert.Equal(NoiseDistributionLevel.Normal, StudyNoiseDistributionAreaChartControl.ResolveLevel(45, 45));
        Assert.Equal(NoiseDistributionLevel.Noisy, StudyNoiseDistributionAreaChartControl.ResolveLevel(55, 45));
        Assert.Equal(NoiseDistributionLevel.Extreme, StudyNoiseDistributionAreaChartControl.ResolveLevel(65, 45));
    }

    private static IReadOnlyList<NoiseRealtimePoint> CreateRealtimePoints(int count, TimeSpan step)
    {
        var start = new DateTimeOffset(2026, 5, 6, 12, 0, 0, TimeSpan.Zero);
        var points = new List<NoiseRealtimePoint>(count);
        for (var i = 0; i < count; i++)
        {
            var displayDb = 38 + i;
            points.Add(new NoiseRealtimePoint(
                Timestamp: start + TimeSpan.FromTicks(step.Ticks * i),
                Rms: 0.2,
                Dbfs: -60 + i,
                DisplayDb: displayDb,
                Peak: 0.3,
                IsOverThreshold: displayDb > 50));
        }

        return points;
    }

    private static IReadOnlyList<NoiseRealtimePoint> CreateRealtimePoints(IReadOnlyList<(double OffsetSeconds, double DisplayDb)> samples)
    {
        var start = new DateTimeOffset(2026, 5, 6, 12, 0, 0, TimeSpan.Zero);
        var points = new List<NoiseRealtimePoint>(samples.Count);
        for (var i = 0; i < samples.Count; i++)
        {
            var sample = samples[i];
            points.Add(new NoiseRealtimePoint(
                Timestamp: start + TimeSpan.FromSeconds(sample.OffsetSeconds),
                Rms: 0.2,
                Dbfs: -60 + i,
                DisplayDb: sample.DisplayDb,
                Peak: 0.3,
                IsOverThreshold: sample.DisplayDb > 50));
        }

        return points;
    }

    private static StudyAnalyticsSnapshot CreateSnapshot(string marker)
    {
        var config = new StudyAnalyticsConfig();
        var session = new StudySessionSnapshot(
            State: StudySessionRuntimeState.Idle,
            SessionId: null,
            Label: string.Empty,
            StartedAt: null,
            EndedAt: null,
            Elapsed: TimeSpan.Zero,
            Metrics: new StudySessionMetrics(
                CurrentScore: 0,
                AvgScore: 0,
                MinScore: 0,
                MaxScore: 0,
                WeightedOverRatioDbfs: 0,
                TotalSegmentCount: 0,
                EffectiveDuration: TimeSpan.Zero,
                SliceCount: 0),
            LastError: string.Empty);

        return new StudyAnalyticsSnapshot(
            State: StudyAnalyticsRuntimeState.Ready,
            StreamStatus: NoiseStreamStatus.Initializing,
            DataMode: StudyDataMode.Realtime,
            Config: config,
            LatestRealtimePoint: null,
            LatestSlice: null,
            RealtimeBuffer: Array.Empty<NoiseRealtimePoint>(),
            Session: session,
            LastSessionReport: null,
            SelectedSessionReportId: null,
            SessionHistory: Array.Empty<StudySessionHistoryEntry>(),
            LastError: marker);
    }
}
