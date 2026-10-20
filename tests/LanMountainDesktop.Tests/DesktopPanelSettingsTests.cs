using System.Text.Json;
using LanMountainDesktop.DesktopEditing;
using LanMountainDesktop.Models;
using Xunit;

namespace LanMountainDesktop.Tests;

public sealed class DesktopPanelSettingsTests
{
    [Fact]
    public void Defaults_DescribeAThreeRowPanelWithEightColumns()
    {
        var snapshot = new AppSettingsSnapshot();

        Assert.False(snapshot.EnableDesktopPanel);
        Assert.Equal(DesktopPanelMetrics.DefaultCellSize, snapshot.DesktopPanelCellSize, 3);
        Assert.Equal(DesktopPanelMetrics.DefaultColumns, snapshot.DesktopPanelColumns);
        Assert.Equal(DesktopPanelMetrics.DefaultRows, snapshot.DesktopPanelRows);
        Assert.True(snapshot.DesktopPanelUseDesktopLayer);
    }

    [Fact]
    public void EnableSwitch_StaysOnTheLegacyDiskKey()
    {
        var json = JsonSerializer.Serialize(new AppSettingsSnapshot { EnableDesktopPanel = true });
        using var document = JsonDocument.Parse(json);

        Assert.True(document.RootElement.TryGetProperty("EnableFusedDesktop", out var legacyKey));
        Assert.True(legacyKey.GetBoolean());
        Assert.False(document.RootElement.TryGetProperty("EnableDesktopPanel", out _));
    }

    [Fact]
    public void LegacyEnableFusedDesktopValue_UpgradesIntoThePanelSwitch()
    {
        var snapshot = JsonSerializer.Deserialize<AppSettingsSnapshot>(
            """{"EnableFusedDesktop":true}""");

        Assert.NotNull(snapshot);
        Assert.True(snapshot!.EnableDesktopPanel);
    }

    [Fact]
    public void LayoutClone_DoesNotSharePlacementInstances()
    {
        var layout = new DesktopPanelLayoutSnapshot
        {
            ComponentPlacements =
            [
                new DesktopPanelPlacementSnapshot
                {
                    PlacementId = "placement",
                    ComponentId = "component",
                    Column = 1,
                    Row = 2,
                    WidthCells = 3,
                    HeightCells = 4
                }
            ]
        };

        var clone = layout.Clone();
        clone.ComponentPlacements[0].Column = 9;

        Assert.Equal(1, layout.ComponentPlacements[0].Column);
        Assert.Equal(9, clone.ComponentPlacements[0].Column);
    }
}
