using Avalonia;
using LanMountainDesktop.Views.Components;
using Xunit;

namespace LanMountainDesktop.Tests;

public sealed class WhiteboardViewportHelperTests
{
    [Fact]
    public void ZoomAt_WithCenterAnchor_KeepsAnchorLogicalPointStable()
    {
        var viewportSize = new Size(200, 100);
        var canvasSize = new Size(400, 200);
        var state = new WhiteboardViewportState(1d, default);
        var anchor = new Point(100, 50);
        var before = WhiteboardViewportHelper.ToLogicalPoint(state, anchor);

        var zoomed = WhiteboardViewportHelper.ZoomAt(state, 2d, anchor, viewportSize, canvasSize);
        var after = WhiteboardViewportHelper.ToLogicalPoint(zoomed, anchor);

        Assert.Equal(before.X, after.X, precision: 3);
        Assert.Equal(before.Y, after.Y, precision: 3);
    }

    [Fact]
    public void PanBy_ClampsToScaledCanvasBounds()
    {
        var viewportSize = new Size(100, 100);
        var canvasSize = new Size(200, 200);
        var state = new WhiteboardViewportState(2d, default);

        var positive = WhiteboardViewportHelper.PanBy(state, new Vector(500, 500), viewportSize, canvasSize);
        var negative = WhiteboardViewportHelper.PanBy(state, new Vector(-500, -500), viewportSize, canvasSize);

        Assert.Equal(0d, positive.Offset.X, precision: 3);
        Assert.Equal(0d, positive.Offset.Y, precision: 3);
        Assert.Equal(-300d, negative.Offset.X, precision: 3);
        Assert.Equal(-300d, negative.Offset.Y, precision: 3);
    }

    [Fact]
    public void Clamp_WhenCanvasIsSmallerThanViewport_CentersCanvas()
    {
        var state = new WhiteboardViewportState(1d, new Vector(-40, -40));

        var clamped = WhiteboardViewportHelper.Clamp(
            state,
            new Size(300, 300),
            new Size(100, 100));

        Assert.Equal(100d, clamped.Offset.X, precision: 3);
        Assert.Equal(100d, clamped.Offset.Y, precision: 3);
    }

    [Fact]
    public void Clamp_AfterViewportResize_KeepsOffsetInsideBounds()
    {
        var state = new WhiteboardViewportState(2d, new Vector(-220, -220));

        var clamped = WhiteboardViewportHelper.Clamp(
            state,
            new Size(300, 300),
            new Size(200, 200));

        Assert.Equal(-100d, clamped.Offset.X, precision: 3);
        Assert.Equal(-100d, clamped.Offset.Y, precision: 3);
    }
}
