using System.Collections.Generic;
using LanMountainDesktop.Appearance;
using LanMountainDesktop.ComponentSystem;
using LanMountainDesktop.Host.Abstractions;
using LanMountainDesktop.Views.Components;
using Xunit;

namespace LanMountainDesktop.Tests;

public sealed class InfoRecommendationHostCornerRadiusTests
{
    private static readonly string[] WideInfoComponentIds =
    [
        BuiltInComponentIds.DesktopDailyPoetry,
        BuiltInComponentIds.DesktopDailyArtwork,
        BuiltInComponentIds.DesktopDailyWord,
        BuiltInComponentIds.DesktopCnrDailyNews,
        BuiltInComponentIds.DesktopIfengNews,
        BuiltInComponentIds.DesktopBilibiliHotSearch,
        BuiltInComponentIds.DesktopBaiduHotSearch,
        BuiltInComponentIds.DesktopStcn24Forum
    ];

    [Theory]
    [InlineData(80d)]
    [InlineData(120d)]
    [InlineData(160d)]
    public void InfoHostRegistrations_ResolveToTheUnifiedLgBaseline(double cellSize)
    {
        var registry = new DesktopComponentRuntimeRegistry(
            ComponentRegistry.CreateDefault(),
            DesktopComponentRuntimeRegistry.GetDefaultRegistrations());

        foreach (var componentId in WideInfoComponentIds)
        {
            AssertResolved(registry, componentId, cellSize);
        }

        AssertResolved(registry, BuiltInComponentIds.DesktopDailyWord2x2, cellSize);
    }

    private static void AssertResolved(
        DesktopComponentRuntimeRegistry registry,
        string componentId,
        double cellSize)
    {
        Assert.True(
            registry.TryGetDescriptor(componentId, out var descriptor),
            $"Missing runtime registration for '{componentId}'.");

        var sharp = descriptor.ResolveCornerRadius(CreateChromeContext(componentId, cellSize, "Sharp"));
        var balanced = descriptor.ResolveCornerRadius(CreateChromeContext(componentId, cellSize, "Balanced"));
        var rounded = descriptor.ResolveCornerRadius(CreateChromeContext(componentId, cellSize, "Rounded"));
        var open = descriptor.ResolveCornerRadius(CreateChromeContext(componentId, cellSize, "Open"));

        // All info widgets should resolve to the Component token in the new system
        Assert.Equal(20d, sharp, 3);
        Assert.Equal(24d, balanced, 3);
        Assert.Equal(28d, rounded, 3);
        Assert.Equal(32d, open, 3);
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
