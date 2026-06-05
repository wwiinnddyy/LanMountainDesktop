using System;
using Avalonia;
using LanMountainDesktop.Models;

namespace LanMountainDesktop.DesktopEditing;

internal static class FusedDesktopPlacementMath
{
    public static FusedDesktopComponentPlacementSnapshot CreateCenteredPlacement(
        string placementId,
        string componentId,
        DesktopGridGeometry grid,
        int widthCells,
        int heightCells)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(placementId);
        ArgumentException.ThrowIfNullOrWhiteSpace(componentId);

        var safeWidthCells = Math.Max(1, widthCells);
        var safeHeightCells = Math.Max(1, heightCells);
        var column = Math.Clamp(
            (grid.ColumnCount - safeWidthCells) / 2,
            0,
            Math.Max(0, grid.ColumnCount - safeWidthCells));
        var row = Math.Clamp(
            (grid.RowCount - safeHeightCells) / 2,
            0,
            Math.Max(0, grid.RowCount - safeHeightCells));
        var rect = DesktopPlacementMath.GetCellRect(grid, column, row, safeWidthCells, safeHeightCells);

        return new FusedDesktopComponentPlacementSnapshot
        {
            PlacementId = placementId,
            ComponentId = componentId,
            X = rect.X,
            Y = rect.Y,
            Width = rect.Width,
            Height = rect.Height,
            GridColumn = column,
            GridRow = row,
            GridWidthCells = safeWidthCells,
            GridHeightCells = safeHeightCells
        };
    }

    public static FusedDesktopComponentPlacementSnapshot SnapToNearestCell(
        FusedDesktopComponentPlacementSnapshot placement,
        DesktopGridGeometry grid,
        Point requestedOrigin)
    {
        ArgumentNullException.ThrowIfNull(placement);

        var widthCells = Math.Max(1, placement.GridWidthCells ?? EstimateCellSpan(placement.Width, grid));
        var heightCells = Math.Max(1, placement.GridHeightCells ?? EstimateCellSpan(placement.Height, grid));
        var maxColumn = Math.Max(0, grid.ColumnCount - widthCells);
        var maxRow = Math.Max(0, grid.RowCount - heightCells);
        var pitch = grid.Pitch;

        if (!grid.IsValid || pitch <= 0)
        {
            return placement.Clone();
        }

        var column = Math.Clamp(
            (int)Math.Round((requestedOrigin.X - grid.Origin.X) / pitch, MidpointRounding.AwayFromZero),
            0,
            maxColumn);
        var row = Math.Clamp(
            (int)Math.Round((requestedOrigin.Y - grid.Origin.Y) / pitch, MidpointRounding.AwayFromZero),
            0,
            maxRow);
        var rect = DesktopPlacementMath.GetCellRect(grid, column, row, widthCells, heightCells);

        var snapped = placement.Clone();
        snapped.X = rect.X;
        snapped.Y = rect.Y;
        snapped.Width = rect.Width;
        snapped.Height = rect.Height;
        snapped.GridColumn = column;
        snapped.GridRow = row;
        snapped.GridWidthCells = widthCells;
        snapped.GridHeightCells = heightCells;
        return snapped;
    }

    private static int EstimateCellSpan(double pixelSize, DesktopGridGeometry grid)
    {
        if (!grid.IsValid || grid.CellSize <= 0)
        {
            return 1;
        }

        return Math.Max(1, (int)Math.Round(
            (Math.Max(1, pixelSize) + grid.CellGap) / grid.Pitch,
            MidpointRounding.AwayFromZero));
    }
}
