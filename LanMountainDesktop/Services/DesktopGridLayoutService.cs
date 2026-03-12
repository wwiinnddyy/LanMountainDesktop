using System;

namespace LanMountainDesktop.Services;

public readonly record struct DesktopGridMetrics(
    int ColumnCount,
    int RowCount,
    double CellSize,
    double GapPx,
    double EdgeInsetPx,
    double GridWidthPx,
    double GridHeightPx)
{
    public double Pitch => CellSize + GapPx;
}

public sealed class DesktopGridLayoutService
{
    public const string RelaxedSpacingPreset = "Relaxed";
    public const string CompactSpacingPreset = "Compact";

    public string NormalizeSpacingPreset(string? value)
    {
        return string.Equals(value, CompactSpacingPreset, StringComparison.OrdinalIgnoreCase)
            ? CompactSpacingPreset
            : RelaxedSpacingPreset;
    }

    public double ResolveGapRatio(string? preset)
    {
        return string.Equals(preset, CompactSpacingPreset, StringComparison.OrdinalIgnoreCase) ? 0.06 : 0.12;
    }

    public double CalculateEdgeInset(double hostWidth, double hostHeight, int shortSideCells, int insetPercent)
    {
        if (hostWidth <= 1 || hostHeight <= 1)
        {
            return 0;
        }

        var cells = Math.Max(1, shortSideCells);
        var shortSidePx = Math.Max(1, Math.Min(hostWidth, hostHeight));
        var baseCell = shortSidePx / cells;
        var insetRatio = Math.Clamp(insetPercent, 0, 30) / 100d;
        return Math.Clamp(baseCell * insetRatio, 0, 80);
    }

    public DesktopGridMetrics CalculateGridMetrics(
        double hostWidth,
        double hostHeight,
        int shortSideCells,
        double gapRatio,
        double edgeInsetPx)
    {
        if (hostWidth <= 1 || hostHeight <= 1)
        {
            return default;
        }

        var shortSide = Math.Max(1, shortSideCells);
        var clampedGapRatio = Math.Max(0, gapRatio);
        var inset = Math.Max(0, edgeInsetPx);
        var availableWidth = Math.Max(1, hostWidth - inset * 2);
        var availableHeight = Math.Max(1, hostHeight - inset * 2);

        if (hostWidth >= hostHeight)
        {
            var rowCount = shortSide;
            var denominator = rowCount + Math.Max(0, rowCount - 1) * clampedGapRatio;
            if (denominator <= 0)
            {
                return default;
            }

            var cellSize = availableHeight / denominator;
            var gapPx = cellSize * clampedGapRatio;
            var pitch = cellSize + gapPx;
            if (pitch <= 0)
            {
                return default;
            }

            var columnCount = Math.Max(1, (int)Math.Floor((availableWidth + gapPx) / pitch));
            var gridWidth = columnCount * cellSize + Math.Max(0, columnCount - 1) * gapPx;
            var gridHeight = rowCount * cellSize + Math.Max(0, rowCount - 1) * gapPx;
            return new DesktopGridMetrics(columnCount, rowCount, cellSize, gapPx, inset, gridWidth, gridHeight);
        }

        var columnCountPortrait = shortSide;
        var denominatorPortrait = columnCountPortrait + Math.Max(0, columnCountPortrait - 1) * clampedGapRatio;
        if (denominatorPortrait <= 0)
        {
            return default;
        }

        var cellSizePortrait = availableWidth / denominatorPortrait;
        var gapPxPortrait = cellSizePortrait * clampedGapRatio;
        var pitchPortrait = cellSizePortrait + gapPxPortrait;
        if (pitchPortrait <= 0)
        {
            return default;
        }

        var rowCountPortrait = Math.Max(1, (int)Math.Floor((availableHeight + gapPxPortrait) / pitchPortrait));
        var gridWidthPortrait = columnCountPortrait * cellSizePortrait + Math.Max(0, columnCountPortrait - 1) * gapPxPortrait;
        var gridHeightPortrait = rowCountPortrait * cellSizePortrait + Math.Max(0, rowCountPortrait - 1) * gapPxPortrait;
        return new DesktopGridMetrics(
            columnCountPortrait,
            rowCountPortrait,
            cellSizePortrait,
            gapPxPortrait,
            inset,
            gridWidthPortrait,
            gridHeightPortrait);
    }
}
