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
    /// <summary>
    /// 把一段像素宽度/高度换算成"占几格"，只有这一处算法：分子先把 <c>CellGap</c> 加上再除以 <c>Pitch</c>，
    /// 取整用 <c>AwayFromZero</c>，最少 1 格；网格不合法（<c>CellSize &lt;= 0</c> 等）一律给 1 格。
    /// </summary>
    /// <remarks>
    /// 收口前 <c>FusedDesktopPlacementMath</c>（桌面摆放快照）与 <c>DesktopWidgetWindow</c>（组件浮窗按请求尺寸
    /// 估格）各有一份逐字相同的 5 行（2026-09-24 由普查尺子量出，共 4 个调用点）。漂开的症状不报错：
    /// 同一个组件"从桌面拖出来的大小"与"浮窗请求回来的大小"会差一格。
    /// "网格不合法给 1 格"是两份抄本一致的兜底，没顺手改成 0 或抛——那会让首帧还没算好几何的时候
    /// 组件直接不占位。
    /// </remarks>
    public static int EstimateCellSpan(double pixelSize, DesktopGridGeometry grid)
    {
        if (!grid.IsValid || grid.CellSize <= 0)
        {
            return 1;
        }

        return Math.Max(1, (int)Math.Round(
            (Math.Max(1, pixelSize) + grid.CellGap) / grid.Pitch,
            MidpointRounding.AwayFromZero));
    }

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
