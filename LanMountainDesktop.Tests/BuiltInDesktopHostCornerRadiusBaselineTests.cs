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
    [InlineData(80d, 0d)]
    [InlineData(120d, 1d)]
    [InlineData(160d, 2.5d)]
    public void BuiltInDesktopHosts_ResolveToTheUnifiedLgBaseline(double cellSize, double globalScale)
    {
        var registry = new DesktopComponentRuntimeRegistry(
            ComponentRegistry.CreateDefault(),
            DesktopComponentRuntimeRegistry.GetDefaultRegistrations());
        var expected = AppearanceCornerRadiusTokenFactory.Create(globalScale).Component.TopLeft;

        foreach (var descriptor in registry.GetDesktopComponents())
        {
            var resolved = descriptor.ResolveCornerRadius(CreateChromeContext(descriptor.Definition.Id, cellSize, globalScale));
            Assert.Equal(expected, resolved, 3);
        }
    }

    private static ComponentChromeContext CreateChromeContext(
        string componentId,
        double cellSize,
        double globalScale)
    {
        return new ComponentChromeContext(
            componentId,
            null,
            cellSize,
            globalScale,
            AppearanceCornerRadiusTokenFactory.Create(globalScale));
    }
}
