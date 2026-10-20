using System;
using Avalonia;

namespace LanMountainDesktop.DesktopEditing;

/// <summary>
/// 桌面板网格的像素几何。窗口尺寸由格子反推：内边距 + 每格边长 + 格间距，
/// 因此"格子多大"是唯一需要配置的量，窗口不会出现格子和边框对不上的状态。
/// </summary>
internal readonly record struct DesktopPanelGridMetrics(
    int ColumnCount,
    int RowCount,
    double CellSize,
    double CellGap,
    double Padding,
    double GridWidth,
    double GridHeight,
    double WindowWidth,
    double WindowHeight)
{
    public double Pitch => CellSize + CellGap;

    public bool IsValid =>
        ColumnCount > 0 &&
        RowCount > 0 &&
        CellSize > 0 &&
        CellGap >= 0 &&
        Pitch > 0;

    public Size WindowSize => new(WindowWidth, WindowHeight);
}

internal static class DesktopPanelMetrics
{
    /// <summary>桌面板最小 4 列 × 3 行，即需求里的 3×4 格；再小就连 1×1 组件都排不开。</summary>
    public const int MinColumns = 4;

    public const int MinRows = 3;

    public const int MaxColumns = 20;

    public const int MaxRows = 12;

    public const double MinCellSize = 36d;

    public const double MaxCellSize = 160d;

    public const double DefaultCellSize = 72d;

    public const int DefaultColumns = 8;

    public const int DefaultRows = 3;

    /// <summary>格间距固定按格边长的比例走，格子放大时视觉留白同步放大。</summary>
    public const double GapRatio = 0.12d;

    /// <summary>面板内边距（逻辑像素）。</summary>
    public const double Padding = 16d;

    /// <summary>面板底边与工作区底边之间的留白（逻辑像素）。</summary>
    public const double BottomMargin = 12d;

    public static int NormalizeColumns(int columns)
    {
        return Math.Clamp(columns, MinColumns, MaxColumns);
    }

    public static int NormalizeRows(int rows)
    {
        return Math.Clamp(rows, MinRows, MaxRows);
    }

    public static double NormalizeCellSize(double cellSize)
    {
        return double.IsFinite(cellSize)
            ? Math.Clamp(cellSize, MinCellSize, MaxCellSize)
            : DefaultCellSize;
    }

    public static DesktopPanelGridMetrics CalculateGrid(double cellSize, int columns, int rows)
    {
        var safeColumns = NormalizeColumns(columns);
        var safeRows = NormalizeRows(rows);
        var safeCell = NormalizeCellSize(cellSize);
        var gap = safeCell * GapRatio;
        var gridWidth = safeColumns * safeCell + Math.Max(0, safeColumns - 1) * gap;
        var gridHeight = safeRows * safeCell + Math.Max(0, safeRows - 1) * gap;
        return new DesktopPanelGridMetrics(
            safeColumns,
            safeRows,
            safeCell,
            gap,
            Padding,
            gridWidth,
            gridHeight,
            gridWidth + Padding * 2,
            gridHeight + Padding * 2);
    }

    /// <summary>
    /// 把用户配置的格子和尺寸压进当前工作区：先缩格子（不低于最小格子），
    /// 仍放不下才减少行列（不低于 4×3）。返回值就是窗口最终尺寸的唯一来源。
    /// </summary>
    public static DesktopPanelGridMetrics Fit(
        double requestedCellSize,
        int requestedColumns,
        int requestedRows,
        double availableWidth,
        double availableHeight)
    {
        var columns = NormalizeColumns(requestedColumns);
        var rows = NormalizeRows(requestedRows);
        var cellSize = NormalizeCellSize(requestedCellSize);

        if (!double.IsFinite(availableWidth) ||
            !double.IsFinite(availableHeight) ||
            availableWidth <= 1 ||
            availableHeight <= 1)
        {
            return CalculateGrid(cellSize, columns, rows);
        }

        var layout = CalculateGrid(cellSize, columns, rows);
        if (layout.WindowWidth <= availableWidth && layout.WindowHeight <= availableHeight)
        {
            return layout;
        }

        cellSize = Math.Min(cellSize, MaxCellSizeThatFits(columns, rows, availableWidth, availableHeight));
        if (cellSize >= MinCellSize)
        {
            return CalculateGrid(cellSize, columns, rows);
        }

        cellSize = MinCellSize;
        while (columns > MinColumns && CalculateGrid(cellSize, columns, rows).WindowWidth > availableWidth)
        {
            columns--;
        }

        while (rows > MinRows && CalculateGrid(cellSize, columns, rows).WindowHeight > availableHeight)
        {
            rows--;
        }

        var stillTooLarge = CalculateGrid(cellSize, columns, rows);
        if (stillTooLarge.WindowWidth > availableWidth || stillTooLarge.WindowHeight > availableHeight)
        {
            cellSize = Math.Max(
                MinCellSize,
                Math.Min(cellSize, MaxCellSizeThatFits(columns, rows, availableWidth, availableHeight)));
        }

        return CalculateGrid(cellSize, columns, rows);
    }

    public static PixelPoint CalculateBottomDockPosition(
        PixelRect workingArea,
        double scaling,
        Size windowSize,
        double bottomMargin = BottomMargin)
    {
        var safeScaling = double.IsFinite(scaling) && scaling > 0d ? scaling : 1d;
        var widthPixels = ScaleToPixels(windowSize.Width, safeScaling);
        var heightPixels = ScaleToPixels(windowSize.Height, safeScaling);
        var marginPixels = ScaleToPixels(Math.Max(0d, bottomMargin), safeScaling);
        var maxX = Math.Max(workingArea.X, workingArea.Right - widthPixels);
        var x = Math.Clamp(
            workingArea.X + Math.Max(0, (workingArea.Width - widthPixels) / 2),
            workingArea.X,
            maxX);
        var y = Math.Max(workingArea.Y, workingArea.Bottom - heightPixels - marginPixels);
        return new PixelPoint(x, y);
    }

    private static double MaxCellSizeThatFits(
        int columns,
        int rows,
        double availableWidth,
        double availableHeight)
    {
        var widthDenominator = columns + Math.Max(0, columns - 1) * GapRatio;
        var heightDenominator = rows + Math.Max(0, rows - 1) * GapRatio;
        var byWidth = (availableWidth - Padding * 2) / widthDenominator;
        var byHeight = (availableHeight - Padding * 2) / heightDenominator;
        var fitted = Math.Min(byWidth, byHeight);
        // 向下取到千分位，避免浮点误差让“刚好放下”的窗口比工作区多出 1 像素。
        return fitted > 0d ? Math.Floor(fitted * 1000d) / 1000d : fitted;
    }

    private static int ScaleToPixels(double logicalSize, double scaling)
    {
        if (!double.IsFinite(logicalSize) || logicalSize <= 0d)
        {
            return 1;
        }

        var pixels = Math.Ceiling(logicalSize * scaling);
        return pixels >= int.MaxValue ? int.MaxValue : Math.Max(1, (int)pixels);
    }
}
