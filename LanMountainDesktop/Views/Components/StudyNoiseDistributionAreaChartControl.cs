using System;
using System.Buffers;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using LanMountainDesktop.Models;

namespace LanMountainDesktop.Views.Components;

public sealed class StudyNoiseDistributionAreaChartControl : Control
{
    private const double DynamicTailSeconds = 4;
    private const double RealtimeVisibleSeconds = 12;

    private static readonly IBrush GridBrush = new SolidColorBrush(Color.Parse("#304E6780"));
    private static readonly IBrush AxisBrush = new SolidColorBrush(Color.Parse("#5C6D86A1"));
    private static readonly Pen GridPen = new(GridBrush, 1);
    private static readonly Pen AxisPen = new(AxisBrush, 1.1);

    private static readonly IBrush QuietBandBrush = new SolidColorBrush(Color.Parse("#1834D399"));
    private static readonly IBrush NormalBandBrush = new SolidColorBrush(Color.Parse("#1760A5FA"));
    private static readonly IBrush NoisyBandBrush = new SolidColorBrush(Color.Parse("#16F59E0B"));
    private static readonly IBrush ExtremeBandBrush = new SolidColorBrush(Color.Parse("#18EF4444"));
    private static readonly IBrush AreaFillBrush = CreateAreaGradientBrush(0x56);
    private static readonly IBrush DynamicAreaFillBrush = CreateAreaGradientBrush(0x78);
    private static readonly IBrush LatestGlowBrush = new SolidColorBrush(Color.Parse("#5852D6FF"));
    private static readonly IBrush LatestPointBrush = new SolidColorBrush(Color.Parse("#FFFFFFFF"));
    private static readonly Pen StaticLinePen = new(new SolidColorBrush(Color.Parse("#C452AEEA")), 1.35);
    private static readonly Pen DynamicLinePen = new(new SolidColorBrush(Color.Parse("#FF8BE8FF")), 1.9);
    private static readonly Pen LatestPointPen = new(new SolidColorBrush(Color.Parse("#FF52D6FF")), 1.4);

    private IReadOnlyList<NoiseRealtimePoint> _points = Array.Empty<NoiseRealtimePoint>();
    private Point[]? _pointBuffer;
    private StreamGeometry? _gridGeometry;
    private StreamGeometry? _axisGeometry;
    private StreamGeometry? _staticLineGeometry;
    private StreamGeometry? _staticFillGeometry;
    private StreamGeometry? _dynamicLineGeometry;
    private StreamGeometry? _dynamicFillGeometry;
    private Rect _cachedGridPlot;
    private Rect _cachedPlot;
    private DateTimeOffset _logicalOrigin;
    private DateTimeOffset _lastSeriesStart;
    private DateTimeOffset _lastSeriesEnd;
    private double _baselineDb = 45;
    private double _cachedBaselineDb = 45;
    private double _cachedPixelsPerSecond;
    private double _viewportTranslateX;
    private bool _hasLogicalOrigin;
    private bool _isStaticSeries;
    private bool _staticGeometryDirty = true;
    private bool _dynamicGeometryDirty = true;
    private int _lastSeriesSignature;
    private int _cachedStaticEndExclusive = -1;
    private int _cachedDynamicStartIndex = -1;
    private int _staticSourceCount;
    private int _dynamicSourceCount;
    private int _staticPathBuildVersion;
    private int _dynamicPathBuildVersion;
    private int _cachedPathCountForTesting;

    public void UpdateSeries(IReadOnlyList<NoiseRealtimePoint>? points, double baselineDb)
    {
        UpdateSeries(points, baselineDb, isStaticSeries: false);
    }

    public void UpdateSeries(IReadOnlyList<NoiseRealtimePoint>? points, double baselineDb, bool isStaticSeries)
    {
        var nextPoints = points ?? Array.Empty<NoiseRealtimePoint>();
        var nextBaselineDb = Math.Clamp(baselineDb, 20, 85);
        var nextSignature = ComputeSeriesSignature(nextPoints, nextBaselineDb, isStaticSeries);
        if (ReferenceEquals(_points, nextPoints) &&
            Math.Abs(_baselineDb - nextBaselineDb) < 0.001 &&
            _isStaticSeries == isStaticSeries &&
            _lastSeriesSignature == nextSignature)
        {
            return;
        }

        var baselineChanged = Math.Abs(_baselineDb - nextBaselineDb) >= 0.001;
        var modeChanged = _isStaticSeries != isStaticSeries;
        UpdateLogicalOrigin(nextPoints, baselineChanged || modeChanged);

        _points = nextPoints;
        _baselineDb = nextBaselineDb;
        _isStaticSeries = isStaticSeries;
        _lastSeriesSignature = nextSignature;
        _dynamicGeometryDirty = true;
        if (baselineChanged || modeChanged || nextPoints.Count < 2)
        {
            _staticGeometryDirty = true;
        }

        InvalidateVisual();
    }

