using LanMountainDesktop.Services;
using Xunit;

namespace LanMountainDesktop.Tests;

public sealed class DesktopGridLayoutServiceTests
{
    private readonly DesktopGridLayoutService _service = new();

    [Theory]
    [InlineData("Compact", "Compact")]
    [InlineData("compact", "Compact")]
    [InlineData("COMPACT", "Compact")]
    [InlineData("Relaxed", "Relaxed")]
    [InlineData("relaxed", "Relaxed")]
    [InlineData(null, "Relaxed")]
    [InlineData("", "Relaxed")]
    [InlineData("unknown", "Relaxed")]
    public void NormalizeSpacingPreset_MapsToKnownPresets(string? input, string expected)
    {
        Assert.Equal(expected, _service.NormalizeSpacingPreset(input));
    }

    [Theory]
    [InlineData("Compact", 0.06)]
    [InlineData("compact", 0.06)]
    [InlineData("Relaxed", 0.12)]
    [InlineData(null, 0.12)]
    [InlineData("unknown", 0.12)]
    public void ResolveGapRatio_ReturnsExpectedRatio(string? preset, double expected)
    {
        Assert.Equal(expected, _service.ResolveGapRatio(preset), precision: 6);
    }

    [Fact]
    public void CalculateGridMetrics_LandscapeOrientation_ComputesCorrectMetrics()
    {
        var metrics = _service.CalculateGridMetrics(
            hostWidth: 1920, hostHeight: 1080,
            shortSideCells: 6, gapRatio: 0.12, edgeInsetPx: 40);

        Assert.True(metrics.ColumnCount > 0);
        Assert.Equal(6, metrics.RowCount);
        Assert.True(metrics.CellSize > 0);
        Assert.True(metrics.GapPx > 0);
        Assert.Equal(40, metrics.EdgeInsetPx);
        Assert.True(metrics.GridWidthPx > 0);
        Assert.True(metrics.GridHeightPx > 0);
    }

    [Fact]
    public void CalculateGridMetrics_PortraitOrientation_SwapsRowsAndColumns()
    {
        var metrics = _service.CalculateGridMetrics(
            hostWidth: 1080, hostHeight: 1920,
            shortSideCells: 6, gapRatio: 0.12, edgeInsetPx: 40);

        Assert.Equal(6, metrics.ColumnCount);
        Assert.True(metrics.RowCount > 0);
        Assert.True(metrics.CellSize > 0);
    }

    [Fact]
    public void CalculateGridMetrics_SquareHost_UsesShortSideForColumns()
    {
        var metrics = _service.CalculateGridMetrics(
            hostWidth: 1000, hostHeight: 1000,
            shortSideCells: 5, gapRatio: 0.1, edgeInsetPx: 20);

        Assert.Equal(5, metrics.ColumnCount);
        Assert.True(metrics.RowCount >= 5);
    }

    [Theory]
    [InlineData(0, 1080)]
    [InlineData(1920, 0)]
    [InlineData(-1, 1080)]
    [InlineData(1920, -1)]
    [InlineData(1, 1080)]
    [InlineData(1920, 1)]
    public void CalculateGridMetrics_DegenerateDimensions_ReturnsDefault(double width, double height)
    {
        var metrics = _service.CalculateGridMetrics(width, height, 4, 0.12, 20);

        Assert.Equal(default, metrics);
    }

    [Fact]
    public void CalculateGridMetrics_ZeroGapRatio_ProducesZeroGap()
    {
        var metrics = _service.CalculateGridMetrics(
            hostWidth: 1920, hostHeight: 1080,
            shortSideCells: 4, gapRatio: 0, edgeInsetPx: 0);

        Assert.Equal(0, metrics.GapPx);
        Assert.True(metrics.CellSize > 0);
    }

    [Fact]
    public void CalculateGridMetrics_NegativeGapRatio_ClampedToZero()
    {
        var metrics = _service.CalculateGridMetrics(
            hostWidth: 1920, hostHeight: 1080,
            shortSideCells: 4, gapRatio: -0.5, edgeInsetPx: 0);

        Assert.Equal(0, metrics.GapPx);
    }

    [Fact]
    public void CalculateEdgeInset_DegenerateHost_ReturnsZero()
    {
        Assert.Equal(0, _service.CalculateEdgeInset(0, 1080, 4, 10));
        Assert.Equal(0, _service.CalculateEdgeInset(1920, 0, 4, 10));
        Assert.Equal(0, _service.CalculateEdgeInset(1, 1080, 4, 10));
    }

    [Fact]
    public void CalculateEdgeInset_ClampsPercentTo0And30()
    {
        var at30 = _service.CalculateEdgeInset(1920, 1080, 6, 30);
        var overClamped = _service.CalculateEdgeInset(1920, 1080, 6, 50);
        var underClamped = _service.CalculateEdgeInset(1920, 1080, 6, -10);

        Assert.True(at30 > 0);
        Assert.Equal(at30, overClamped);
        Assert.Equal(0, underClamped);
    }

    [Fact]
    public void CalculateEdgeInset_ResultIsClampedToMax80()
    {
        var result = _service.CalculateEdgeInset(10000, 10000, 1, 30);
        Assert.True(result <= 80);
    }

    [Fact]
    public void DesktopGridMetrics_Pitch_EqualsCellSizePlusGap()
    {
        var metrics = _service.CalculateGridMetrics(
            hostWidth: 1920, hostHeight: 1080,
            shortSideCells: 4, gapRatio: 0.12, edgeInsetPx: 20);

        Assert.Equal(metrics.CellSize + metrics.GapPx, metrics.Pitch, precision: 6);
    }

    [Fact]
    public void CalculateGridMetrics_GridFitsWithinAvailableArea()
    {
        const double hostWidth = 1920;
        const double hostHeight = 1080;
        const double inset = 40;

        var metrics = _service.CalculateGridMetrics(
            hostWidth, hostHeight, shortSideCells: 6, gapRatio: 0.12, edgeInsetPx: inset);

        var availableWidth = hostWidth - inset * 2;
        var availableHeight = hostHeight - inset * 2;

        Assert.True(metrics.GridWidthPx <= availableWidth + 1,
            $"Grid width {metrics.GridWidthPx} should fit within available width {availableWidth}");
        Assert.True(metrics.GridHeightPx <= availableHeight + 1,
            $"Grid height {metrics.GridHeightPx} should fit within available height {availableHeight}");
    }
}
