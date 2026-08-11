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
    public void LegacyCellSizeResolver_ReturnsUnscaledFixedValue()
    {
        var registration = new DesktopComponentRuntimeRegistration(
            componentId: "test.component",
            displayNameLocalizationKey: null,
            controlFactory: () => new Border(),
            cornerRadiusResolver: cellSize => Math.Clamp(cellSize * 0.30, 10, 40));

        var resolver = Assert.IsType<Func<ComponentChromeContext, double>>(registration.CornerRadiusResolver);
        // Previously: (120 * 0.30) * 2.0 = 72.0
        // Now: (120 * 0.30) = 36.0 (No scale applied automatically by the wrapper)
        var resolved = resolver(CreateChromeContext(cellSize: 120));

        Assert.Equal(36.0, resolved, 3);
    }

    [Fact]
    public void ChromeContextResolver_UsesTokenValue()
    {
        var registration = new DesktopComponentRuntimeRegistration(
            componentId: "test.component",
            displayNameLocalizationKey: null,
            controlFactory: _ => new Border(),
            cornerRadiusResolver: chromeContext => chromeContext.CornerRadiusTokens.Component.TopLeft);

        var resolver = Assert.IsType<Func<ComponentChromeContext, double>>(registration.CornerRadiusResolver);
        var resolved = resolver(CreateChromeContext(cellSize: 50));

        Assert.Equal(24.0, resolved, 3);
    }

    private static ComponentChromeContext CreateChromeContext(double cellSize)
    {
        return new ComponentChromeContext(
            ComponentId: "test.component",
            PlacementId: null,
            CellSize: cellSize,
            CornerRadiusTokens: new AppearanceCornerRadiusTokens(
                Micro: new CornerRadius(6),
                Xs: new CornerRadius(12),
                Sm: new CornerRadius(14),
                Md: new CornerRadius(20),
                Lg: new CornerRadius(28),
                Xl: new CornerRadius(32),
                Island: new CornerRadius(36),
                Component: new CornerRadius(24)));
    }
}
