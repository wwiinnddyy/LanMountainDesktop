using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using LanMountainDesktop.Models;

namespace LanMountainDesktop.Views.Components;

public sealed class StudyNoiseDistributionScatterChartControl : Control
{
    private readonly record struct SampledPoint(double X, double Y, NoiseDistributionLevel Level);

    private static readonly IBrush GridBrush = new SolidColorBrush(Color.Parse("#2E5E7A96"));
    private static readonly IBrush AxisBrush = new SolidColorBrush(Color.Parse("#5C6D86A1"));
    private static readonly Pen GridPen = new(GridBrush, 1);
    private static readonly Pen AxisPen = new(AxisBrush, 1.1);

    private static readonly IBrush QuietBrush = new SolidColorBrush(Color.Parse("#FF34D399"));
    private static readonly IBrush NormalBrush = new SolidColorBrush(Color.Parse("#FF60A5FA"));
    private static readonly IBrush NoisyBrush = new SolidColorBrush(Color.Parse("#FFF59E0B"));
    private static readonly IBrush ExtremeBrush = new SolidColorBrush(Color.Parse("#FFEF4444"));
    private static readonly byte[] CloudAlphas = [44, 58, 72, 86];
    private static readonly byte[] GlowAlphas = [26, 36];
    private static readonly IBrush[][] CloudBrushes = CreateBrushTable(CloudAlphas);
    private static readonly IBrush[][] GlowBrushes = CreateBrushTable(GlowAlphas);

    private IReadOnlyList<NoiseRealtimePoint> _points = Array.Empty<NoiseRealtimePoint>();
    private SampledPoint[] _sampledPoints = Array.Empty<SampledPoint>();
    private int _sampledPointCount;
    private double _baselineDb = 45;
    private Rect _cachedPlot;
    private bool _sampleCacheDirty = true;
    private int _lastSeriesSignature;

    public void UpdateSeries(IReadOnlyList<NoiseRealtimePoint>? points, double baselineDb)
    {
        var nextPoints = points ?? Array.Empty<NoiseRealtimePoint>();
        var nextBaselineDb = Math.Clamp(baselineDb, 20, 85);
        var nextSignature = ComputeSeriesSignature(nextPoints, nextBaselineDb);
        if (ReferenceEquals(_points, nextPoints) &&
            Math.Abs(_baselineDb - nextBaselineDb) < 0.001 &&
            _lastSeriesSignature == nextSignature)
        {
            return;
        }

        _points = nextPoints;
        _baselineDb = nextBaselineDb;
        _lastSeriesSignature = nextSignature;
        _sampleCacheDirty = true;
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

        EnsureSampleCache(plot);
        if (_sampledPointCount < 2)
        {
            return;
        }

        DrawElectronCloud(context, plot);
    }

    private void DrawElectronCloud(DrawingContext context, Rect plot)
    {
        var cloudLayers = CloudAlphas.Length;
        var baseRadius = Math.Clamp(Math.Min(plot.Width, plot.Height) / 45d, 3, 12);

        for (var layer = cloudLayers - 1; layer >= 0; layer--)
        {
            var layerRatio = cloudLayers == 1 ? 0d : layer / (double)(cloudLayers - 1);
            var layerRadius = baseRadius * (1.2 + layerRatio * 0.8);
            var layerBrushes = CloudBrushes[layer];

            for (var i = 0; i < _sampledPointCount; i++)
            {
                var pt = _sampledPoints[i];
                var jitterX = ComputeJitter(pt.X * 1000 + layer) * layerRadius * 0.3;
                var jitterY = ComputeJitter(pt.Y * 1000 + layer) * layerRadius * 0.3;

                context.DrawEllipse(
                    layerBrushes[(int)pt.Level],
                    pen: null,
                    center: new Point(pt.X + jitterX, pt.Y + jitterY),
                    radiusX: layerRadius,
                    radiusY: layerRadius * 0.7);
            }
        }

        var glowLayers = GlowAlphas.Length;
        for (var layer = glowLayers - 1; layer >= 0; layer--)
        {
            var layerRatio = glowLayers == 1 ? 0d : layer / (double)(glowLayers - 1);
            var layerRadius = baseRadius * (0.8 + layerRatio * 0.6);
            var layerBrushes = GlowBrushes[layer];
            for (var i = 0; i < _sampledPointCount; i++)
            {
                var pt = _sampledPoints[i];
                context.DrawEllipse(
                    layerBrushes[(int)pt.Level],
                    pen: null,
                    center: new Point(pt.X, pt.Y),
                    radiusX: layerRadius,
                    radiusY: layerRadius * 0.6);
            }
        }

        var latest = _sampledPoints[_sampledPointCount - 1];
        for (var i = 3; i >= 0; i--)
        {
            var radius = baseRadius * (1.5 + i * 0.8);
            var alpha = (byte)(30 - i * 6);
            var glowBrush = GetAlphaBrush(latest.Level, alpha);
            context.DrawEllipse(glowBrush, null, new Point(latest.X, latest.Y), radius, radius * 0.6);
        }

        context.DrawEllipse(
            GetLevelBrush(latest.Level),
            new Pen(Brushes.White, 1.5),
            new Point(latest.X, latest.Y),
            baseRadius + 1,
            baseRadius * 0.7 + 1);

        context.DrawEllipse(
            Brushes.White,
            null,
            new Point(latest.X, latest.Y),
            2,
            2);
    }

