using Avalonia;
using Avalonia.Controls;
using LanMountainDesktop.Host.Abstractions;
using LanMountainDesktop.Shared.Contracts;
using LanMountainDesktop.Views.Components;
using Xunit;

namespace LanMountainDesktop.Tests;

public sealed class DesktopComponentRuntimeRegistrationCornerRadiusTests
{
    [Fact]
    public void LegacyCellSizeResolver_AppliesGlobalCornerRadiusScale()
    {
        var registration = new DesktopComponentRuntimeRegistration(
            componentId: "test.component",
            displayNameLocalizationKey: null,
            controlFactory: () => new Border(),
            cornerRadiusResolver: cellSize => Math.Clamp(cellSize * 0.30, 10, 40));

        var resolver = Assert.IsType<Func<ComponentChromeContext, double>>(registration.CornerRadiusResolver);
        var resolved = resolver(CreateChromeContext(cellSize: 120, globalScale: 2.0));

        Assert.Equal(72.0, resolved, 3);
    }

    [Fact]
    public void ChromeContextResolver_IsNotDoubleScaledByRegistrationWrapper()
    {
        var registration = new DesktopComponentRuntimeRegistration(
            componentId: "test.component",
            displayNameLocalizationKey: null,
            controlFactory: _ => new Border(),
            cornerRadiusResolver: chromeContext => chromeContext.CellSize + chromeContext.GlobalCornerRadiusScale);

        var resolver = Assert.IsType<Func<ComponentChromeContext, double>>(registration.CornerRadiusResolver);
        var resolved = resolver(CreateChromeContext(cellSize: 50, globalScale: 2.5));

        Assert.Equal(52.5, resolved, 3);
    }

    private static ComponentChromeContext CreateChromeContext(double cellSize, double globalScale)
    {
        return new ComponentChromeContext(
            ComponentId: "test.component",
            PlacementId: null,
            CellSize: cellSize,
            GlobalCornerRadiusScale: globalScale,
            CornerRadiusTokens: new AppearanceCornerRadiusTokens(
                new CornerRadius(6),
                new CornerRadius(12),
                new CornerRadius(14),
                new CornerRadius(20),
                new CornerRadius(28),
                new CornerRadius(32),
                new CornerRadius(36),
                new CornerRadius(8)));
    }
}
