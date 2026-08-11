using LanMountainDesktop.Views;
using Xunit;

namespace LanMountainDesktop.Tests;

public sealed class FusedDesktopLibraryPreviewLayoutTests
{
    [Fact]
    public void Calculate_PreservesLandscapeComponentRatio()
    {
        var metrics = FusedDesktopLibraryPreviewLayout.Calculate(
            widthCells: 4,
            heightCells: 2,
            stageWidth: 520,
            stageHeight: 320);

        Assert.Equal(4, metrics.WidthCells);
        Assert.Equal(2, metrics.HeightCells);
        Assert.True(metrics.Width > metrics.Height);
        Assert.Equal(2d, metrics.Width / metrics.Height, precision: 3);
    }

    [Fact]
    public void Calculate_PreservesPortraitComponentRatio()
    {
        var metrics = FusedDesktopLibraryPreviewLayout.Calculate(
            widthCells: 2,
            heightCells: 4,
            stageWidth: 520,
            stageHeight: 320);

        Assert.Equal(2, metrics.WidthCells);
        Assert.Equal(4, metrics.HeightCells);
        Assert.True(metrics.Height > metrics.Width);
        Assert.Equal(0.5d, metrics.Width / metrics.Height, precision: 3);
    }

    [Fact]
    public void Calculate_FitsPreviewInsideStageInsets()
    {
        var metrics = FusedDesktopLibraryPreviewLayout.Calculate(
            widthCells: 4,
            heightCells: 4,
            stageWidth: 420,
            stageHeight: 260);

        Assert.Equal(metrics.Width, metrics.Height, precision: 3);
        Assert.True(metrics.Width <= 420);
        Assert.True(metrics.Height <= 260);
        Assert.True(metrics.CellSize > 0);
    }

    [Fact]
    public void Calculate_UsesFallbackStageWhenBoundsAreNotMeasured()
    {
        var metrics = FusedDesktopLibraryPreviewLayout.Calculate(
            widthCells: 4,
            heightCells: 2,
            stageWidth: 0,
            stageHeight: 0);

        Assert.True(metrics.Width > 0);
        Assert.True(metrics.Height > 0);
        Assert.Equal(2d, metrics.Width / metrics.Height, precision: 3);
    }

    [Fact]
    public void Calculate_RespectsMinCellSize()
    {
        // 测试非常小的 stage 尺寸，确保 cellSize 不会小于 MinCellSize
        var metrics = FusedDesktopLibraryPreviewLayout.Calculate(
            widthCells: 10,
            heightCells: 10,
            stageWidth: 50,
            stageHeight: 50);

        Assert.Equal(32d, metrics.CellSize, precision: 3);
        Assert.Equal(320d, metrics.Width, precision: 3);
        Assert.Equal(320d, metrics.Height, precision: 3);
    }
}
