using Avalonia;
using LanMountainDesktop.Services.Settings;
using Xunit;

namespace LanMountainDesktop.Tests;

public sealed class SettingsWindowPlacementHelperTests
{
    [Fact]
    public void ResolveWorkingArea_PrefersReferenceScreen()
    {
        var referenceArea = new PixelRect(1920, 0, 2560, 1440);
        var primaryArea = new PixelRect(0, 0, 1920, 1080);

        var result = SettingsWindowPlacementHelper.ResolveWorkingArea(
            referenceArea,
            primaryArea,
            fallbackWindowWidth: 1120,
            fallbackWindowHeight: 760);

        Assert.Equal(referenceArea, result);
    }

    [Fact]
    public void ResolveWorkingArea_FallsBackToPrimaryScreenWhenReferenceIsMissing()
    {
        var primaryArea = new PixelRect(0, 0, 1920, 1080);

        var result = SettingsWindowPlacementHelper.ResolveWorkingArea(
            referenceWorkingArea: null,
            primaryWorkingArea: primaryArea,
            fallbackWindowWidth: 1120,
            fallbackWindowHeight: 760);

        Assert.Equal(primaryArea, result);
    }

    [Fact]
    public void CalculateCenteredPosition_ReturnsCenteredPointInsideWorkingArea()
    {
        var workingArea = new PixelRect(1920, 40, 2560, 1400);

        var result = SettingsWindowPlacementHelper.CalculateCenteredPosition(
            workingArea,
            windowWidth: 1120,
            windowHeight: 760);

        Assert.Equal(new PixelPoint(2640, 360), result);
    }
}