    public void CompactCaches()
    {
        ReleasePointBuffer();
        _staticLineGeometry = null;
        _staticFillGeometry = null;
        _dynamicLineGeometry = null;
        _dynamicFillGeometry = null;
        _staticGeometryDirty = true;
        _dynamicGeometryDirty = true;
        _cachedStaticEndExclusive = -1;
        _cachedDynamicStartIndex = -1;
        _cachedPathCountForTesting = 0;
    }

    internal int StaticSourceCount => _staticSourceCount;

    internal int DynamicSourceCount => _dynamicSourceCount;

    internal int CachedPathCount
    {
        get
        {
            var count = 0;
            if (_staticLineGeometry is not null)
            {
                count++;
            }

            if (_staticFillGeometry is not null)
            {
                count++;
            }

            if (_dynamicLineGeometry is not null)
            {
                count++;
            }

            if (_dynamicFillGeometry is not null)
            {
                count++;
            }

            return count > 0 ? count : _cachedPathCountForTesting;
        }
    }

    internal int StaticPathBuildVersion => _staticPathBuildVersion;

    internal int DynamicPathBuildVersion => _dynamicPathBuildVersion;

    internal void RebuildCacheForTesting(Rect plot)
    {
        if (_points.Count >= 2)
        {
            EnsureGeometryPlanForTesting(plot);
        }
    }

    internal static double ResolveVisibleDurationSeconds(IReadOnlyList<NoiseRealtimePoint> points)
    {
        if (points.Count < 2)
        {
            return RealtimeVisibleSeconds;
        }

        var duration = (points[^1].Timestamp - points[0].Timestamp).TotalSeconds;
        if (double.IsNaN(duration) || double.IsInfinity(duration) || duration <= 1)
        {
            duration = RealtimeVisibleSeconds;
        }

        return Math.Clamp(duration, 4, 60);
    }

    internal static int ResolveFirstTailIndex(IReadOnlyList<NoiseRealtimePoint> points, TimeSpan tailDuration)
    {
        if (points.Count <= 1)
        {
            return 0;
        }

        var cutoff = points[^1].Timestamp - tailDuration;
        for (var i = 0; i < points.Count; i++)
        {
            if (points[i].Timestamp >= cutoff)
            {
                return i;
            }
        }

        return points.Count - 1;
    }

    internal static (int StaticSourceCount, int DynamicSourceCount) ResolveLayerSourceCounts(
        IReadOnlyList<NoiseRealtimePoint> points,
        TimeSpan tailDuration,
        bool isStaticSeries = false)
    {
        if (points.Count < 2)
        {
            return (0, 0);
        }

        if (isStaticSeries)
        {
            return (points.Count, 0);
        }

        var firstTailIndex = ResolveFirstTailIndex(points, tailDuration);
        var dynamicStartIndex = Math.Max(0, firstTailIndex - 1);
        var staticCount = firstTailIndex >= 2 ? firstTailIndex : 0;
        var dynamicCount = points.Count - dynamicStartIndex >= 2 ? points.Count - dynamicStartIndex : 0;
        return (staticCount, dynamicCount);
    }

    internal static double MapTimestampToLogicalX(DateTimeOffset timestamp, DateTimeOffset origin, double pixelsPerSecond)
    {
        return Math.Max(0, (timestamp - origin).TotalSeconds * pixelsPerSecond);
    }

