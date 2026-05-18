using System;
using Avalonia;

namespace LanMountainDesktop.Views.Components;

internal readonly record struct WhiteboardViewportState(double Zoom, Vector Offset);

internal static class WhiteboardViewportHelper
{
    public const double DefaultZoom = 1d;
    public const double MinZoom = 0.5d;
    public const double MaxZoom = 4d;

    public static WhiteboardViewportState CreateDefault(Size viewportSize, Size canvasSize)
    {
        return Clamp(new WhiteboardViewportState(DefaultZoom, default), viewportSize, canvasSize);
    }

    public static WhiteboardViewportState Clamp(
        WhiteboardViewportState state,
        Size viewportSize,
        Size canvasSize)
    {
        var zoom = NormalizeZoom(state.Zoom);
        var viewport = NormalizeSize(viewportSize);
        var canvas = NormalizeSize(canvasSize);
        var scaledWidth = canvas.Width * zoom;
        var scaledHeight = canvas.Height * zoom;

        return new WhiteboardViewportState(
            zoom,
            new Vector(
                ClampAxis(state.Offset.X, viewport.Width, scaledWidth),
                ClampAxis(state.Offset.Y, viewport.Height, scaledHeight)));
    }

    public static WhiteboardViewportState PanBy(
        WhiteboardViewportState state,
        Vector delta,
        Size viewportSize,
        Size canvasSize)
    {
        if (!IsFinite(delta.X) || !IsFinite(delta.Y))
        {
            return Clamp(state, viewportSize, canvasSize);
        }

        return Clamp(state with { Offset = state.Offset + delta }, viewportSize, canvasSize);
    }

    public static WhiteboardViewportState ZoomAt(
        WhiteboardViewportState state,
        double targetZoom,
        Point anchorViewportPoint,
        Size viewportSize,
        Size canvasSize)
    {
        return ZoomFromGestureStart(
            state,
            targetZoom,
            anchorViewportPoint,
            anchorViewportPoint,
            viewportSize,
            canvasSize);
    }

    public static WhiteboardViewportState ZoomFromGestureStart(
        WhiteboardViewportState startState,
        double targetZoom,
        Point initialCenter,
        Point currentCenter,
        Size viewportSize,
        Size canvasSize)
    {
        var start = Clamp(startState, viewportSize, canvasSize);
        var zoom = NormalizeZoom(targetZoom);
        var anchor = ToLogicalPoint(start, initialCenter);
        var offset = new Vector(
            currentCenter.X - (anchor.X * zoom),
            currentCenter.Y - (anchor.Y * zoom));

        return Clamp(new WhiteboardViewportState(zoom, offset), viewportSize, canvasSize);
    }

    public static Point ToLogicalPoint(WhiteboardViewportState state, Point viewportPoint)
    {
        var zoom = NormalizeZoom(state.Zoom);
        return new Point(
            (viewportPoint.X - state.Offset.X) / zoom,
            (viewportPoint.Y - state.Offset.Y) / zoom);
    }

    public static WhiteboardViewportState Fit(Size viewportSize, Size canvasSize)
    {
        var viewport = NormalizeSize(viewportSize);
        var canvas = NormalizeSize(canvasSize);
        var fitZoom = Math.Min(viewport.Width / canvas.Width, viewport.Height / canvas.Height);
        return Clamp(new WhiteboardViewportState(fitZoom, default), viewport, canvas);
    }

    public static double NormalizeZoom(double zoom)
    {
        if (!IsFinite(zoom))
        {
            return DefaultZoom;
        }

        return Math.Clamp(zoom, MinZoom, MaxZoom);
    }

    public static Size NormalizeSize(Size size)
    {
        return new Size(
            IsFinite(size.Width) ? Math.Max(1d, size.Width) : 1d,
            IsFinite(size.Height) ? Math.Max(1d, size.Height) : 1d);
    }

    private static double ClampAxis(double offset, double viewportLength, double scaledCanvasLength)
    {
        if (!IsFinite(offset))
        {
            offset = 0d;
        }

        if (scaledCanvasLength <= viewportLength)
        {
            return (viewportLength - scaledCanvasLength) * 0.5d;
        }

        return Math.Clamp(offset, viewportLength - scaledCanvasLength, 0d);
    }

    private static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
