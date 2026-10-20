using System;
using System.Collections.Generic;

namespace LanMountainDesktop.Models;

/// <summary>
/// 桌面板内的一个组件放置。坐标与跨格数都相对于桌面板自己的网格，
/// 与屏幕坐标无关——桌面板大小变化时只需要重新计算像素几何。
/// </summary>
public sealed class DesktopPanelPlacementSnapshot
{
    public string PlacementId { get; set; } = string.Empty;

    public string ComponentId { get; set; } = string.Empty;

    public int Column { get; set; }

    public int Row { get; set; }

    public int WidthCells { get; set; } = 1;

    public int HeightCells { get; set; } = 1;

    public DesktopPanelPlacementSnapshot Clone()
    {
        return new DesktopPanelPlacementSnapshot
        {
            PlacementId = PlacementId,
            ComponentId = ComponentId,
            Column = Column,
            Row = Row,
            WidthCells = WidthCells,
            HeightCells = HeightCells
        };
    }
}

public sealed class DesktopPanelLayoutSnapshot
{
    public List<DesktopPanelPlacementSnapshot> ComponentPlacements { get; set; } = [];

    public DesktopPanelLayoutSnapshot Clone()
    {
        return new DesktopPanelLayoutSnapshot
        {
            ComponentPlacements = [.. ComponentPlacements.ConvertAll(static placement => placement.Clone())]
        };
    }
}