    internal static NoiseDistributionLevel ResolveLevel(double displayDb, double baselineDb)
    {
        var quietUpper = baselineDb;
        var normalUpper = baselineDb + 10d;
        var noisyUpper = baselineDb + 20d;

        if (displayDb < quietUpper)
        {
            return NoiseDistributionLevel.Quiet;
        }

        if (displayDb < normalUpper)
        {
            return NoiseDistributionLevel.Normal;
        }

        if (displayDb < noisyUpper)
        {
            return NoiseDistributionLevel.Noisy;
        }

        return NoiseDistributionLevel.Extreme;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        ReleasePointBuffer();
        _gridGeometry = null;
        _axisGeometry = null;
        _staticLineGeometry = null;
        _staticFillGeometry = null;
        _dynamicLineGeometry = null;
        _dynamicFillGeometry = null;
        _staticGeometryDirty = true;
        _dynamicGeometryDirty = true;
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

        DrawLevelBands(context, plot);
        DrawGrid(context, plot);

        if (_points.Count < 2)
        {
            return;
        }

        EnsureGeometry(plot);

        using (context.PushClip(plot))
        using (context.PushTransform(Matrix.CreateTranslation(_viewportTranslateX, 0)))
        {
            if (_staticFillGeometry is not null)
            {
                context.DrawGeometry(AreaFillBrush, pen: null, _staticFillGeometry);
            }

            if (_dynamicFillGeometry is not null)
            {
                context.DrawGeometry(DynamicAreaFillBrush, pen: null, _dynamicFillGeometry);
            }

            if (_staticLineGeometry is not null)
            {
                context.DrawGeometry(brush: null, pen: StaticLinePen, _staticLineGeometry);
            }

            if (_dynamicLineGeometry is not null)
            {
                context.DrawGeometry(brush: null, pen: DynamicLinePen, _dynamicLineGeometry);
            }

            DrawLatestPoint(context, plot);
        }
    }

    private void UpdateLogicalOrigin(IReadOnlyList<NoiseRealtimePoint> nextPoints, bool forceReset)
    {
        if (nextPoints.Count == 0)
        {
            ResetSeriesState();
            return;
        }

        var nextStart = nextPoints[0].Timestamp;
        var nextEnd = nextPoints[^1].Timestamp;
        if (!_hasLogicalOrigin || forceReset)
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
        _staticSourceCount = 0;
        _dynamicSourceCount = 0;
        _cachedStaticEndExclusive = -1;
        _cachedDynamicStartIndex = -1;
        _cachedPathCountForTesting = 0;
        _staticGeometryDirty = true;
        _dynamicGeometryDirty = true;
    }

    private void ResetSeriesState()
    {
        _hasLogicalOrigin = false;
        _lastSeriesStart = default;
        _lastSeriesEnd = default;
        _staticLineGeometry = null;
        _staticFillGeometry = null;
        _dynamicLineGeometry = null;
        _dynamicFillGeometry = null;
        _staticSourceCount = 0;
        _dynamicSourceCount = 0;
        _cachedStaticEndExclusive = -1;
        _cachedDynamicStartIndex = -1;
        _cachedPathCountForTesting = 0;
        _staticGeometryDirty = true;
        _dynamicGeometryDirty = true;
    }

    private void DrawLevelBands(DrawingContext context, Rect plot)
    {
        var quietTop = MapDbToY(plot, _baselineDb);
        var normalTop = MapDbToY(plot, _baselineDb + 10d);
        var noisyTop = MapDbToY(plot, _baselineDb + 20d);

        context.DrawRectangle(ExtremeBandBrush, null, new Rect(plot.Left, plot.Top, plot.Width, Math.Max(0, noisyTop - plot.Top)));
        context.DrawRectangle(NoisyBandBrush, null, new Rect(plot.Left, noisyTop, plot.Width, Math.Max(0, normalTop - noisyTop)));
        context.DrawRectangle(NormalBandBrush, null, new Rect(plot.Left, normalTop, plot.Width, Math.Max(0, quietTop - normalTop)));
        context.DrawRectangle(QuietBandBrush, null, new Rect(plot.Left, quietTop, plot.Width, Math.Max(0, plot.Bottom - quietTop)));
    }

    private void DrawGrid(DrawingContext context, Rect plot)
    {
        if (_gridGeometry is null || _axisGeometry is null || _cachedGridPlot != plot)
        {
            _cachedGridPlot = plot;
            (_gridGeometry, _axisGeometry) = BuildGridGeometry(plot);
        }

        context.DrawGeometry(brush: null, pen: GridPen, _gridGeometry);
        context.DrawGeometry(brush: null, pen: AxisPen, _axisGeometry);
    }

