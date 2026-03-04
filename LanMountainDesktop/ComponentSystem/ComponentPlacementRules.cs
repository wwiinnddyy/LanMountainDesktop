using System;

namespace LanMountainDesktop.ComponentSystem;

public static class ComponentPlacementRules
{
    public static (int WidthCells, int HeightCells) EnsureMinimumSize(
        DesktopComponentDefinition definition,
        int requestedWidthCells,
        int requestedHeightCells)
    {
        var width = Math.Max(definition.MinWidthCells, requestedWidthCells);
        var height = Math.Max(definition.MinHeightCells, requestedHeightCells);
        return (Math.Max(1, width), Math.Max(1, height));
    }

    public static bool CanPlaceInStatusBar(DesktopComponentDefinition definition, int requestedHeightCells)
    {
        return definition.AllowStatusBarPlacement && requestedHeightCells == 1;
    }

    public static (int Column, int Row) ClampToGrid(int requestedColumn, int requestedRow, int maxColumns, int maxRows)
    {
        var clampedColumn = Math.Clamp(requestedColumn, 0, Math.Max(0, maxColumns - 1));
        var clampedRow = Math.Clamp(requestedRow, 0, Math.Max(0, maxRows - 1));
        return (clampedColumn, clampedRow);
    }
}
