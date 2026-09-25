using System;
using System.Buffers;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using LanMountainDesktop.Models;

namespace LanMountainDesktop.Views.Components;

public sealed class StudyNoiseCurveChartControl : Control
{
    private const double MinDisplayDb = 20;
    private const double MaxDisplayDb = 100;
    private const double DynamicTailSeconds = 4;

    private static readonly IBrush GridBrush = new SolidColorBrush(Color.Parse("#324E6780"));
    private static readonly IBrush AxisBrush = new SolidColorBrush(Color.Parse("#5C6D86A1"));
    private static readonly IBrush LineBrush = new SolidColorBrush(Color.Parse("#FF52AEEA"));
    private static readonly IBrush FillBrush = new SolidColorBrush(Color.Parse("#3552AEEA"));
    private static readonly Pen GridPen = new(GridBrush, 1);
    private static readonly Pen AxisPen = new(AxisBrush, 1.1);
    private static readonly Pen LinePen = new(LineBrush, 1.8);

    private IReadOnlyList<NoiseRealtimePoint> _points = Array.Empty<NoiseRealtimePoint>();
    private Point[]? _pointBuffer;
    private readonly StudyChartGridLayer _gridLayer = new();
    private StreamGeometry? _staticLineGeometry;
    private StreamGeometry? _staticFillGeometry;
    private StreamGeometry? _dynamicLineGeometry;
    private StreamGeometry? _dynamicFillGeometry;
    private Rect _cachedPlot;
    private DateTimeOffset _logicalOrigin;
    private DateTimeOffset _lastSeriesStart;
    private DateTimeOffset _lastSeriesEnd;
    private double _cachedPixelsPerSecond;
    private double _viewportTranslateX;
    private bool _hasLogicalOrigin;
    private bool _geometryDirty = true;
    private int _lastSeriesSignature;
    private int _staticSourceCount;
    private int _dynamicSourceCount;

    public void UpdateSeries(IReadOnlyList<NoiseRealtimePoint>? points)
    {
        var nextPoints = points ?? Array.Empty<NoiseRealtimePoint>();
        var nextSignature = ComputeSeriesSignature(nextPoints);
        if (ReferenceEquals(_points, nextPoints) && _lastSeriesSignature == nextSignature)
        {
            return;
        }

        UpdateLogicalOrigin(nextPoints);
        _points = nextPoints;
        _lastSeriesSignature = nextSignature;
        _geometryDirty = true;
        InvalidateVisual();
    }

    public void CompactCaches()
    {
        if (_pointBuffer is not null && _pointBuffer.Length > 2048)
        {
            PointBufferPool.ReturnPoints(ref _pointBuffer);
        }

        _staticLineGeometry = null;
        _staticFillGeometry = null;
        _dynamicLineGeometry = null;
        _dynamicFillGeometry = null;
        _geometryDirty = true;
    }

    internal int StaticSourceCount => _staticSourceCount;

    internal int DynamicSourceCount => _dynamicSourceCount;

    internal void RebuildCacheForTesting(Rect plot)
    {
        if (_points.Count >= 2)
        {
            EnsureGeometry(plot);
        }
    }

    internal static double ResolveVisibleDurationSeconds(IReadOnlyList<NoiseRealtimePoint> points)
    {
        if (points.Count < 2)
        {
            return 12;
        }

        var duration = (points[^1].Timestamp - points[0].Timestamp).TotalSeconds;
        if (double.IsNaN(duration) || double.IsInfinity(duration) || duration <= 1)
        {
            duration = 12;
        }

        return Math.Clamp(duration, 4, 60);
    }

    internal static (int StaticSourceCount, int DynamicSourceCount) ResolveLayerSourceCounts(
        IReadOnlyList<NoiseRealtimePoint> points,
        TimeSpan tailDuration)
    {
        if (points.Count < 2)
        {
            return (0, 0);
        }

        var firstTailIndex = StudyNoiseSeriesRules.FirstTailIndex(points, tailDuration);
        var dynamicStartIndex = Math.Max(0, firstTailIndex - 1);
        var staticCount = firstTailIndex >= 2 ? firstTailIndex : 0;
        var dynamicCount = points.Count - dynamicStartIndex >= 2 ? points.Count - dynamicStartIndex : 0;
        return (staticCount, dynamicCount);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        PointBufferPool.ReturnPoints(ref _pointBuffer);
        _gridLayer.DropGeometry();
        _staticLineGeometry = null;
        _staticFillGeometry = null;
        _dynamicLineGeometry = null;
        _dynamicFillGeometry = null;
        _geometryDirty = true;
        base.OnDetachedFromVisualTree(e);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var bounds = Bounds;
        if (bounds.Width <= 2 || bounds.Height <= 2)
        {
            return;
        }

        var plot = new Rect(
            x: 1,
            y: 1,
            width: Math.Max(1, bounds.Width - 2),
            height: Math.Max(1, bounds.Height - 2));

        _gridLayer.Draw(context, plot, GridPen, AxisPen);

        if (_points.Count < 2)
        {
            return;
        }

        EnsureGeometry(plot);
        if (_staticLineGeometry is null &&
            _staticFillGeometry is null &&
            _dynamicLineGeometry is null &&
            _dynamicFillGeometry is null)
        {
            return;
        }

        using (context.PushClip(plot))
        using (context.PushTransform(Matrix.CreateTranslation(_viewportTranslateX, 0)))
        {
            if (_staticFillGeometry is not null)
            {
                context.DrawGeometry(FillBrush, pen: null, _staticFillGeometry);
            }

            if (_dynamicFillGeometry is not null)
            {
                context.DrawGeometry(FillBrush, pen: null, _dynamicFillGeometry);
            }

            if (_staticLineGeometry is not null)
            {
                context.DrawGeometry(brush: null, pen: LinePen, _staticLineGeometry);
            }

            if (_dynamicLineGeometry is not null)
            {
                context.DrawGeometry(brush: null, pen: LinePen, _dynamicLineGeometry);
            }
        }
    }

