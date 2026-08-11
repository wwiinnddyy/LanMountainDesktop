using System.Linq;
using LanMountainDesktop.PluginSdk;
using LanMountainDesktop.Services.Settings;
using Xunit;

namespace LanMountainDesktop.Tests;

public sealed class SettingsCatalogServiceTests
{
    [Fact]
    public void BuiltInAppSectionsIncludeIndependentMaterialColorAndWallpaperEntries()
    {
        var catalog = new SettingsCatalogService();

        var sections = catalog.GetSections(SettingsScope.App).ToList();

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
        Assert.Equal(SettingsCategories.Appearance, materialColor.Category);
        Assert.Equal(SettingsScope.App, materialColor.Scope);
        Assert.Equal("settings.material_color.title", materialColor.TitleLocalizationKey);
        Assert.Equal("Color", materialColor.IconKey);

        var wallpaper = sections.Single(section => section.Id == "wallpaper");
        Assert.Equal(SettingsCategories.Appearance, wallpaper.Category);
        Assert.Equal(SettingsScope.App, wallpaper.Scope);
        Assert.Equal("settings.wallpaper.title", wallpaper.TitleLocalizationKey);
        Assert.Equal("Image", wallpaper.IconKey);
    }
}
