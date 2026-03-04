namespace LanMountainDesktop.Models;

public sealed class DesktopComponentPlacementSnapshot
{
    public string PlacementId { get; set; } = string.Empty;

    public int PageIndex { get; set; }

    public string ComponentId { get; set; } = string.Empty;

    public int Row { get; set; }

    public int Column { get; set; }

    public int WidthCells { get; set; } = 1;

    public int HeightCells { get; set; } = 1;
}
