using System;
using Avalonia;

namespace LanMountainDesktop.DesktopEditing;

internal readonly record struct DesktopGridGeometry(
    Point Origin,
    double CellSize,
    double CellGap,
    int ColumnCount,
    int RowCount)
{
    public double Pitch => CellSize + CellGap;

    public bool IsValid =>
        CellSize > 0 &&
        ColumnCount > 0 &&
        RowCount > 0 &&
        Pitch > 0;
}

internal static class DesktopPlacementMath
{
    public static double ComputeDragStartThreshold(double cellSize)
    {
        return Math.Max(10d, Math.Max(0d, cellSize) * 0.18d);
    }

    public static double Distance(Point start, Point end)
    {
        return Math.Sqrt(DistanceSquared(start, end));
    }

    public static double DistanceSquared(Point start, Point end)
    {
        var deltaX = end.X - start.X;
        var deltaY = end.Y - start.Y;
        return deltaX * deltaX + deltaY * deltaY;
    }

    public static bool HasExceededThreshold(Point start, Point end, double threshold)
    {
        if (threshold <= 0)
        {
            return true;
        }

        return DistanceSquared(start, end) >= threshold * threshold;
    }

    public static Point Add(Point left, Point right)
    {
        return new Point(left.X + right.X, left.Y + right.Y);
    }

    public static Point Subtract(Point left, Point right)
    {
        return new Point(left.X - right.X, left.Y - right.Y);
    }

    public static bool ContainsPoint(Rect rect, Point point)
    {
        return rect.Contains(point);
    }

    public static bool Intersects(Rect left, Rect right)
    {
        return left.Intersects(right);
    }

    public static bool HasCellPositionChanged(int originalRow, int originalColumn, int targetRow, int targetColumn)
    {
        return originalRow != targetRow || originalColumn != targetColumn;
    }

    public static bool HasCellSpanChanged(int originalWidthCells, int originalHeightCells, int targetWidthCells, int targetHeightCells)
    {
        return originalWidthCells != targetWidthCells || originalHeightCells != targetHeightCells;
    }

    public static bool IsOccludedByComponentLibrary(Point point, Rect? componentLibraryBounds)
    {
        return componentLibraryBounds.HasValue && ContainsPoint(componentLibraryBounds.Value, point);
    }

    public static bool IsOccludedByComponentLibrary(Rect previewRect, Rect? componentLibraryBounds)
    {
        return componentLibraryBounds.HasValue && Intersects(previewRect, componentLibraryBounds.Value);
    }

    public static bool CanCommitPlacement(Rect placementRect, Rect? componentLibraryBounds)
    {
        return !IsOccludedByComponentLibrary(placementRect, componentLibraryBounds);
    }

    public static Rect GetGridBounds(DesktopGridGeometry grid)
    {
        if (!grid.IsValid)
        {
            return default;
        }

        var width = grid.ColumnCount * grid.CellSize + Math.Max(0, grid.ColumnCount - 1) * grid.CellGap;
        var height = grid.RowCount * grid.CellSize + Math.Max(0, grid.RowCount - 1) * grid.CellGap;
        return new Rect(grid.Origin, new Size(width, height));
    }

    public static Rect GetCellRect(
        DesktopGridGeometry grid,
        int column,
        int row,
        int widthCells = 1,
        int heightCells = 1)
    {
        var safeWidthCells = Math.Max(1, widthCells);
        var safeHeightCells = Math.Max(1, heightCells);
        var safeColumn = Math.Max(0, column);
        var safeRow = Math.Max(0, row);
        var pitch = grid.Pitch;
        var x = grid.Origin.X + safeColumn * pitch;
        var y = grid.Origin.Y + safeRow * pitch;
        var width = safeWidthCells * grid.CellSize + Math.Max(0, safeWidthCells - 1) * grid.CellGap;
        var height = safeHeightCells * grid.CellSize + Math.Max(0, safeHeightCells - 1) * grid.CellGap;
        return new Rect(x, y, width, height);
    }

    public static Rect GetSnappedCellRect(
        DesktopGridGeometry grid,
        Point pointerInViewport,
        Point pointerOffset,
        int widthCells,
        int heightCells)
    {
        return TryGetSnappedCell(grid, pointerInViewport, pointerOffset, widthCells, heightCells, out var column, out var row)
            ? GetCellRect(grid, column, row, widthCells, heightCells)
            : default;
    }

    public static bool TryGetSnappedCell(
        DesktopGridGeometry grid,
        Point pointerInViewport,
        Point pointerOffset,
        int widthCells,
        int heightCells,
        out int column,
        out int row)
    {
        column = 0;
        row = 0;

        if (!grid.IsValid)
        {
            return false;
        }

        var safeWidthCells = Math.Max(1, widthCells);
        var safeHeightCells = Math.Max(1, heightCells);
        var maxColumn = Math.Max(0, grid.ColumnCount - safeWidthCells);
        var maxRow = Math.Max(0, grid.RowCount - safeHeightCells);
        var pitch = grid.Pitch;
        if (pitch <= 0)
        {
            return false;
        }

        var previewOrigin = Subtract(pointerInViewport, pointerOffset);
        var relativeX = previewOrigin.X - grid.Origin.X;
        var relativeY = previewOrigin.Y - grid.Origin.Y;

        column = (int)Math.Floor(relativeX / pitch);
        row = (int)Math.Floor(relativeY / pitch);
        column = Math.Clamp(column, 0, maxColumn);
        row = Math.Clamp(row, 0, maxRow);
        return true;
    }
}
