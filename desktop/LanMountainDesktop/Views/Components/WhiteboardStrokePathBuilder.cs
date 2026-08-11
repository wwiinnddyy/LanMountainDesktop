using System;
using System.Collections.Generic;
using DotNetCampus.Inking.Primitive;
using SkiaSharp;

namespace LanMountainDesktop.Views.Components;

internal static class WhiteboardStrokePathBuilder
{
    private const float MinimumInkThickness = 0.5f;
    private const float PointOverlapTolerance = 0.01f;

    public static SKPath BuildPath(IReadOnlyList<InkStylusPoint> pointList, double inkThickness)
    {
        var strokePath = new SKPath();
        if (pointList.Count == 0)
        {
            return strokePath;
        }

        var points = CollectFinitePoints(pointList);
        if (points.Count == 0)
        {
            return strokePath;
        }

        var normalizedThickness = NormalizeInkThickness(inkThickness);
        if (points.Count == 1 || AreAllPointsOverlapping(points))
        {
            AddSinglePointStroke(strokePath, points[0], normalizedThickness);
            return strokePath;
        }

        using var centerPath = BuildCenterPath(points);
        using var strokePaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = normalizedThickness,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round
        };

        if (!strokePaint.GetFillPath(centerPath, strokePath) || strokePath.IsEmpty)
        {
            strokePath.Reset();
            AddPointFallbackStroke(strokePath, points, normalizedThickness);
        }

        return strokePath;
    }

    private static List<SKPoint> CollectFinitePoints(IReadOnlyList<InkStylusPoint> pointList)
    {
        var points = new List<SKPoint>(pointList.Count);
        foreach (var point in pointList)
        {
            if (!double.IsFinite(point.X) || !double.IsFinite(point.Y))
            {
                continue;
            }

            points.Add(new SKPoint((float)point.X, (float)point.Y));
        }

        return points;
    }

    private static SKPath BuildCenterPath(IReadOnlyList<SKPoint> points)
    {
        var centerPath = new SKPath();
        centerPath.MoveTo(points[0].X, points[0].Y);

        if (points.Count == 2)
        {
            centerPath.LineTo(points[1].X, points[1].Y);
            return centerPath;
        }

        for (var i = 1; i < points.Count - 1; i++)
        {
            var current = points[i];
            var next = points[i + 1];
            var midpoint = new SKPoint(
                (current.X + next.X) * 0.5f,
                (current.Y + next.Y) * 0.5f);
            centerPath.QuadTo(current.X, current.Y, midpoint.X, midpoint.Y);
        }

        var lastPoint = points[^1];
        centerPath.LineTo(lastPoint.X, lastPoint.Y);
        return centerPath;
    }

    private static void AddPointFallbackStroke(SKPath path, IReadOnlyList<SKPoint> points, float inkThickness)
    {
        foreach (var point in points)
        {
            AddSinglePointStroke(path, point, inkThickness);
        }
    }

    private static void AddSinglePointStroke(SKPath path, SKPoint point, float inkThickness)
    {
        path.AddCircle(point.X, point.Y, inkThickness * 0.5f);
    }

    private static bool AreAllPointsOverlapping(IReadOnlyList<SKPoint> points)
    {
        var firstPoint = points[0];
        for (var i = 1; i < points.Count; i++)
        {
            if (Math.Abs(points[i].X - firstPoint.X) > PointOverlapTolerance ||
                Math.Abs(points[i].Y - firstPoint.Y) > PointOverlapTolerance)
            {
                return false;
            }
        }

        return true;
    }

    private static float NormalizeInkThickness(double inkThickness)
    {
        if (!double.IsFinite(inkThickness))
        {
            return MinimumInkThickness;
        }

        return Math.Max(MinimumInkThickness, (float)inkThickness);
    }
}
