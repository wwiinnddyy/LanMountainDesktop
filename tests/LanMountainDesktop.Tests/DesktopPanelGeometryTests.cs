using Avalonia;
using LanMountainDesktop.DesktopEditing;
using Xunit;

namespace LanMountainDesktop.Tests;

public sealed class DesktopPanelGeometryTests
{
    [Fact]
    public void MinimumConfiguration_ReservesThreeRowsAndFourColumns()
    {
        var metrics = DesktopPanelMetrics.CalculateGrid(
            DesktopPanelMetrics.MinCellSize,
            DesktopPanelMetrics.MinColumns,
            DesktopPanelMetrics.MinRows);

        Assert.Equal(4, metrics.ColumnCount);
        Assert.Equal(3, metrics.RowCount);
        Assert.Equal(DesktopPanelMetrics.MinCellSize, metrics.CellSize, 3);
        Assert.True(metrics.WindowWidth > 0d);
        Assert.True(metrics.WindowHeight > 0d);
    }

    [Fact]
    public void PanelSizeGrowsAndShrinksWithCellSize()
    {
        var small = DesktopPanelMetrics.CalculateGrid(48d, 8, 3);
        var large = DesktopPanelMetrics.CalculateGrid(96d, 8, 3);

        Assert.True(large.WindowWidth > small.WindowWidth);
        Assert.True(large.WindowHeight > small.WindowHeight);
        Assert.Equal(8, large.ColumnCount);
        Assert.Equal(3, large.RowCount);
    }

    [Fact]
    public void Fit_KeepsRequestedGridWhenItAlreadyFits()
    {
        var metrics = DesktopPanelMetrics.Fit(72d, 8, 3, 1920d, 1040d);

        Assert.Equal(8, metrics.ColumnCount);
        Assert.Equal(3, metrics.RowCount);
        Assert.Equal(72d, metrics.CellSize, 3);
    }

    [Fact]
    public void Fit_ShrinksCellsBeforeDroppingRowsOrColumns()
    {
        var metrics = DesktopPanelMetrics.Fit(120d, 10, 6, 800d, 320d);

        Assert.True(metrics.WindowWidth <= 800d);
        Assert.True(metrics.WindowHeight <= 320d);
        Assert.Equal(10, metrics.ColumnCount);
        Assert.Equal(6, metrics.RowCount);
        Assert.True(metrics.CellSize < 120d);
        Assert.True(metrics.CellSize >= DesktopPanelMetrics.MinCellSize);
    }

    [Fact]
    public void Fit_ReducesGridOnlyAfterCellsReachMinimum()
    {
        var metrics = DesktopPanelMetrics.Fit(120d, 20, 12, 260d, 220d);

        Assert.True(metrics.WindowWidth <= 260d);
        Assert.True(metrics.WindowHeight <= 220d);
        Assert.True(metrics.ColumnCount < 20);
        Assert.True(metrics.RowCount < 12);
        Assert.True(metrics.ColumnCount >= DesktopPanelMetrics.MinColumns);
        Assert.True(metrics.RowCount >= DesktopPanelMetrics.MinRows);
        Assert.Equal(DesktopPanelMetrics.MinCellSize, metrics.CellSize, 3);
    }

    [Fact]
    public void MinimumPanelFitsOnASmallWorkArea()
    {
        var metrics = DesktopPanelMetrics.Fit(
            DesktopPanelMetrics.MinCellSize,
            DesktopPanelMetrics.MinColumns,
            DesktopPanelMetrics.MinRows,
            260d,
            220d);

        Assert.Equal(DesktopPanelMetrics.MinColumns, metrics.ColumnCount);
        Assert.Equal(DesktopPanelMetrics.MinRows, metrics.RowCount);
        Assert.True(metrics.WindowWidth <= 260d);
        Assert.True(metrics.WindowHeight <= 220d);
    }

    [Fact]
    public void BottomDock_CentersHorizontallyAndSitsAboveTheBottomEdge()
    {
        var position = DesktopPanelMetrics.CalculateBottomDockPosition(
            new PixelRect(0, 0, 1920, 1040),
            1d,
            new Size(600d, 200d));

        Assert.Equal(660, position.X);
        Assert.Equal(828, position.Y);
    }

    [Fact]
    public void BottomDock_ScalesSizeMarginAndCenterForHighDpi()
    {
        var position = DesktopPanelMetrics.CalculateBottomDockPosition(
            new PixelRect(0, 0, 1920, 1040),
            2d,
            new Size(600d, 200d));

        Assert.Equal(360, position.X);
        Assert.Equal(616, position.Y);
    }

    [Fact]
    public void BottomDock_UsesTheTargetMonitorOrigin()
    {
        var position = DesktopPanelMetrics.CalculateBottomDockPosition(
            new PixelRect(-1920, 0, 1920, 1040),
            1d,
            new Size(600d, 200d));

        Assert.Equal(-1260, position.X);
        Assert.Equal(828, position.Y);
    }

    [Fact]
    public void Normalization_ClampsEveryDimensionToItsLegalRange()
    {
        Assert.Equal(DesktopPanelMetrics.MinCellSize, DesktopPanelMetrics.NormalizeCellSize(1d));
        Assert.Equal(DesktopPanelMetrics.MaxCellSize, DesktopPanelMetrics.NormalizeCellSize(10000d));
        Assert.Equal(DesktopPanelMetrics.DefaultCellSize, DesktopPanelMetrics.NormalizeCellSize(double.NaN));
        Assert.Equal(DesktopPanelMetrics.MinColumns, DesktopPanelMetrics.NormalizeColumns(1));
        Assert.Equal(DesktopPanelMetrics.MaxColumns, DesktopPanelMetrics.NormalizeColumns(1000));
        Assert.Equal(DesktopPanelMetrics.MinRows, DesktopPanelMetrics.NormalizeRows(1));
        Assert.Equal(DesktopPanelMetrics.MaxRows, DesktopPanelMetrics.NormalizeRows(1000));
    }
}
