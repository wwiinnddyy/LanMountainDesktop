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
}