    private void EnsureSampleCache(Rect plot)
    {
        if (!_sampleCacheDirty && _cachedPlot == plot)
        {
            return;
        }

        _cachedPlot = plot;
        _sampledPointCount = BuildSampledPoints(plot);
        _sampleCacheDirty = false;
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
        if (dbRange <= 0)
        {
            dbRange = 30;
        }

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

    private int BuildSampledPoints(Rect plot)
    {
        if (_points.Count < 2)
        {
            return 0;
        }

        var maxSamples = Math.Clamp((int)Math.Ceiling(plot.Width / 2d), 48, 144);
        var targetCount = Math.Min(_points.Count, maxSamples);
        if (_sampledPoints.Length < targetCount)
        {
            _sampledPoints = new SampledPoint[targetCount];
        }

        var start = _points[0].Timestamp;
        var end = _points[^1].Timestamp;
        var totalTicks = Math.Max(1, (end - start).Ticks);
        var step = _points.Count <= targetCount
            ? 1d
            : (_points.Count - 1d) / Math.Max(1d, targetCount - 1d);

        var outputIndex = 0;
        var lastSourceIndex = -1;
        for (var i = 0; i < targetCount; i++)
        {
            var sourceIndex = i == targetCount - 1
                ? _points.Count - 1
                : (int)Math.Round(i * step);
            sourceIndex = Math.Clamp(sourceIndex, 0, _points.Count - 1);
            if (sourceIndex == lastSourceIndex)
            {
                continue;
            }

            var point = _points[sourceIndex];
            _sampledPoints[outputIndex++] = new SampledPoint(
                MapX(plot, point.Timestamp, start, totalTicks),
                MapYContinuous(plot, point.DisplayDb),
                ResolveLevel(point.DisplayDb, _baselineDb));
            lastSourceIndex = sourceIndex;
        }

        return outputIndex;
    }

    private static int ComputeSeriesSignature(IReadOnlyList<NoiseRealtimePoint> points, double baselineDb)
    {
        if (points.Count == 0)
        {
            return HashCode.Combine(0, baselineDb);
        }

        var first = points[0];
        var last = points[^1];
        return HashCode.Combine(
            points.Count,
            first.Timestamp.UtcTicks,
            last.Timestamp.UtcTicks,
            Math.Round(last.DisplayDb, 2),
            Math.Round(baselineDb, 2));
    }

    private static IBrush[][] CreateBrushTable(IReadOnlyList<byte> alphas)
    {
        var table = new IBrush[alphas.Count][];
        for (var i = 0; i < alphas.Count; i++)
        {
            table[i] =
            [
                GetLevelBrushWithAlpha(NoiseDistributionLevel.Quiet, alphas[i]),
                GetLevelBrushWithAlpha(NoiseDistributionLevel.Normal, alphas[i]),
                GetLevelBrushWithAlpha(NoiseDistributionLevel.Noisy, alphas[i]),
                GetLevelBrushWithAlpha(NoiseDistributionLevel.Extreme, alphas[i])
            ];
        }

        return table;
    }

    private static IBrush GetAlphaBrush(NoiseDistributionLevel level, byte alpha)
    {
        for (var i = 0; i < CloudAlphas.Length; i++)
        {
            if (CloudAlphas[i] == alpha)
            {
                return CloudBrushes[i][(int)level];
            }
        }

        for (var i = 0; i < GlowAlphas.Length; i++)
        {
            if (GlowAlphas[i] == alpha)
            {
                return GlowBrushes[i][(int)level];
            }
        }

        return GetLevelBrushWithAlpha(level, alpha);
    }
}

public enum NoiseDistributionLevel
{
    Quiet = 0,
    Normal = 1,
    Noisy = 2,
    Extreme = 3
}
