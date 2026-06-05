using Avalonia;
using LanMountainDesktop.DesktopEditing;
using LanMountainDesktop.Models;
using Xunit;

namespace LanMountainDesktop.Tests;

public sealed class DesktopPlacementMathTests
{
    [Fact]
    public void ComputeDragStartThreshold_UsesFloorAndCellScale()
    {
        Assert.Equal(10d, DesktopPlacementMath.ComputeDragStartThreshold(24));
        Assert.Equal(14.4d, DesktopPlacementMath.ComputeDragStartThreshold(80), 3);
    }

    [Fact]
    public void HasExceededThreshold_OnlyReturnsTrueAfterEnoughMovement()
    {
        var start = new Point(20, 20);

        Assert.False(DesktopPlacementMath.HasExceededThreshold(start, new Point(27, 25), 10));
        Assert.True(DesktopPlacementMath.HasExceededThreshold(start, new Point(31, 20), 10));
    }

    [Fact]
    public void OcclusionHelpers_DetectPointAndRectOverlap()
    {
        var libraryBounds = new Rect(100, 100, 200, 160);

        Assert.True(DesktopPlacementMath.IsOccludedByComponentLibrary(new Point(120, 150), libraryBounds));
        Assert.False(DesktopPlacementMath.IsOccludedByComponentLibrary(new Point(80, 90), libraryBounds));
        Assert.True(DesktopPlacementMath.IsOccludedByComponentLibrary(new Rect(250, 120, 120, 80), libraryBounds));
        Assert.False(DesktopPlacementMath.IsOccludedByComponentLibrary(new Rect(10, 10, 40, 40), libraryBounds));
    }

    [Fact]
    public void TryGetSnappedCell_ClampsInsideGridBounds()
    {
        var grid = new DesktopGridGeometry(
            Origin: default,
            CellSize: 80,
            CellGap: 8,
            ColumnCount: 4,
            RowCount: 5);

        var result = DesktopPlacementMath.TryGetSnappedCell(
            grid,
            pointerInViewport: new Point(490, 520),
            pointerOffset: new Point(10, 10),
            widthCells: 2,
            heightCells: 3,
            out var column,
            out var row);

        Assert.True(result);
        Assert.Equal(2, column);
        Assert.Equal(2, row);
    }

    [Fact]
    public void GetCellRect_MapsCellsToPixelRect()
    {
        var grid = new DesktopGridGeometry(
            Origin: new Point(12, 24),
            CellSize: 80,
            CellGap: 8,
            ColumnCount: 6,
            RowCount: 8);

        var rect = DesktopPlacementMath.GetCellRect(grid, column: 2, row: 3, widthCells: 2, heightCells: 3);

        Assert.Equal(188, rect.X, 3);
        Assert.Equal(288, rect.Y, 3);
        Assert.Equal(168, rect.Width, 3);
        Assert.Equal(256, rect.Height, 3);
    }

    [Fact]
    public void Session_DoesNotCommitWhilePointerIsStillInsideLibrary()
    {
        var session = DesktopEditSession.CreatePendingNew(
            componentId: "demo",
            pageIndex: 0,
            widthCells: 2,
            heightCells: 2,
            startPointerInViewport: new Point(80, 80),
            pointerOffsetInViewport: new Point(60, 60),
            componentLibraryBounds: new Rect(0, 0, 220, 300));

        session = session.WithCurrentPointer(new Point(130, 150));

        Assert.True(session.HasExceededThreshold(DesktopPlacementMath.ComputeDragStartThreshold(80)));
        Assert.True(session.IsPointerInsideComponentLibrary());
        Assert.False(session.CanCommit);
    }

    [Fact]
    public void Session_ResizePreviewStillBlocksWhenPointerRemainsInsideLibrary()
    {
        var session = DesktopEditSession.CreateResizingExisting(
            componentId: "demo",
            placementId: "placement-1",
            pageIndex: 0,
            widthCells: 2,
            heightCells: 2,
            startPointerInViewport: new Point(80, 80),
            componentLibraryBounds: new Rect(0, 0, 220, 300))
            .WithCurrentPointer(new Point(130, 150));

        Assert.True(session.IsPointerInsideComponentLibrary());
        Assert.False(session.CanCommit);
    }

    [Fact]
    public void HasCellPositionChanged_DetectsNoOpAndRealMoves()
    {
        Assert.False(DesktopPlacementMath.HasCellPositionChanged(2, 3, 2, 3));
        Assert.True(DesktopPlacementMath.HasCellPositionChanged(2, 3, 2, 4));
    }

    [Fact]
    public void HasCellSpanChanged_DetectsNoOpAndRealResizes()
    {
        Assert.False(DesktopPlacementMath.HasCellSpanChanged(2, 3, 2, 3));
        Assert.True(DesktopPlacementMath.HasCellSpanChanged(2, 3, 3, 3));
    }

    [Fact]
    public void CanCommitPlacement_BlocksWhenPlacementIsOccludedByLibrary()
    {
        var placementRect = new Rect(160, 110, 180, 140);
        var occludingLibraryBounds = new Rect(120, 80, 240, 220);
        var distantLibraryBounds = new Rect(420, 420, 80, 80);

        Assert.False(DesktopPlacementMath.CanCommitPlacement(placementRect, occludingLibraryBounds));
        Assert.True(DesktopPlacementMath.CanCommitPlacement(placementRect, distantLibraryBounds));
        Assert.True(DesktopPlacementMath.CanCommitPlacement(placementRect, componentLibraryBounds: null));
    }

