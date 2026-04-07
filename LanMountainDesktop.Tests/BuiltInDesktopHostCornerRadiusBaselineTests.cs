using System;
using Avalonia;
using LanMountainDesktop.Appearance;
using LanMountainDesktop.ComponentSystem;
using LanMountainDesktop.Host.Abstractions;
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

    private static ComponentChromeContext CreateChromeContext(
        string componentId,
        double cellSize,
        string style)
    {
        return new ComponentChromeContext(
            componentId,
            null,
            cellSize,
            AppearanceCornerRadiusTokenFactory.Create(style));
    }
}
