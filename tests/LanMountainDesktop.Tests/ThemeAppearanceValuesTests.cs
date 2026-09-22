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

    [Theory]
    [InlineData("auto", ThemeAppearanceValues.WallpaperColorSourceAuto)]
    [InlineData("APP", ThemeAppearanceValues.WallpaperColorSourceApp)]
    [InlineData("system", ThemeAppearanceValues.WallpaperColorSourceSystem)]
    [InlineData("unknown", ThemeAppearanceValues.WallpaperColorSourceAuto)]
    [InlineData(null, ThemeAppearanceValues.WallpaperColorSourceAuto)]
    public void NormalizeWallpaperColorSource_ReturnsKnownValue(string? input, string expected)
    {
        Assert.Equal(expected, ThemeAppearanceValues.NormalizeWallpaperColorSource(input));
    }

    /// <summary>
    /// 明暗档的取值口径。收口前设置页 view model 与主题领域服务各抄一份，
    /// "认哪些串"这件事有两个真源——忽略大小写这一格尤其不能漂：盘上有早期写的 <c>Dark</c>，
    /// 读的一侧认、写的一侧不认，同一个设置就会在界面显示成黑夜、落盘被改回白天。
    /// </summary>
    [Theory]
    [InlineData("light", ThemeAppearanceValues.ThemeModeLight)]
    [InlineData("dark", ThemeAppearanceValues.ThemeModeDark)]
    [InlineData("DARK", ThemeAppearanceValues.ThemeModeDark)]
    [InlineData("follow_system", ThemeAppearanceValues.ThemeModeFollowSystem)]
    [InlineData("Follow_System", ThemeAppearanceValues.ThemeModeFollowSystem)]
    [InlineData("night", ThemeAppearanceValues.ThemeModeLight)]
    [InlineData("", ThemeAppearanceValues.ThemeModeLight)]
    [InlineData(null, ThemeAppearanceValues.ThemeModeLight)]
    public void NormalizeThemeMode_ReturnsKnownValue(string? input, string expected)
    {
        Assert.Equal(expected, ThemeAppearanceValues.NormalizeThemeMode(input));
    }
}
