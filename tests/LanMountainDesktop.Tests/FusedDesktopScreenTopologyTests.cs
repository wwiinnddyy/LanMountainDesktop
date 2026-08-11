using Avalonia;
using LanMountainDesktop.Services;
using Xunit;

namespace LanMountainDesktop.Tests;

public sealed class FusedDesktopScreenTopologyTests
{
    [Fact]
    public void RemovedMonitorPositionMovesToNearestRemainingWorkingArea()
    {
        var screens = new[]
        {
            new FusedDesktopScreenWorkArea(new PixelRect(0, 0, 1920, 1040), 1d)
        };

        var result = FusedDesktopManagerService.CoerceToValidWorkingArea(
            new PixelPoint(-2600, 200),
            new Size(300, 180),
            screens);

        Assert.Equal(new PixelPoint(0, 200), result);
    }

    [Fact]
    public void HighDpiWorkingAreaKeepsEntireWidgetVisible()
    {
        var screens = new[]
        {
            new FusedDesktopScreenWorkArea(new PixelRect(0, 0, 1920, 1040), 2d)
        };

        var result = FusedDesktopManagerService.CoerceToValidWorkingArea(
            new PixelPoint(1800, 980),
            new Size(200, 120),
            screens);

        Assert.Equal(new PixelPoint(1520, 800), result);
    }

    [Fact]
    public void NegativeCoordinateMonitorUsesItsOwnWorkAreaAndScaling()
    {
        var screens = new[]
        {
            new FusedDesktopScreenWorkArea(new PixelRect(-1920, 0, 1920, 1080), 1d),
            new FusedDesktopScreenWorkArea(new PixelRect(0, 0, 2560, 1400), 1.5d)
        };

        var result = FusedDesktopManagerService.CoerceToValidWorkingArea(
            new PixelPoint(-100, 1000),
            new Size(200, 200),
            screens);

        Assert.Equal(new PixelPoint(-200, 880), result);
    }
}
