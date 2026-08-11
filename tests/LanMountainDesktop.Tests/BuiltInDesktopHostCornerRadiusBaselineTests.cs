using System;
using Avalonia;
using LanMountainDesktop.AirAppSdk;
using LanMountainDesktop.ComponentSystem;
using LanMountainDesktop.AirAppSdk;
using LanMountainDesktop.Views.Components;
using Xunit;

namespace LanMountainDesktop.Tests;

public sealed class BuiltInDesktopHostCornerRadiusBaselineTests
{
    [Theory]
    [InlineData(80d, "Sharp")]
    [InlineData(120d, "Balanced")]
    [InlineData(160d, "Rounded")]
    public void BuiltInDesktopHosts_ResolveToTheUnifiedLgBaseline(double cellSize, string style)
    {
        var registry = new DesktopComponentRuntimeRegistry(
            ComponentRegistry.CreateDefault(),
            DesktopComponentRuntimeRegistry.GetDefaultRegistrations());
        var expected = AppearanceCornerRadiusTokenFactory.Create(style).Component.TopLeft;

        foreach (var descriptor in registry.GetDesktopComponents())
        {
            var resolved = descriptor.ResolveCornerRadius(CreateChromeContext(descriptor.Definition.Id, cellSize, style));
            Assert.Equal(expected, resolved, 3);
        }
    }

    private static AirAppComponentChromeContext CreateChromeContext(
        string componentId,
        double cellSize,
        string style)
    {
        return new AirAppComponentChromeContext(
            componentId,
            null,
            cellSize,
            AppearanceCornerRadiusTokenFactory.Create(style));
    }
}
