using DotNetCampus.Inking.Primitive;
using LanMountainDesktop.Views.Components;
using Xunit;

namespace LanMountainDesktop.Tests;

public sealed class WhiteboardStrokePathBuilderTests
{
    [Fact]
    public void BuildPath_WithEmptyPointList_ReturnsEmptyPath()
    {
        using var path = WhiteboardStrokePathBuilder.BuildPath(Array.Empty<InkStylusPoint>(), inkThickness: 3d);

        Assert.True(path.IsEmpty);
    }

    [Fact]
    public void BuildPath_WithSinglePoint_CreatesVisibleStroke()
    {
        using var path = WhiteboardStrokePathBuilder.BuildPath(
            [CreatePoint(24, 32)],
            inkThickness: 6d);

        Assert.False(path.IsEmpty);
        Assert.True(path.Bounds.Width >= 5.5f);
        Assert.True(path.Bounds.Height >= 5.5f);
    }

    [Fact]
    public void BuildPath_WithMultiplePoints_CreatesFilledStroke()
    {
        using var path = WhiteboardStrokePathBuilder.BuildPath(
            [
                CreatePoint(10, 10),
                CreatePoint(30, 18),
                CreatePoint(52, 14)
            ],
            inkThickness: 4d);

        Assert.False(path.IsEmpty);
        Assert.True(path.Bounds.Width > 40f);
        Assert.True(path.Bounds.Height > 4f);
    }

    [Fact]
    public void BuildPath_WithThickerStroke_ExpandsStrokeBounds()
    {
        var points = new[]
        {
            CreatePoint(10, 10),
            CreatePoint(80, 10)
        };

        using var thinPath = WhiteboardStrokePathBuilder.BuildPath(points, inkThickness: 1d);
        using var thickPath = WhiteboardStrokePathBuilder.BuildPath(points, inkThickness: 8d);

        Assert.True(thickPath.Bounds.Height > thinPath.Bounds.Height);
    }

    [Fact]
    public void BuildPath_WithNonFinitePoints_UsesRemainingFinitePoints()
    {
        using var path = WhiteboardStrokePathBuilder.BuildPath(
            [
                CreatePoint(double.NaN, 10),
                CreatePoint(20, 20)
            ],
            inkThickness: 4d);

        Assert.False(path.IsEmpty);
    }

    private static InkStylusPoint CreatePoint(double x, double y)
    {
        return new InkStylusPoint(x, y, pressure: 1f);
    }
}