    private static (StreamGeometry Grid, StreamGeometry Axis) BuildGridGeometry(Rect plot)
    {
        const int horizontalDivisions = 4;
        const int verticalDivisions = 4;

        var grid = new StreamGeometry();
        using (var builder = grid.Open())
        {
            for (var i = 0; i <= horizontalDivisions; i++)
            {
                var y = plot.Top + plot.Height * (i / (double)horizontalDivisions);
                AddLine(builder, new Point(plot.Left, y), new Point(plot.Right, y));
            }

            for (var i = 0; i <= verticalDivisions; i++)
            {
                var x = plot.Left + plot.Width * (i / (double)verticalDivisions);
                AddLine(builder, new Point(x, plot.Top), new Point(x, plot.Bottom));
            }
        }

        var axis = new StreamGeometry();
        using (var builder = axis.Open())
        {
            AddLine(builder, new Point(plot.Left, plot.Top), new Point(plot.Left, plot.Bottom));
            AddLine(builder, new Point(plot.Left, plot.Bottom), new Point(plot.Right, plot.Bottom));
        }

        return (grid, axis);
    }

    private static void AddLine(StreamGeometryContext builder, Point start, Point end)
    {
        builder.BeginFigure(start, isFilled: false);
        builder.LineTo(end);
        builder.EndFigure(isClosed: false);
    }

    private void EnsureGeometry(Rect plot)
    {
        var visibleDurationSeconds = _isStaticSeries
            ? ResolveVisibleDurationSeconds(_points)
            : RealtimeVisibleSeconds;
        var pixelsPerSecond = plot.Width / Math.Max(0.001, visibleDurationSeconds);
        var latestLogicalX = MapTimestampToLogicalX(_points[^1].Timestamp, _logicalOrigin, pixelsPerSecond);
        _viewportTranslateX = plot.Right - latestLogicalX;

        var metricsChanged = _cachedPlot != plot ||
                             Math.Abs(_cachedPixelsPerSecond - pixelsPerSecond) >= 0.001 ||
                             Math.Abs(_cachedBaselineDb - _baselineDb) >= 0.001;
        if (metricsChanged)
        {
            _cachedPlot = plot;
            _cachedPixelsPerSecond = pixelsPerSecond;
            _cachedBaselineDb = _baselineDb;
            _staticGeometryDirty = true;
            _dynamicGeometryDirty = true;
        }

        var firstTailIndex = _isStaticSeries
            ? _points.Count
            : ResolveFirstTailIndex(_points, TimeSpan.FromSeconds(DynamicTailSeconds));
        var dynamicStartIndex = _isStaticSeries ? _points.Count : Math.Max(0, firstTailIndex - 1);
        var staticEndExclusive = firstTailIndex;

        if (staticEndExclusive != _cachedStaticEndExclusive)
        {
            _staticGeometryDirty = true;
        }

        if (dynamicStartIndex != _cachedDynamicStartIndex)
        {
            _dynamicGeometryDirty = true;
        }

        if (_staticGeometryDirty)
        {
            RebuildStaticGeometry(plot, pixelsPerSecond, staticEndExclusive);
        }

        if (_dynamicGeometryDirty)
        {
            RebuildDynamicGeometry(plot, pixelsPerSecond, dynamicStartIndex);
        }
    }

