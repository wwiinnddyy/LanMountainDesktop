using LanMountainDesktop.Services;
using LanMountainDesktop.Services.Settings;
using LanMountainDesktop.PluginSdk;
using Xunit;

namespace LanMountainDesktop.Tests;

public sealed class SettingsSearchResultTests
{
    [Fact]
    public void Constructor_TrimsAndNormalizesFields()
    {
        var result = new SettingsSearchResult(
            pageId: "  general  ",
            pageTitle: "  General Settings  ",
            pageDescription: "  App settings  ",
            displayTitle: "  General  ",
            displayDescription: "  Main settings page  ",
            targetId: "  general-page  ",
            targetControl: null,
            isPageResult: true);

        Assert.Equal("general", result.PageId);
        Assert.Equal("General Settings", result.PageTitle);
        Assert.Equal("App settings", result.PageDescription);
        Assert.Equal("General", result.DisplayTitle);
        Assert.Equal("Main settings page", result.DisplayDescription);
        Assert.Equal("general-page", result.TargetId);
    }

    [Fact]
    public void Constructor_NullDescriptionBecomesNull()
    {
        var result = new SettingsSearchResult(
            pageId: "test",
            pageTitle: "Test",
            pageDescription: null,
            displayTitle: "Test",
            displayDescription: "  ",
            targetId: null,
            targetControl: null,
            isPageResult: true);

        Assert.Null(result.PageDescription);
        Assert.Null(result.DisplayDescription);
        Assert.Null(result.TargetId);
    }

    [Fact]
    public void Constructor_ThrowsOnEmptyPageId()
    {
        Assert.Throws<ArgumentException>(() => new SettingsSearchResult(
            pageId: "", pageTitle: "T", pageDescription: null,
            displayTitle: "T", displayDescription: null,
            targetId: null, targetControl: null, isPageResult: true));
    }

    [Fact]
    public void Constructor_ThrowsOnEmptyDisplayTitle()
    {
        Assert.Throws<ArgumentException>(() => new SettingsSearchResult(
            pageId: "test", pageTitle: "T", pageDescription: null,
            displayTitle: "  ", displayDescription: null,
            targetId: null, targetControl: null, isPageResult: true));
    }

    [Fact]
    public void Constructor_DeduplicatesAndNormalizesKeywords()
    {
        var result = new SettingsSearchResult(
            pageId: "test", pageTitle: "Test", pageDescription: null,
            displayTitle: "Test", displayDescription: null,
            targetId: null, targetControl: null, isPageResult: true,
            keywords: new[] { "General", "general", "  ", "Appearance", "GENERAL" });

        Assert.Equal(2, result.Keywords.Count);
        Assert.Contains("General", result.Keywords);
        Assert.Contains("Appearance", result.Keywords);
    }

    [Fact]
    public void SearchText_CombinesAllNonEmptyFields()
    {
        var result = new SettingsSearchResult(
            pageId: "general", pageTitle: "General", pageDescription: "Main page",
            displayTitle: "General Settings", displayDescription: "App configuration",
            targetId: "gen-01", targetControl: null, isPageResult: true,
            keywords: new[] { "Settings", "Config" });

        var searchText = result.SearchText;

        Assert.Contains("general", searchText);
        Assert.Contains("General", searchText);
        Assert.Contains("Main page", searchText);
        Assert.Contains("General Settings", searchText);
        Assert.Contains("App configuration", searchText);
        Assert.Contains("gen-01", searchText);
        Assert.Contains("Settings", searchText);
        Assert.Contains("Config", searchText);
    }

    [Fact]
    public void ToString_ReturnsDisplayTitle()
    {
        var result = new SettingsSearchResult(
            pageId: "test", pageTitle: "Test Page", pageDescription: null,
            displayTitle: "My Display Title", displayDescription: null,
            targetId: null, targetControl: null, isPageResult: false);

        Assert.Equal("My Display Title", result.ToString());
    }
}

public sealed class SettingsSearchServiceFilterTests
{
    private static SettingsSearchResult MakeResult(
        string pageId, string pageTitle, string displayTitle,
        string? displayDescription = null, bool isPageResult = true) =>
        new(pageId, pageTitle, null, displayTitle, displayDescription,
            pageId, null, isPageResult);

    [Fact]
    public void Filter_ReturnsFalseForNullSearch()
    {
        var result = MakeResult("test", "Test", "Test Page");
        Assert.False(SettingsSearchService.Filter(null, result));
    }

    [Fact]
    public void Filter_ReturnsFalseForEmptySearch()
    {
        var result = MakeResult("test", "Test", "Test Page");
        Assert.False(SettingsSearchService.Filter("", result));
    }

    [Fact]
    public void Filter_ReturnsFalseForNonSearchResultItem()
    {
        Assert.False(SettingsSearchService.Filter("test", "not a result"));
        Assert.False(SettingsSearchService.Filter("test", null));
    }

    [Fact]
    public void Filter_MatchesDisplayTitlePrefix()
    {
        var result = MakeResult("general", "General", "General Settings");
        Assert.True(SettingsSearchService.Filter("General", result));
        Assert.True(SettingsSearchService.Filter("Gen", result));
    }

    [Fact]
    public void Filter_MatchesDisplayTitleContains()
    {
        var result = MakeResult("general", "General", "General Settings");
        Assert.True(SettingsSearchService.Filter("Settings", result));
    }

    [Fact]
    public void Filter_MatchesPageTitle()
    {
        var result = MakeResult("general", "General Settings", "General");
        Assert.True(SettingsSearchService.Filter("Settings", result));
    }

    [Fact]
    public void Filter_IsCaseInsensitive()
    {
        var result = MakeResult("general", "General", "General Settings");
        Assert.True(SettingsSearchService.Filter("general", result));
        Assert.True(SettingsSearchService.Filter("GENERAL", result));
    }

    [Fact]
    public void Filter_ReturnsFalseWhenNoTermMatches()
    {
        var result = MakeResult("general", "General", "General Settings");
        Assert.False(SettingsSearchService.Filter("xyznotfound", result));
    }
}
