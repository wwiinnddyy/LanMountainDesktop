using System;
using Avalonia;
using LanMountainDesktop.ComponentSystem;

namespace LanMountainDesktop.Views;

internal readonly record struct FusedDesktopLibraryPreviewMetrics(
    int WidthCells,
    int HeightCells,
    double CellSize,
    double Width,
    double Height);

internal static class FusedDesktopLibraryPreviewLayout
{
    internal const double DefaultStageWidth = 460d;
    internal const double DefaultStageHeight = 300d;

    private const double StageHorizontalInset = 48d;
    private const double StageVerticalInset = 42d;
    private const double MinCellSize = 32d;
    private const double MaxCellSize = 128d;

    public static FusedDesktopLibraryPreviewMetrics Calculate(
        DesktopComponentDefinition definition,
        Size stageSize)
    {
        ArgumentNullException.ThrowIfNull(definition);

        return Calculate(
            definition.MinWidthCells,
            definition.MinHeightCells,
            stageSize.Width,
            stageSize.Height);
    }

    public static FusedDesktopLibraryPreviewMetrics Calculate(
        int widthCells,
        int heightCells,
        double stageWidth,
        double stageHeight)
    {
        var normalizedWidthCells = Math.Max(1, widthCells);
        var normalizedHeightCells = Math.Max(1, heightCells);
        var normalizedStageWidth = NormalizeStageLength(stageWidth, DefaultStageWidth);
        var normalizedStageHeight = NormalizeStageLength(stageHeight, DefaultStageHeight);

        var availableWidth = Math.Max(1d, normalizedStageWidth - StageHorizontalInset);
        var availableHeight = Math.Max(1d, normalizedStageHeight - StageVerticalInset);
        var fitCellSize = Math.Min(
            availableWidth / normalizedWidthCells,
            availableHeight / normalizedHeightCells);

        if (!double.IsFinite(fitCellSize) || fitCellSize <= 0d)
        {
            fitCellSize = Math.Min(
                (DefaultStageWidth - StageHorizontalInset) / normalizedWidthCells,
                (DefaultStageHeight - StageVerticalInset) / normalizedHeightCells);
        }

        var cellSize = fitCellSize >= MinCellSize
            ? Math.Min(fitCellSize, MaxCellSize)
            : Math.Max(1d, fitCellSize);

        return new FusedDesktopLibraryPreviewMetrics(
            normalizedWidthCells,
            normalizedHeightCells,
            cellSize,
            normalizedWidthCells * cellSize,
            normalizedHeightCells * cellSize);
    }

    private static double NormalizeStageLength(double value, double fallback)
    {
        return double.IsFinite(value) && value > 1d
            ? value
            : fallback;
    }
}
