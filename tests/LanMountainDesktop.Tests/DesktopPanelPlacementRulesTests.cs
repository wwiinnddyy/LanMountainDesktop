using LanMountainDesktop.DesktopEditing;
using LanMountainDesktop.Models;
using Xunit;

namespace LanMountainDesktop.Tests;

public sealed class DesktopPanelPlacementRulesTests
{
    [Fact]
    public void TryFindFreeSlot_ReturnsTheFirstRowMajorGap()
    {
        var placements = new[]
        {
            Create("a", column: 0, row: 0, widthCells: 2, heightCells: 1),
            Create("b", column: 2, row: 0, widthCells: 2, heightCells: 1)
        };

        var found = DesktopPanelPlacementRules.TryFindFreeSlot(
            placements,
            columnCount: 4,
            rowCount: 3,
            widthCells: 2,
            heightCells: 1,
            out var column,
            out var row);

        Assert.True(found);
        Assert.Equal(0, column);
        Assert.Equal(1, row);
    }

    [Fact]
    public void TryFindFreeSlot_ReturnsFalseWhenTheGridIsFull()
    {
        var placements = new[] { Create("full", column: 0, row: 0, widthCells: 4, heightCells: 3) };

        Assert.False(DesktopPanelPlacementRules.TryFindFreeSlot(
            placements,
            columnCount: 4,
            rowCount: 3,
            widthCells: 1,
            heightCells: 1,
            out _,
            out _));
    }

    [Fact]
    public void TryFindFreeSlot_RejectsASpanLargerThanTheGrid()
    {
        Assert.False(DesktopPanelPlacementRules.TryFindFreeSlot(
            [],
            columnCount: 4,
            rowCount: 3,
            widthCells: 5,
            heightCells: 1,
            out _,
            out _));
    }

    [Fact]
    public void CanApplyPlacement_RejectsOverlapButIgnoresItself()
    {
        var existing = Create("a", column: 0, row: 0, widthCells: 2, heightCells: 2);
        var overlapping = Create("b", column: 1, row: 1, widthCells: 2, heightCells: 2);
        var adjacent = Create("c", column: 2, row: 0, widthCells: 2, heightCells: 2);
        var movedSelf = Create("a", column: 1, row: 1, widthCells: 2, heightCells: 2);

        Assert.False(DesktopPanelPlacementRules.CanApplyPlacement([existing], overlapping, 4, 3));
        Assert.True(DesktopPanelPlacementRules.CanApplyPlacement([existing], adjacent, 4, 3));
        Assert.True(DesktopPanelPlacementRules.CanApplyPlacement([existing], movedSelf, 4, 3));
    }

    [Fact]
    public void CanApplyPlacement_RejectsOutOfBoundsSpans()
    {
        Assert.False(DesktopPanelPlacementRules.CanApplyPlacement(
            [],
            Create("a", column: 3, row: 0, widthCells: 2, heightCells: 1),
            columnCount: 4,
            rowCount: 3));

        Assert.False(DesktopPanelPlacementRules.CanApplyPlacement(
            [],
            Create("a", column: 0, row: 2, widthCells: 1, heightCells: 2),
            columnCount: 4,
            rowCount: 3));
    }

    [Fact]
    public void FitToGrid_ClampsSpanAndPositionIntoTinyGrids()
    {
        var placement = Create("a", column: 9, row: 9, widthCells: 5, heightCells: 5);

        DesktopPanelPlacementRules.FitToGrid([placement], columnCount: 3, rowCount: 2);

        Assert.Equal(3, placement.WidthCells);
        Assert.Equal(2, placement.HeightCells);
        Assert.Equal(0, placement.Column);
        Assert.Equal(0, placement.Row);
    }

    [Fact]
    public void FitToGrid_RespectsTheComponentMinimumSpan()
    {
        var placement = Create("a", column: 0, row: 0, widthCells: 1, heightCells: 1);

        DesktopPanelPlacementRules.FitToGrid(
            [placement],
            columnCount: 4,
            rowCount: 3,
            _ => (2, 2));

        Assert.Equal(2, placement.WidthCells);
        Assert.Equal(2, placement.HeightCells);
    }

    [Fact]
    public void FitToGrid_MovesACollisionToTheFirstFreeSlot()
    {
        var first = Create("a", column: 0, row: 0, widthCells: 4, heightCells: 2);
        var second = Create("b", column: 2, row: 0, widthCells: 4, heightCells: 2);

        DesktopPanelPlacementRules.FitToGrid([first, second], columnCount: 4, rowCount: 4);

        Assert.Equal(0, first.Column);
        Assert.Equal(0, first.Row);
        Assert.Equal(0, second.Column);
        Assert.Equal(2, second.Row);
        Assert.False(DesktopPanelPlacementRules.Overlaps(
            [first],
            second.PlacementId,
            second.Column,
            second.Row,
            second.WidthCells,
            second.HeightCells));
    }

    private static DesktopPanelPlacementSnapshot Create(
        string placementId,
        int column,
        int row,
        int widthCells,
        int heightCells)
    {
        return new DesktopPanelPlacementSnapshot
        {
            PlacementId = placementId,
            ComponentId = $"component.{placementId}",
            Column = column,
            Row = row,
            WidthCells = widthCells,
            HeightCells = heightCells
        };
    }
}