    private void UpdateLogicalOrigin(IReadOnlyList<NoiseRealtimePoint> nextPoints)
    {
        if (nextPoints.Count == 0)
        {
            _hasLogicalOrigin = false;
            _lastSeriesStart = default;
            _lastSeriesEnd = default;
            return;
        }

        var nextStart = nextPoints[0].Timestamp;
        var nextEnd = nextPoints[^1].Timestamp;
        if (!_hasLogicalOrigin)
        {
            ResetLogicalOrigin(nextStart);
        }
        else
        {
            var overlapsPreviousSeries = nextStart <= _lastSeriesEnd && nextEnd >= _lastSeriesStart;
            if (!overlapsPreviousSeries || nextStart < _logicalOrigin)
            {
                ResetLogicalOrigin(nextStart);
            }
        }

        _lastSeriesStart = nextStart;
        _lastSeriesEnd = nextEnd;
    }

    private void ResetLogicalOrigin(DateTimeOffset origin)
    {
        _logicalOrigin = origin;
        _hasLogicalOrigin = true;
        _staticLineGeometry = null;
        _staticFillGeometry = null;
        _dynamicLineGeometry = null;
        _dynamicFillGeometry = null;
        _geometryDirty = true;
    }

    private void EnsureGeometry(Rect plot)
    {
        var visibleDurationSeconds = ResolveVisibleDurationSeconds(_points);
        var pixelsPerSecond = plot.Width / visibleDurationSeconds;
        var latestLogicalX = StudyChartGeometry.MapTimestampToLogicalX(_points[^1].Timestamp, _logicalOrigin, pixelsPerSecond);
        _viewportTranslateX = plot.Right - latestLogicalX;

        if (!_geometryDirty &&
            _cachedPlot == plot &&
            Math.Abs(_cachedPixelsPerSecond - pixelsPerSecond) < 0.001)
        {
            return;
        }

        _cachedPlot = plot;
        _cachedPixelsPerSecond = pixelsPerSecond;
        _staticLineGeometry = null;
        _staticFillGeometry = null;
        _dynamicLineGeometry = null;
        _dynamicFillGeometry = null;
        _staticSourceCount = 0;
        _dynamicSourceCount = 0;

        var firstTailIndex = StudyNoiseSeriesRules.FirstTailIndex(_points, TimeSpan.FromSeconds(DynamicTailSeconds));
        var dynamicStartIndex = Math.Max(0, firstTailIndex - 1);
        var staticEndExclusive = firstTailIndex;

        if (staticEndExclusive >= 2)
        {
            (_staticLineGeometry, _staticFillGeometry, _staticSourceCount) = BuildLayerGeometry(
                startIndex: 0,
                endExclusive: staticEndExclusive,
                plot,
                pixelsPerSecond);
        }

        if (_points.Count - dynamicStartIndex >= 2)
        {
            (_dynamicLineGeometry, _dynamicFillGeometry, _dynamicSourceCount) = BuildLayerGeometry(
                dynamicStartIndex,
                _points.Count,
                plot,
                pixelsPerSecond);
        }

        _geometryDirty = false;
    }

    private (StreamGeometry? Line, StreamGeometry? Fill, int SourceCount) BuildLayerGeometry(
        int startIndex,
        int endExclusive,
        Rect plot,
        double pixelsPerSecond)
    {
        var sourceCount = endExclusive - startIndex;
        if (sourceCount < 2)
        {
            return (null, null, sourceCount);
        }

        var maxSamples = Math.Clamp((int)Math.Floor(plot.Width), 56, 360);
        var pointCount = StudyChartGeometry.BuildPlotPoints(
            _points, startIndex, endExclusive, plot, _logicalOrigin, pixelsPerSecond,
            MinDisplayDb, MaxDisplayDb, maxSamples, ref _pointBuffer);
        if (pointCount < 2 || _pointBuffer is null)
        {
            return (null, null, sourceCount);
        }

        var lineGeometry = new StreamGeometry();
        using (var builder = lineGeometry.Open())
        {
            builder.BeginFigure(_pointBuffer[0], false);
            for (var i = 1; i < pointCount; i++)
            {
                builder.LineTo(_pointBuffer[i]);
            }
        }

        var fillGeometry = new StreamGeometry();
        using (var builder = fillGeometry.Open())
        {
            var first = _pointBuffer[0];
            builder.BeginFigure(new Point(first.X, plot.Bottom), true);
            builder.LineTo(first);

            for (var i = 1; i < pointCount; i++)
            {
                builder.LineTo(_pointBuffer[i]);
            }

            var last = _pointBuffer[pointCount - 1];
            builder.LineTo(new Point(last.X, plot.Bottom));
            builder.LineTo(new Point(first.X, plot.Bottom));
            builder.EndFigure(true);
        }

        return (lineGeometry, fillGeometry, sourceCount);
    }

    private static int ComputeSeriesSignature(IReadOnlyList<NoiseRealtimePoint> points)
    {
        if (points.Count == 0)
        {
            return 0;
        }

        var first = points[0];
        var last = points[^1];
        return HashCode.Combine(
            points.Count,
            first.Timestamp.UtcTicks,
            Math.Round(first.DisplayDb, 2),
            last.Timestamp.UtcTicks,
            Math.Round(last.DisplayDb, 2));
    }
}
