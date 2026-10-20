using System;
using System.Collections.Generic;
using LanMountainDesktop.Models;

namespace LanMountainDesktop.DesktopEditing;

internal static class DesktopPanelPlacementRules
{
    public static bool IsWithinBounds(
        int column,
        int row,
        int widthCells,
        int heightCells,
        int columnCount,
        int rowCount)
    {
        return column >= 0 &&
               row >= 0 &&
               widthCells > 0 &&
               heightCells > 0 &&
               column + widthCells <= columnCount &&
               row + heightCells <= rowCount;
    }

    public static bool Overlaps(
        IReadOnlyList<DesktopPanelPlacementSnapshot> placements,
        string? excludedPlacementId,
        int column,
        int row,
        int widthCells,
        int heightCells)
    {
        foreach (var placement in placements)
        {
            if (excludedPlacementId is not null &&
                string.Equals(placement.PlacementId, excludedPlacementId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (RectsOverlap(
                    column,
                    row,
                    widthCells,
                    heightCells,
                    placement.Column,
                    placement.Row,
                    Math.Max(1, placement.WidthCells),
                    Math.Max(1, placement.HeightCells)))
            {
                return true;
            }
        }

        return false;
    }

    public static bool TryFindFreeSlot(
        IReadOnlyList<DesktopPanelPlacementSnapshot> placements,
        int columnCount,
        int rowCount,
        int widthCells,
        int heightCells,
        out int column,
        out int row)
    {
        column = 0;
        row = 0;
        var safeWidth = Math.Max(1, widthCells);
        var safeHeight = Math.Max(1, heightCells);
        if (safeWidth > columnCount || safeHeight > rowCount)
        {
            return false;
        }

        var maxColumn = Math.Max(0, columnCount - safeWidth);
        var maxRow = Math.Max(0, rowCount - safeHeight);
        for (var candidateRow = 0; candidateRow <= maxRow; candidateRow++)
        {
            for (var candidateColumn = 0; candidateColumn <= maxColumn; candidateColumn++)
            {
                if (Overlaps(placements, null, candidateColumn, candidateRow, safeWidth, safeHeight))
                {
                    continue;
                }

                column = candidateColumn;
                row = candidateRow;
                return true;
            }
        }

        return false;
    }

    public static bool CanApplyPlacement(
        IReadOnlyList<DesktopPanelPlacementSnapshot> placements,
        DesktopPanelPlacementSnapshot candidate,
        int columnCount,
        int rowCount)
    {
        return IsWithinBounds(
                   candidate.Column,
                   candidate.Row,
                   candidate.WidthCells,
                   candidate.HeightCells,
                   columnCount,
                   rowCount) &&
               !Overlaps(
                   placements,
                   candidate.PlacementId,
                   candidate.Column,
                   candidate.Row,
                   candidate.WidthCells,
                   candidate.HeightCells);
    }

    /// <summary>
    /// 面板行列变小时，先按最小跨格把每个放置夹进边界；夹完仍与前面的放置重叠的，
    /// 依次找第一个空位。找不到空位的保留原处（宁可视觉重叠，也不静默丢掉用户组件）。
    /// </summary>
    public static void FitToGrid(
        IReadOnlyList<DesktopPanelPlacementSnapshot> placements,
        int columnCount,
        int rowCount,
        Func<string, (int WidthCells, int HeightCells)>? minimumSpanResolver = null)
    {
        var accepted = new List<DesktopPanelPlacementSnapshot>(placements.Count);
        foreach (var placement in placements)
        {
            var minimum = minimumSpanResolver?.Invoke(placement.ComponentId) ?? (1, 1);
            var minWidth = Math.Clamp(Math.Max(1, minimum.WidthCells), 1, columnCount);
            var minHeight = Math.Clamp(Math.Max(1, minimum.HeightCells), 1, rowCount);
            var width = Math.Clamp(Math.Max(1, placement.WidthCells), minWidth, columnCount);
            var height = Math.Clamp(Math.Max(1, placement.HeightCells), minHeight, rowCount);
            placement.Column = Math.Clamp(placement.Column, 0, Math.Max(0, columnCount - width));
            placement.Row = Math.Clamp(placement.Row, 0, Math.Max(0, rowCount - height));
            placement.WidthCells = width;
            placement.HeightCells = height;

            if (Overlaps(accepted, null, placement.Column, placement.Row, width, height))
            {
                if (TryFindFreeSlot(accepted, columnCount, rowCount, width, height, out var freeColumn, out var freeRow))
                {
                    placement.Column = freeColumn;
                    placement.Row = freeRow;
                }
            }

            accepted.Add(placement);
        }
    }

    private static bool RectsOverlap(
        int leftColumn,
        int leftRow,
        int leftWidth,
        int leftHeight,
        int rightColumn,
        int rightRow,
        int rightWidth,
        int rightHeight)
    {
        return leftColumn < rightColumn + rightWidth &&
               leftColumn + leftWidth > rightColumn &&
               leftRow < rightRow + rightHeight &&
               leftRow + leftHeight > rightRow;
    }
}