    private void EnsureGeometryPlanForTesting(Rect plot)
    {
        var visibleDurationSeconds = _isStaticSeries
            ? ResolveVisibleDurationSeconds(_points)
            : RealtimeVisibleSeconds;
        var pixelsPerSecond = plot.Width / Math.Max(0.001, visibleDurationSeconds);
        var latestLogicalX = MapTimestampToLogicalX(_points[^1].Timestamp, _logicalOrigin, pixelsPerSecond);
        _viewportTranslateX = plot.Right - latestLogicalX;

        var metricsChanged = _cachedPlot != plot ||
                             Math.Abs(_cachedPixelsPerSecond - pixelsPerSecond) >= 0.001 ||
                             Math.Abs(_cachedBaselineDb - _baselineDb) >= 0.001;
        if (metricsChanged)
        {
            _cachedPlot = plot;
            _cachedPixelsPerSecond = pixelsPerSecond;
            _cachedBaselineDb = _baselineDb;
            _staticGeometryDirty = true;
            _dynamicGeometryDirty = true;
        }

        var firstTailIndex = _isStaticSeries
            ? _points.Count
            : ResolveFirstTailIndex(_points, TimeSpan.FromSeconds(DynamicTailSeconds));
        var dynamicStartIndex = _isStaticSeries ? _points.Count : Math.Max(0, firstTailIndex - 1);
        var staticEndExclusive = firstTailIndex;

        if (staticEndExclusive != _cachedStaticEndExclusive)
        {
            _staticGeometryDirty = true;
        }

        if (dynamicStartIndex != _cachedDynamicStartIndex)
        {
            _dynamicGeometryDirty = true;
        }

        if (_staticGeometryDirty)
        {
            _staticSourceCount = staticEndExclusive >= 2 ? staticEndExclusive : 0;
            _cachedStaticEndExclusive = staticEndExclusive;
            _staticGeometryDirty = false;
            _staticPathBuildVersion++;
        }

        if (_dynamicGeometryDirty)
        {
            _dynamicSourceCount = !_isStaticSeries && _points.Count - dynamicStartIndex >= 2
                ? _points.Count - dynamicStartIndex
                : 0;
            _cachedDynamicStartIndex = dynamicStartIndex;
            _dynamicGeometryDirty = false;
            _dynamicPathBuildVersion++;
        }

        _cachedPathCountForTesting = (_staticSourceCount >= 2 ? 2 : 0) + (_dynamicSourceCount >= 2 ? 2 : 0);
    }

    private void RebuildStaticGeometry(Rect plot, double pixelsPerSecond, int staticEndExclusive)
    {
        _staticLineGeometry = null;
        _staticFillGeometry = null;
        _staticSourceCount = 0;

        if (staticEndExclusive >= 2)
        {
            (_staticLineGeometry, _staticFillGeometry, _staticSourceCount) = BuildLayerGeometry(
                startIndex: 0,
                endExclusive: staticEndExclusive,
                plot,
                pixelsPerSecond);
        }

        _cachedStaticEndExclusive = staticEndExclusive;
        _staticGeometryDirty = false;
        _staticPathBuildVersion++;
    }

    private void RebuildDynamicGeometry(Rect plot, double pixelsPerSecond, int dynamicStartIndex)
    {
        _dynamicLineGeometry = null;
        _dynamicFillGeometry = null;
        _dynamicSourceCount = 0;

        if (!_isStaticSeries && _points.Count - dynamicStartIndex >= 2)
        {
            (_dynamicLineGeometry, _dynamicFillGeometry, _dynamicSourceCount) = BuildLayerGeometry(
                dynamicStartIndex,
                _points.Count,
                plot,
                pixelsPerSecond);
        }

        _cachedDynamicStartIndex = dynamicStartIndex;
        _dynamicGeometryDirty = false;
        _dynamicPathBuildVersion++;
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

        var maxSamples = Math.Clamp((int)Math.Floor(plot.Width), 64, 420);
        var pointCount = BuildPlotPoints(startIndex, endExclusive, plot, pixelsPerSecond, maxSamples);
        if (pointCount < 2 || _pointBuffer is null)
        {
            return (null, null, sourceCount);
        }

        var lineGeometry = new StreamGeometry();
        using (var builder = lineGeometry.Open())
        {
            AddSmoothedPath(builder, _pointBuffer, pointCount, isFilled: false);
        }

        var fillGeometry = new StreamGeometry();
        using (var builder = fillGeometry.Open())
        {
            var first = _pointBuffer[0];
            builder.BeginFigure(new Point(first.X, plot.Bottom), true);
            builder.LineTo(first);
            AddSmoothedSegments(builder, _pointBuffer, pointCount);

            var last = _pointBuffer[pointCount - 1];
            builder.LineTo(new Point(last.X, plot.Bottom));
            builder.LineTo(new Point(first.X, plot.Bottom));
            builder.EndFigure(true);
        }

        return (lineGeometry, fillGeometry, sourceCount);
    }

    private static void AddSmoothedPath(StreamGeometryContext builder, Point[] points, int pointCount, bool isFilled)
    {
        builder.BeginFigure(points[0], isFilled);
        AddSmoothedSegments(builder, points, pointCount);
        builder.EndFigure(isClosed: false);
    }

