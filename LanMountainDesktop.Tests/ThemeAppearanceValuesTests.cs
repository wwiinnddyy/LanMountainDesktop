using LanMountainDesktop.Services;
using Xunit;

namespace LanMountainDesktop.Tests;

public sealed class ThemeAppearanceValuesTests
{
    [Theory]
    [InlineData("auto", ThemeAppearanceValues.MaterialAuto)]
    [InlineData("AUTO", ThemeAppearanceValues.MaterialAuto)]
    [InlineData("mica", ThemeAppearanceValues.MaterialMica)]
    [InlineData("acrylic", ThemeAppearanceValues.MaterialAcrylic)]
    [InlineData("unknown", ThemeAppearanceValues.MaterialNone)]
    [InlineData(null, ThemeAppearanceValues.MaterialNone)]
    public void NormalizeSystemMaterialMode_ReturnsKnownValue(string? input, string expected)
    {
        Assert.Equal(expected, ThemeAppearanceValues.NormalizeSystemMaterialMode(input));
    }

    [Fact]
    public void NormalizeAvailableMaterialModes_AddsAutoAndNone()
    {
        var result = ThemeAppearanceValues.NormalizeAvailableMaterialModes([ThemeAppearanceValues.MaterialMica]);

        Assert.Equal(ThemeAppearanceValues.MaterialAuto, result[0]);
        Assert.Equal(ThemeAppearanceValues.MaterialNone, result[1]);
        Assert.Contains(ThemeAppearanceValues.MaterialMica, result);
    }
}