    [Fact]
    public void Session_AllowsCommitWhenComponentLibraryBoundsAreCleared()
    {
        var pendingSession = DesktopEditSession.CreatePendingNew(
            componentId: "demo",
            pageIndex: 0,
            widthCells: 2,
            heightCells: 2,
            startPointerInViewport: new Point(80, 80),
            pointerOffsetInViewport: new Point(60, 60),
            componentLibraryBounds: null)
            .WithCurrentPointer(new Point(200, 180));

        Assert.True(pendingSession.HasExceededThreshold(DesktopPlacementMath.ComputeDragStartThreshold(80)));
        Assert.False(pendingSession.IsPointerInsideComponentLibrary());
        Assert.False(pendingSession.IsPreviewOccludedByComponentLibrary(new Rect(100, 100, 40, 40)));
        Assert.False(pendingSession.CanCommit);

        var resizeSession = DesktopEditSession.CreateResizingExisting(
            componentId: "demo",
            placementId: "placement-1",
            pageIndex: 0,
            widthCells: 2,
            heightCells: 2,
            startPointerInViewport: new Point(80, 80),
            componentLibraryBounds: null)
            .WithCurrentPointer(new Point(200, 180))
            .WithTargetCell(row: 2, column: 3);

        Assert.False(resizeSession.IsPointerInsideComponentLibrary());
        Assert.False(resizeSession.IsPreviewOccludedByComponentLibrary(new Rect(100, 100, 40, 40)));
        Assert.True(resizeSession.CanCommit);
    }

    [Fact]
    public void FusedCenteredPlacement_UsesGridCenterAndComponentSpan()
    {
        var grid = new DesktopGridGeometry(
            Origin: new Point(12, 20),
            CellSize: 80,
            CellGap: 8,
            ColumnCount: 8,
            RowCount: 6);

        var placement = FusedDesktopPlacementMath.CreateCenteredPlacement(
            "placement-1",
            "component-1",
            grid,
            widthCells: 4,
            heightCells: 2);

        Assert.Equal(2, placement.GridColumn);
        Assert.Equal(2, placement.GridRow);
        Assert.Equal(4, placement.GridWidthCells);
        Assert.Equal(2, placement.GridHeightCells);
        Assert.Equal(188, placement.X, 3);
        Assert.Equal(196, placement.Y, 3);
        Assert.Equal(344, placement.Width, 3);
        Assert.Equal(168, placement.Height, 3);
    }

    [Fact]
    public void FusedSnapToNearestCell_RoundsAndPersistsGridCoordinates()
    {
        var grid = new DesktopGridGeometry(
            Origin: new Point(10, 10),
            CellSize: 100,
            CellGap: 12,
            ColumnCount: 6,
            RowCount: 5);
        var placement = new FusedDesktopComponentPlacementSnapshot
        {
            PlacementId = "placement-1",
            ComponentId = "component-1",
            Width = 212,
            Height = 100,
            GridWidthCells = 2,
            GridHeightCells = 1
        };

        var snapped = FusedDesktopPlacementMath.SnapToNearestCell(
            placement,
            grid,
            requestedOrigin: new Point(255, 135));

        Assert.Equal(2, snapped.GridColumn);
        Assert.Equal(1, snapped.GridRow);
        Assert.Equal(234, snapped.X, 3);
        Assert.Equal(122, snapped.Y, 3);
        Assert.Equal(212, snapped.Width, 3);
        Assert.Equal(100, snapped.Height, 3);
    }

    [Fact]
    public void FusedSnapToNearestCell_ClampsInsideGridBounds()
    {
        var grid = new DesktopGridGeometry(
            Origin: default,
            CellSize: 80,
            CellGap: 8,
            ColumnCount: 4,
            RowCount: 3);
        var placement = new FusedDesktopComponentPlacementSnapshot
        {
            PlacementId = "placement-1",
            ComponentId = "component-1",
            Width = 168,
            Height = 168,
            GridWidthCells = 2,
            GridHeightCells = 2
        };

        var snapped = FusedDesktopPlacementMath.SnapToNearestCell(
            placement,
            grid,
            requestedOrigin: new Point(900, 600));

        Assert.Equal(2, snapped.GridColumn);
        Assert.Equal(1, snapped.GridRow);
        Assert.Equal(176, snapped.X, 3);
        Assert.Equal(88, snapped.Y, 3);
    }

    [Fact]
    public void FusedSnapToNearestCell_EstimatesMissingSpanFromPixelSize()
    {
        var grid = new DesktopGridGeometry(
            Origin: default,
            CellSize: 80,
            CellGap: 8,
            ColumnCount: 6,
            RowCount: 6);
        var placement = new FusedDesktopComponentPlacementSnapshot
        {
            PlacementId = "placement-1",
            ComponentId = "component-1",
            Width = 168,
            Height = 256
        };

        var snapped = FusedDesktopPlacementMath.SnapToNearestCell(
            placement,
            grid,
            requestedOrigin: new Point(90, 180));

        Assert.Equal(2, snapped.GridWidthCells);
        Assert.Equal(3, snapped.GridHeightCells);
        Assert.Equal(1, snapped.GridColumn);
        Assert.Equal(2, snapped.GridRow);
        Assert.Equal(168, snapped.Width, 3);
        Assert.Equal(256, snapped.Height, 3);
    }
}
