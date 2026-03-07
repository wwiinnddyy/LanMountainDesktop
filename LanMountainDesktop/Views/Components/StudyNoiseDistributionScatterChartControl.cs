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

    private static readonly IBrush QuietBrush = new SolidColorBrush(Color.Parse("#FF34D399"));
    private static readonly IBrush NormalBrush = new SolidColorBrush(Color.Parse("#FF60A5FA"));
    private static readonly IBrush NoisyBrush = new SolidColorBrush(Color.Parse("#FFF59E0B"));
    private static readonly IBrush ExtremeBrush = new SolidColorBrush(Color.Parse("#FFEF4444"));

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

        if (_points.Count < 2)
        {
            return;
        }

        DrawElectronCloud(context, plot);
    }

    private void DrawElectronCloud(DrawingContext context, Rect plot)
    {
        var start = _points[0].Timestamp;
        var end = _points[^1].Timestamp;
        var totalTicks = Math.Max(1, (end - start).Ticks);

        var pointCount = _points.Count;
        var cloudLayers = 8;
        var baseRadius = Math.Clamp(Math.Min(plot.Width, plot.Height) / 45d, 3, 12);
        
        var sortedPoints = new List<(double X, double Y, NoiseDistributionLevel Level)>();
        for (var i = 0; i < pointCount; i++)
        {
            var point = _points[i];
            var x = MapX(plot, point.Timestamp, start, totalTicks);
            var y = MapYContinuous(plot, point.DisplayDb);
            var level = ResolveLevel(point.DisplayDb, _baselineDb);
            sortedPoints.Add((x, y, level));
        }

        sortedPoints.Sort((a, b) => a.X.CompareTo(b.X));

        for (var layer = cloudLayers - 1; layer >= 0; layer--)
        {
            var layerRatio = (double)layer / (cloudLayers - 1);
            var layerRadius = baseRadius * (1.2 + layerRatio * 0.8);
            var layerAlpha = (byte)(40 + layerRatio * 25);

            foreach (var pt in sortedPoints)
            {
                var brush = GetLevelBrushWithAlpha(pt.Level, layerAlpha);
                var jitterX = ComputeJitter(pt.X * 1000 + layer) * layerRadius * 0.3;
                var jitterY = ComputeJitter(pt.Y * 1000 + layer) * layerRadius * 0.3;
                
                context.DrawEllipse(
                    brush,
                    pen: null,
                    center: new Point(pt.X + jitterX, pt.Y + jitterY),
                    radiusX: layerRadius,
                    radiusY: layerRadius * 0.7);
            }
        }

        var glowLayers = 5;
        for (var layer = glowLayers - 1; layer >= 0; layer--)
        {
            var layerRatio = (double)layer / (glowLayers - 1);
            var layerRadius = baseRadius * (0.8 + layerRatio * 0.6);
            var layerAlpha = (byte)(20 + layerRatio * 15);

            foreach (var pt in sortedPoints)
            {
                var brush = GetLevelBrushWithAlpha(pt.Level, layerAlpha);
                context.DrawEllipse(
                    brush,
                    pen: null,
                    center: new Point(pt.X, pt.Y),
                    radiusX: layerRadius,
                    radiusY: layerRadius * 0.6);
            }
        }

        var latest = _points[^1];
        var latestX = MapX(plot, latest.Timestamp, start, totalTicks);
        var latestY = MapYContinuous(plot, latest.DisplayDb);
        var latestLevel = ResolveLevel(latest.DisplayDb, _baselineDb);

        for (var i = 3; i >= 0; i--)
        {
            var radius = baseRadius * (1.5 + i * 0.8);
            var alpha = (byte)(30 - i * 6);
            var glowBrush = GetLevelBrushWithAlpha(latestLevel, alpha);
            context.DrawEllipse(glowBrush, null, new Point(latestX, latestY), radius, radius * 0.6);
        }

        context.DrawEllipse(
            GetLevelBrush(latestLevel),
            new Pen(Brushes.White, 1.5),
            new Point(latestX, latestY),
            baseRadius + 1,
            baseRadius * 0.7 + 1);

        context.DrawEllipse(
            Brushes.White,
            null,
            new Point(latestX, latestY),
            2,
            2);
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

    private double MapYContinuous(Rect plot, double displayDb)
    {
        var minDb = _baselineDb - 5;
        var maxDb = _baselineDb + 25;
        var dbRange = maxDb - minDb;
        if (dbRange <= 0) dbRange = 30;

        var normalizedDb = (displayDb - minDb) / dbRange;
        normalizedDb = Math.Clamp(normalizedDb, 0, 1);

        return plot.Bottom - (normalizedDb * plot.Height);
    }

    private static double ComputeJitter(double value)
    {
        var hash = (ulong)(value * 1000000);
        hash ^= hash >> 33;
        hash *= 0xff51afd7ed558ccdUL;
        hash ^= hash >> 33;
        hash *= 0xc4ceb9fe1a85ec53UL;
        hash ^= hash >> 33;
        var normalized = (hash & 0xFFFF) / 65535d;
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
            NoiseDistributionLevel.Quiet => QuietBrush,
            NoiseDistributionLevel.Normal => NormalBrush,
            NoiseDistributionLevel.Noisy => NoisyBrush,
            NoiseDistributionLevel.Extreme => ExtremeBrush,
            _ => NormalBrush
        };
    }

    private static IBrush GetLevelBrushWithAlpha(NoiseDistributionLevel level, byte alpha)
    {
        return level switch
        {
            NoiseDistributionLevel.Quiet => new SolidColorBrush(Color.FromArgb(alpha, 0x34, 0xD3, 0x99)),
            NoiseDistributionLevel.Normal => new SolidColorBrush(Color.FromArgb(alpha, 0x60, 0xA5, 0xFA)),
            NoiseDistributionLevel.Noisy => new SolidColorBrush(Color.FromArgb(alpha, 0xF5, 0x9E, 0x0B)),
            NoiseDistributionLevel.Extreme => new SolidColorBrush(Color.FromArgb(alpha, 0xEF, 0x44, 0x44)),
            _ => new SolidColorBrush(Color.FromArgb(alpha, 0x60, 0xA5, 0xFA))
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
