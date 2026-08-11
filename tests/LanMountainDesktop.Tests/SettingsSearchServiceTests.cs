using LanMountainDesktop.Services;
using Xunit;

namespace LanMountainDesktop.Tests;

public sealed class SettingsSearchServiceTests
{
    [Fact]
    public void Filter_MatchesTitleAndPageMetadata()
    {
        var result = new SettingsSearchResult(
            "appearance",
            "Appearance",
            "Theme and material settings",
            "System material",
            "Choose Mica or Acrylic",
            "appearance:material",
            targetControl: null,
            isPageResult: false,
            keywords: ["fluent"]);

        Assert.True(SettingsSearchService.Filter("material", result));
        Assert.True(SettingsSearchService.Filter("appearance", result));
        Assert.True(SettingsSearchService.Filter("fluent", result));
        Assert.False(SettingsSearchService.Filter("network", result));
    }
}