    private static void AddSmoothedSegments(StreamGeometryContext builder, Point[] points, int pointCount)
    {
        if (pointCount < 2)
        {
            return;
        }

        if (pointCount == 2)
        {
            builder.LineTo(points[1]);
            return;
        }

        for (var i = 0; i < pointCount - 1; i++)
        {
            var p0 = i == 0 ? points[i] : points[i - 1];
            var p1 = points[i];
            var p2 = points[i + 1];
            var p3 = i + 2 < pointCount ? points[i + 2] : p2;

            var control1 = new Point(
                p1.X + (p2.X - p0.X) / 6d,
                p1.Y + (p2.Y - p0.Y) / 6d);
            var control2 = new Point(
                p2.X - (p3.X - p1.X) / 6d,
                p2.Y - (p3.Y - p1.Y) / 6d);

            builder.CubicBezierTo(control1, control2, p2);
        }
    }

    private int BuildPlotPoints(
        int startIndex,
        int endExclusive,
        Rect plot,
        double pixelsPerSecond,
        int maxSamples)
    {
        var sourceCount = endExclusive - startIndex;
        if (sourceCount <= 1)
        {
            return 0;
        }

        if (sourceCount <= maxSamples)
        {
            EnsurePointBufferCapacity(sourceCount);
            if (_pointBuffer is null)
            {
                return 0;
            }

            for (var i = 0; i < sourceCount; i++)
            {
                _pointBuffer[i] = MapToPlot(plot, _points[startIndex + i], pixelsPerSecond);
            }

            return sourceCount;
        }

        var bucketCount = Math.Max(1, (maxSamples - 2) / 2);
        var targetCapacity = 2 + bucketCount * 2;
        EnsurePointBufferCapacity(targetCapacity);
        if (_pointBuffer is null)
        {
            return 0;
        }

        var outputIndex = 0;
        _pointBuffer[outputIndex++] = MapToPlot(plot, _points[startIndex], pixelsPerSecond);

        var middleCount = sourceCount - 2;
        var bucketWidth = middleCount / (double)bucketCount;
        var lastSourceIndex = startIndex;

        for (var bucket = 0; bucket < bucketCount; bucket++)
        {
            var rangeStart = startIndex + 1 + (int)Math.Floor(bucket * bucketWidth);
            var rangeEnd = startIndex + 1 + (int)Math.Floor((bucket + 1) * bucketWidth);
            if (bucket == bucketCount - 1)
            {
                rangeEnd = endExclusive - 1;
            }

            rangeStart = Math.Clamp(rangeStart, startIndex + 1, endExclusive - 2);
            rangeEnd = Math.Clamp(rangeEnd, rangeStart + 1, endExclusive - 1);

            var minIndex = rangeStart;
            var maxIndex = rangeStart;
            var minValue = _points[rangeStart].DisplayDb;
            var maxValue = minValue;

            for (var i = rangeStart + 1; i < rangeEnd; i++)
            {
                var value = _points[i].DisplayDb;
                if (value < minValue)
                {
                    minValue = value;
                    minIndex = i;
                }

                if (value > maxValue)
                {
                    maxValue = value;
                    maxIndex = i;
                }
            }

            if (minIndex == maxIndex)
            {
                if (minIndex != lastSourceIndex)
                {
                    _pointBuffer[outputIndex++] = MapToPlot(plot, _points[minIndex], pixelsPerSecond);
                    lastSourceIndex = minIndex;
                }

                continue;
            }

            var first = minIndex < maxIndex ? minIndex : maxIndex;
            var second = minIndex < maxIndex ? maxIndex : minIndex;

            if (first != lastSourceIndex)
            {
                _pointBuffer[outputIndex++] = MapToPlot(plot, _points[first], pixelsPerSecond);
                lastSourceIndex = first;
            }

            if (second != lastSourceIndex)
            {
                _pointBuffer[outputIndex++] = MapToPlot(plot, _points[second], pixelsPerSecond);
                lastSourceIndex = second;
            }
        }

        var finalIndex = endExclusive - 1;
        if (finalIndex != lastSourceIndex)
        {
            _pointBuffer[outputIndex++] = MapToPlot(plot, _points[finalIndex], pixelsPerSecond);
        }

        return outputIndex;
    }

