using System.Linq;
using LanMountainDesktop.AirAppSdk;
using LanMountainDesktop.Services.Settings;
using Xunit;

namespace LanMountainDesktop.Tests;

public sealed class SettingsCatalogServiceTests
{
    [Fact]
    public void BuiltInAppSectionsIncludeIndependentMaterialColorAndWallpaperEntries()
    {
        var catalog = new SettingsCatalogService();

        var sections = catalog.GetSections(AirAppSettingsScope.App).ToList();

        Assert.Equal(
            [
                "general",
                "material-color",
                "appearance",
                "wallpaper",
                "about"
            ],
            sections.Select(section => section.Id));

        var materialColor = sections.Single(section => section.Id == "material-color");
        Assert.Equal(AirAppSettingsCategories.Appearance, materialColor.Category);
        Assert.Equal(AirAppSettingsScope.App, materialColor.Scope);
        Assert.Equal("settings.material_color.title", materialColor.TitleLocalizationKey);
        Assert.Equal("Color", materialColor.IconKey);

        var wallpaper = sections.Single(section => section.Id == "wallpaper");
        Assert.Equal(AirAppSettingsCategories.Appearance, wallpaper.Category);
        Assert.Equal(AirAppSettingsScope.App, wallpaper.Scope);
        Assert.Equal("settings.wallpaper.title", wallpaper.TitleLocalizationKey);
        Assert.Equal("Image", wallpaper.IconKey);
    }
}
