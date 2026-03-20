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

        var zero = descriptor.ResolveCornerRadius(CreateChromeContext(componentId, cellSize, 0d));
        var unit = descriptor.ResolveCornerRadius(CreateChromeContext(componentId, cellSize, 1d));
        var max = descriptor.ResolveCornerRadius(CreateChromeContext(componentId, cellSize, 2.5d));

        Assert.Equal(0d, zero, 3);
        Assert.Equal(24d, unit, 3);
        Assert.Equal(60d, max, 3);
        Assert.True(zero <= unit && unit <= max);
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