    private Point MapToPlot(Rect plot, NoiseRealtimePoint point, double pixelsPerSecond)
    {
        var x = MapTimestampToLogicalX(point.Timestamp, _logicalOrigin, pixelsPerSecond);
        var y = MapDbToY(plot, point.DisplayDb);
        return new Point(x, y);
    }

    private double MapDbToY(Rect plot, double displayDb)
    {
        var minDb = _baselineDb - 5d;
        var maxDb = _baselineDb + 25d;
        var normalized = (displayDb - minDb) / Math.Max(0.001, maxDb - minDb);
        normalized = Math.Clamp(normalized, 0, 1);
        return plot.Bottom - normalized * plot.Height;
    }

    private void DrawLatestPoint(DrawingContext context, Rect plot)
    {
        if (_points.Count == 0)
        {
            return;
        }

        var latest = _points[^1];
        var center = MapToPlot(plot, latest, _cachedPixelsPerSecond);
        var level = ResolveLevel(latest.DisplayDb, _baselineDb);
        var levelBrush = GetLevelBrush(level);
        var radius = Math.Clamp(Math.Min(plot.Width, plot.Height) / 34d, 3.5, 8);

        context.DrawEllipse(LatestGlowBrush, null, center, radius * 2.8, radius * 2.8);
        context.DrawEllipse(levelBrush, LatestPointPen, center, radius, radius);
        context.DrawEllipse(LatestPointBrush, null, center, radius * 0.36, radius * 0.36);
    }

    private static IBrush GetLevelBrush(NoiseDistributionLevel level)
    {
        return level switch
        {
            NoiseDistributionLevel.Quiet => new SolidColorBrush(Color.Parse("#FF34D399")),
            NoiseDistributionLevel.Normal => new SolidColorBrush(Color.Parse("#FF60A5FA")),
            NoiseDistributionLevel.Noisy => new SolidColorBrush(Color.Parse("#FFF59E0B")),
            NoiseDistributionLevel.Extreme => new SolidColorBrush(Color.Parse("#FFEF4444")),
            _ => new SolidColorBrush(Color.Parse("#FF60A5FA"))
        };
    }

    private void EnsurePointBufferCapacity(int required)
    {
        if (required <= 0)
        {
            return;
        }

        if (_pointBuffer is not null && _pointBuffer.Length >= required)
        {
            return;
        }

        var next = ArrayPool<Point>.Shared.Rent(required);
        if (_pointBuffer is not null)
        {
            ArrayPool<Point>.Shared.Return(_pointBuffer, clearArray: false);
        }

        _pointBuffer = next;
    }

    private void ReleasePointBuffer()
    {
        if (_pointBuffer is null)
        {
            return;
        }

        ArrayPool<Point>.Shared.Return(_pointBuffer, clearArray: false);
        _pointBuffer = null;
    }

    private static IBrush CreateAreaGradientBrush(byte alpha)
    {
        return new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops = new GradientStops
            {
                new GradientStop(Color.FromArgb(alpha, 0xEF, 0x44, 0x44), 0.00),
                new GradientStop(Color.FromArgb(alpha, 0xF5, 0x9E, 0x0B), 0.28),
                new GradientStop(Color.FromArgb(alpha, 0x60, 0xA5, 0xFA), 0.62),
                new GradientStop(Color.FromArgb(alpha, 0x34, 0xD3, 0x99), 1.00)
            }
        };
    }

    private static int ComputeSeriesSignature(
        IReadOnlyList<NoiseRealtimePoint> points,
        double baselineDb,
        bool isStaticSeries)
    {
        if (points.Count == 0)
        {
            return HashCode.Combine(0, Math.Round(baselineDb, 2), isStaticSeries);
        }

        var first = points[0];
        var last = points[^1];
        return HashCode.Combine(
            points.Count,
            first.Timestamp.UtcTicks,
            Math.Round(first.DisplayDb, 2),
            last.Timestamp.UtcTicks,
            Math.Round(last.DisplayDb, 2),
            Math.Round(baselineDb, 2),
            isStaticSeries);
    }
}

public enum NoiseDistributionLevel
{
    Quiet = 0,
    Normal = 1,
    Noisy = 2,
    Extreme = 3
}
