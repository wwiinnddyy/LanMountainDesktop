using LanMountainDesktop.ComponentSystem;
using Xunit;

namespace LanMountainDesktop.Tests;

public sealed class ComponentRegistryTests
{
    private static DesktopComponentDefinition Def(string id, string display = "Test", string category = "Info") =>
        new(id, display, "Icon", category, MinWidthCells: 2, MinHeightCells: 2,
            AllowStatusBarPlacement: false, AllowDesktopPlacement: true);

    [Fact]
    public void Constructor_FiltersOutEmptyAndWhitespaceIds()
    {
        var definitions = new[]
        {
            Def("Valid"),
            Def(""),
            Def("  "),
            Def(null!),
            Def("Another")
        };

        var registry = new ComponentRegistry(definitions);

        Assert.True(registry.IsKnownComponent("Valid"));
        Assert.True(registry.IsKnownComponent("Another"));
        Assert.False(registry.IsKnownComponent(""));
        Assert.False(registry.IsKnownComponent("  "));
    }

    [Fact]
    public void Constructor_DuplicateIdsAreCaseInsensitiveLastWins()
    {
        var first = Def("clock", display: "First");
        var second = Def("Clock", display: "Second");
        var third = Def("CLOCK", display: "Third");

        var registry = new ComponentRegistry(new[] { first, second, third });

        Assert.True(registry.TryGetDefinition("clock", out var result));
        Assert.Equal("Third", result.DisplayName);
    }

    [Fact]
    public void TryGetDefinition_ReturnsFalseForUnknownComponent()
    {
        var registry = new ComponentRegistry(new[] { Def("Known") });

        Assert.False(registry.TryGetDefinition("Unknown", out _));
    }

    [Fact]
    public void TryGetDefinition_IsCaseInsensitive()
    {
        var registry = new ComponentRegistry(new[] { Def("DesktopClock") });

        Assert.True(registry.TryGetDefinition("desktopclock", out var def));
        Assert.Equal("DesktopClock", def.Id);
    }

    [Fact]
    public void IsKnownComponent_ThrowsOnNull()
    {
        var registry = new ComponentRegistry(new[] { Def("A") });

        Assert.Throws<ArgumentNullException>(() => registry.IsKnownComponent(null!));
    }

    [Fact]
    public void AllowsStatusBarPlacement_ReturnsTrueOnlyForStatusBarAllowed()
    {
        var statusBarAllowed = new DesktopComponentDefinition(
            "Clock", "Clock", "Clock", "Status",
            MinWidthCells: 3, MinHeightCells: 1,
            AllowStatusBarPlacement: true, AllowDesktopPlacement: false);
        var desktopOnly = Def("DesktopClock");

        var registry = new ComponentRegistry(new[] { statusBarAllowed, desktopOnly });

        Assert.True(registry.AllowsStatusBarPlacement("Clock"));
        Assert.False(registry.AllowsStatusBarPlacement("DesktopClock"));
        Assert.False(registry.AllowsStatusBarPlacement("Unknown"));
    }

    [Fact]
    public void GetAll_ReturnsSortedByCategoryThenDisplayName()
    {
        var definitions = new[]
        {
            Def("Zebra", display: "Zebra", category: "Info"),
            Def("Alpha", display: "Alpha", category: "Info"),
            Def("Beta", display: "Beta", category: "Date"),
        };

        var registry = new ComponentRegistry(definitions);
        var all = registry.GetAll();

        Assert.Equal(3, all.Count);
        Assert.Equal("Date", all[0].Category);
        Assert.Equal("Beta", all[0].DisplayName);
        Assert.Equal("Info", all[1].Category);
        Assert.Equal("Alpha", all[1].DisplayName);
        Assert.Equal("Info", all[2].Category);
        Assert.Equal("Zebra", all[2].DisplayName);
    }

    [Fact]
    public void RegisterComponents_MergesNewDefinitionsIntoNewRegistry()
    {
        var original = new ComponentRegistry(new[] { Def("A") });
        var extended = original.RegisterComponents(new[] { Def("B") });

        Assert.True(original.IsKnownComponent("A"));
        Assert.False(original.IsKnownComponent("B"));
        Assert.True(extended.IsKnownComponent("A"));
        Assert.True(extended.IsKnownComponent("B"));
    }

    [Fact]
    public void RegisterComponents_DuplicateIdOverridesOriginal()
    {
        var original = new ComponentRegistry(new[] { Def("A", display: "Original") });
        var extended = original.RegisterComponents(new[] { Def("A", display: "Override") });

        Assert.True(extended.TryGetDefinition("A", out var result));
        Assert.Equal("Override", result.DisplayName);
    }

    [Fact]
    public void CreateDefault_ContainsExpectedBuiltInComponents()
    {
        var registry = ComponentRegistry.CreateDefault();

        Assert.True(registry.IsKnownComponent(BuiltInComponentIds.Clock));
        Assert.True(registry.IsKnownComponent(BuiltInComponentIds.DesktopClock));
        Assert.True(registry.IsKnownComponent(BuiltInComponentIds.DesktopWeather));
        Assert.True(registry.IsKnownComponent(BuiltInComponentIds.DesktopMusicControl));
        Assert.True(registry.IsKnownComponent(BuiltInComponentIds.DesktopWhiteboard));
        Assert.True(registry.IsKnownComponent(BuiltInComponentIds.DesktopRssReader));
    }

    [Fact]
    public void CreateDefault_ClockAllowsStatusBarWhileDesktopComponentsDoNot()
    {
        var registry = ComponentRegistry.CreateDefault();

        Assert.True(registry.AllowsStatusBarPlacement(BuiltInComponentIds.Clock));
        Assert.False(registry.AllowsStatusBarPlacement(BuiltInComponentIds.DesktopClock));
        Assert.False(registry.AllowsStatusBarPlacement(BuiltInComponentIds.DesktopWeather));
    }

    [Fact]
    public void CreateDefault_AllDesktopComponentsAllowDesktopPlacement()
    {
        var registry = ComponentRegistry.CreateDefault();
        var all = registry.GetAll();

        foreach (var def in all.Where(d => d.Id.StartsWith("Desktop", StringComparison.Ordinal)))
        {
            Assert.True(def.AllowDesktopPlacement, $"{def.Id} should allow desktop placement");
        }
    }
}
