using System.Collections.Generic;

namespace LanMountainDesktop.Models;

public sealed class DesktopLayoutSettingsSnapshot
{
    public int DesktopPageCount { get; set; } = 1;

    public int CurrentDesktopSurfaceIndex { get; set; }

    public List<DesktopComponentPlacementSnapshot> DesktopComponentPlacements { get; set; } = [];

    public DesktopLayoutSettingsSnapshot Clone()
    {
        var clone = (DesktopLayoutSettingsSnapshot)MemberwiseClone();
        var placements = new List<DesktopComponentPlacementSnapshot>(DesktopComponentPlacements?.Count ?? 0);

        if (DesktopComponentPlacements is not null)
        {
            foreach (var placement in DesktopComponentPlacements)
            {
                if (placement is null)
                {
                    continue;
                }

                placements.Add(new DesktopComponentPlacementSnapshot
                {
                    PlacementId = placement.PlacementId,
                    PageIndex = placement.PageIndex,
                    ComponentId = placement.ComponentId,
                    Row = placement.Row,
                    Column = placement.Column,
                    WidthCells = placement.WidthCells,
                    HeightCells = placement.HeightCells
                });
            }
        }

        clone.DesktopComponentPlacements = placements;
        return clone;
    }
}
