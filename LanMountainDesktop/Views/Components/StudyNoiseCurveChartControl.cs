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
    private static readonly IBrush GridBrush = new SolidColorBrush(Color.Parse("#324E6780"));
    private static readonly IBrush AxisBrush = new SolidColorBrush(Color.Parse("#5C6D86A1"));
    private static readonly IBrush LineBrush = new SolidColorBrush(Color.Parse("#FF52AEEA"));
    private static readonly IBrush FillBrush = new SolidColorBrush(Color.Parse("#3552AEEA"));
    private static readonly Pen GridPen = new(GridBrush, 1);
    private static readonly Pen AxisPen = new(AxisBrush, 1.1);
    private static readonly Pen LinePen = new(LineBrush, 1.8);

    private IReadOnlyList<NoiseRealtimePoint> _points = Array.Empty<NoiseRealtimePoint>();
    private Point[]? _pointBuffer;
    private StreamGeometry? _lineGeometry;
    private StreamGeometry? _fillGeometry;
    private Rect _cachedPlot;
    private bool _geometryDirty = true;
    private int _lastSeriesSignature;

    public void UpdateSeries(IReadOnlyList<NoiseRealtimePoint>? points)
    {
        var nextPoints = points ?? Array.Empty<NoiseRealtimePoint>();
        var nextSignature = ComputeSeriesSignature(nextPoints);
        if (ReferenceEquals(_points, nextPoints) && _lastSeriesSignature == nextSignature)
        {
            return;
        }

        _points = nextPoints;
        _lastSeriesSignature = nextSignature;
        _geometryDirty = true;
        InvalidateVisual();
    }

    public void CompactCaches()
    {
        if (_pointBuffer is not null && _pointBuffer.Length > 2048)
        {
            ArrayPool<Point>.Shared.Return(_pointBuffer, clearArray: false);
            _pointBuffer = null;
        }

        _lineGeometry = null;
        _fillGeometry = null;
        _geometryDirty = true;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        ReleasePointBuffer();
        _lineGeometry = null;
        _fillGeometry = null;
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

        DrawGrid(context, plot);

        if (_points.Count < 2)
        {
            return;
        }

        EnsureGeometry(plot);
        if (_lineGeometry is null || _fillGeometry is null)
        {
            return;
        }

        context.DrawGeometry(FillBrush, pen: null, _fillGeometry);
        context.DrawGeometry(brush: null, pen: LinePen, _lineGeometry);
    }

    private static void DrawGrid(DrawingContext context, Rect plot)
    {
        const int horizontalDivisions = 4;
        const int verticalDivisions = 4;

        for (var i = 0; i <= horizontalDivisions; i++)
        {
            var y = plot.Top + plot.Height * (i / (double)horizontalDivisions);
            context.DrawLine(GridPen, new Point(plot.Left, y), new Point(plot.Right, y));
        }

        for (var i = 0; i <= verticalDivisions; i++)
        {
            var x = plot.Left + plot.Width * (i / (double)verticalDivisions);
            context.DrawLine(GridPen, new Point(x, plot.Top), new Point(x, plot.Bottom));
        }

        context.DrawLine(AxisPen, new Point(plot.Left, plot.Top), new Point(plot.Left, plot.Bottom));
        context.DrawLine(AxisPen, new Point(plot.Left, plot.Bottom), new Point(plot.Right, plot.Bottom));
    }

    private void EnsureGeometry(Rect plot)
    {
        if (!_geometryDirty && _cachedPlot == plot)
        {
            return;
        }

        _cachedPlot = plot;
        _lineGeometry = null;
        _fillGeometry = null;

        var maxSamples = Math.Clamp((int)Math.Floor(plot.Width), 56, 360);
        var pointCount = BuildPlotPoints(plot, maxSamples);
        if (pointCount < 2 || _pointBuffer is null)
        {
            _geometryDirty = false;
            return;
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

        _lineGeometry = lineGeometry;
        _fillGeometry = fillGeometry;
        _geometryDirty = false;
    }

    private int BuildPlotPoints(Rect plot, int maxSamples)
    {
        var sourceCount = _points.Count;
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
                _pointBuffer[i] = MapToPlot(plot, _points[i], _points[0].Timestamp, _points[^1].Timestamp);
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
        var startTimestamp = _points[0].Timestamp;
        var endTimestamp = _points[^1].Timestamp;
        _pointBuffer[outputIndex++] = MapToPlot(plot, _points[0], startTimestamp, endTimestamp);

        var middleCount = sourceCount - 2;
        var bucketWidth = middleCount / (double)bucketCount;
        var lastSourceIndex = 0;

        for (var bucket = 0; bucket < bucketCount; bucket++)
        {
            var rangeStart = 1 + (int)Math.Floor(bucket * bucketWidth);
            var rangeEnd = 1 + (int)Math.Floor((bucket + 1) * bucketWidth);
            if (bucket == bucketCount - 1)
            {
                rangeEnd = sourceCount - 1;
            }

            rangeStart = Math.Clamp(rangeStart, 1, sourceCount - 2);
            rangeEnd = Math.Clamp(rangeEnd, rangeStart + 1, sourceCount - 1);

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
                    _pointBuffer[outputIndex++] = MapToPlot(plot, _points[minIndex], startTimestamp, endTimestamp);
                    lastSourceIndex = minIndex;
                }

                continue;
            }

            var first = minIndex < maxIndex ? minIndex : maxIndex;
            var second = minIndex < maxIndex ? maxIndex : minIndex;

            if (first != lastSourceIndex)
            {
                _pointBuffer[outputIndex++] = MapToPlot(plot, _points[first], startTimestamp, endTimestamp);
                lastSourceIndex = first;
            }

            if (second != lastSourceIndex)
            {
                _pointBuffer[outputIndex++] = MapToPlot(plot, _points[second], startTimestamp, endTimestamp);
                lastSourceIndex = second;
            }
        }

        var finalIndex = sourceCount - 1;
        if (finalIndex != lastSourceIndex)
        {
            _pointBuffer[outputIndex++] = MapToPlot(plot, _points[finalIndex], startTimestamp, endTimestamp);
        }

        return outputIndex;
    }

    private static Point MapToPlot(
        Rect plot,
        NoiseRealtimePoint point,
        DateTimeOffset start,
        DateTimeOffset end)
    {
        const double minDisplayDb = 20;
        const double maxDisplayDb = 100;

        var rangeTicks = Math.Max(1, (end - start).Ticks);
        var offsetTicks = Math.Clamp((point.Timestamp - start).Ticks, 0, rangeTicks);
        var x = plot.Left + plot.Width * (offsetTicks / (double)rangeTicks);

        var clampedDb = Math.Clamp(point.DisplayDb, minDisplayDb, maxDisplayDb);
        var normalized = (clampedDb - minDisplayDb) / (maxDisplayDb - minDisplayDb);
        var y = plot.Bottom - normalized * plot.Height;
        return new Point(x, y);
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
            last.Timestamp.UtcTicks,
            Math.Round(last.DisplayDb, 2));
    }
}
