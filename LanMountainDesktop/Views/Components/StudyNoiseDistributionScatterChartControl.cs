using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using LanMountainDesktop.Models;

namespace LanMountainDesktop.Views.Components;

public sealed class StudyNoiseDistributionScatterChartControl : Control
{
    private static readonly IBrush GridBrush = new SolidColorBrush(Color.Parse("#2E5E7A96"));
    private static readonly IBrush AxisBrush = new SolidColorBrush(Color.Parse("#5C6D86A1"));
    private static readonly Pen GridPen = new(GridBrush, 1);
    private static readonly Pen AxisPen = new(AxisBrush, 1.1);

    private static readonly IBrush QuietPointBrush = new SolidColorBrush(Color.Parse("#FF34D399"));
    private static readonly IBrush NormalPointBrush = new SolidColorBrush(Color.Parse("#FF60A5FA"));
    private static readonly IBrush NoisyPointBrush = new SolidColorBrush(Color.Parse("#FFF59E0B"));
    private static readonly IBrush ExtremePointBrush = new SolidColorBrush(Color.Parse("#FFEF4444"));

    private IReadOnlyList<NoiseRealtimePoint> _points = Array.Empty<NoiseRealtimePoint>();
    private double _baselineDb = 45;

    public void UpdateSeries(IReadOnlyList<NoiseRealtimePoint>? points, double baselineDb)
    {
        _points = points ?? Array.Empty<NoiseRealtimePoint>();
        _baselineDb = Math.Clamp(baselineDb, 20, 85);
        InvalidateVisual();
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

        if (_points.Count == 0)
        {
            return;
        }

        var start = _points[0].Timestamp;
        var end = _points[^1].Timestamp;
        var totalTicks = Math.Max(1, (end - start).Ticks);

        var maxRenderPoints = Math.Clamp((int)Math.Floor(plot.Width * 1.5), 80, 520);
        var step = Math.Max(1, _points.Count / Math.Max(1, maxRenderPoints));
        var radius = Math.Clamp(Math.Min(plot.Width, plot.Height) / 88d, 1.4, 3.8);

        for (var i = 0; i < _points.Count; i += step)
        {
            var point = _points[i];
            var level = ResolveLevel(point.DisplayDb, _baselineDb);
            var x = MapX(plot, point.Timestamp, start, totalTicks);
            var y = MapY(plot, level, point.Timestamp);
            context.DrawEllipse(GetLevelBrush(level), pen: null, center: new Point(x, y), radiusX: radius, radiusY: radius);
        }

        // Ensure latest point is always visible.
        var latest = _points[^1];
        var latestLevel = ResolveLevel(latest.DisplayDb, _baselineDb);
        var latestX = MapX(plot, latest.Timestamp, start, totalTicks);
        var latestY = MapY(plot, latestLevel, latest.Timestamp);
        context.DrawEllipse(GetLevelBrush(latestLevel), pen: new Pen(Brushes.White, 1), center: new Point(latestX, latestY), radiusX: radius + 0.8, radiusY: radius + 0.8);
    }

    private static void DrawGrid(DrawingContext context, Rect plot)
    {
        const int verticalDivisions = 4;

        for (var i = 0; i <= verticalDivisions; i++)
        {
            var x = plot.Left + plot.Width * (i / (double)verticalDivisions);
            context.DrawLine(GridPen, new Point(x, plot.Top), new Point(x, plot.Bottom));
        }

        for (var i = 0; i <= 4; i++)
        {
            var y = plot.Top + plot.Height * (i / 4d);
            context.DrawLine(GridPen, new Point(plot.Left, y), new Point(plot.Right, y));
        }

        context.DrawLine(AxisPen, new Point(plot.Left, plot.Top), new Point(plot.Left, plot.Bottom));
        context.DrawLine(AxisPen, new Point(plot.Left, plot.Bottom), new Point(plot.Right, plot.Bottom));
    }

    private static double MapX(Rect plot, DateTimeOffset timestamp, DateTimeOffset start, long totalTicks)
    {
        var offsetTicks = Math.Clamp((timestamp - start).Ticks, 0, totalTicks);
        return plot.Left + plot.Width * (offsetTicks / (double)totalTicks);
    }

    private static double MapY(Rect plot, NoiseDistributionLevel level, DateTimeOffset timestamp)
    {
        // 4 bands: quiet(bottom) -> extreme(top). Add deterministic jitter in each band.
        var bandHeight = plot.Height / 4d;
        var levelIndex = level switch
        {
            NoiseDistributionLevel.Quiet => 0,
            NoiseDistributionLevel.Normal => 1,
            NoiseDistributionLevel.Noisy => 2,
            NoiseDistributionLevel.Extreme => 3,
            _ => 1
        };

        var centerY = plot.Bottom - ((levelIndex + 0.5) * bandHeight);
        var jitter = ComputeJitter(timestamp.Ticks) * bandHeight * 0.26;
        return Math.Clamp(centerY + jitter, plot.Top + 1.5, plot.Bottom - 1.5);
    }

    private static double ComputeJitter(long ticks)
    {
        // Deterministic pseudo-random value in [-1, 1] to avoid overlap without animation noise.
        var value = (ulong)ticks;
        value ^= value >> 33;
        value *= 0xff51afd7ed558ccdUL;
        value ^= value >> 33;
        value *= 0xc4ceb9fe1a85ec53UL;
        value ^= value >> 33;
        var normalized = (value & 0xFFFF) / 65535d;
        return (normalized * 2d) - 1d;
    }

    private static NoiseDistributionLevel ResolveLevel(double displayDb, double baselineDb)
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

    private static IBrush GetLevelBrush(NoiseDistributionLevel level)
    {
        return level switch
        {
            NoiseDistributionLevel.Quiet => QuietPointBrush,
            NoiseDistributionLevel.Normal => NormalPointBrush,
            NoiseDistributionLevel.Noisy => NoisyPointBrush,
            NoiseDistributionLevel.Extreme => ExtremePointBrush,
            _ => NormalPointBrush
        };
    }
}

public enum NoiseDistributionLevel
{
    Quiet = 0,
    Normal = 1,
    Noisy = 2,
    Extreme = 3
}
