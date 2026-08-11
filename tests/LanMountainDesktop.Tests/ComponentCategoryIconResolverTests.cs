using FluentIcons.Common;
using LanMountainDesktop.ComponentSystem;
using Xunit;

namespace LanMountainDesktop.Tests;

public sealed class ComponentCategoryIconResolverTests
{
    [Fact]
    public void ResolveCategoryIcon_AllCategory_ReturnsApps()
    {
        var result = ComponentCategoryIconResolver.ResolveCategoryIcon("all", []);
        Assert.Equal(Icon.Apps, result);
    }

    [Fact]
    public void ResolveCategoryIcon_ResolvesFromFirstComponentIconKey()
    {
        var components = new[]
        {
            new DesktopComponentDefinition("test1", "Test", "Clock", "Clock", 2, 2, false, true)
        };
        var result = ComponentCategoryIconResolver.ResolveCategoryIcon("Clock", components);
        Assert.Equal(Icon.Clock, result);
    }

    [Fact]
    public void ResolveCategoryIcon_WeatherSunny_ResolvesCorrectly()
    {
        var components = new[]
        {
            new DesktopComponentDefinition("test1", "Test", "WeatherSunny", "Weather", 2, 2, false, true)
        };
        var result = ComponentCategoryIconResolver.ResolveCategoryIcon("Weather", components);
        Assert.Equal(Icon.WeatherSunny, result);
    }

    [Fact]
    public void ResolveCategoryIcon_News_ResolvesCorrectly()
    {
        var components = new[]
        {
            new DesktopComponentDefinition("test1", "Test", "News", "Info", 2, 2, false, true)
        };
        var result = ComponentCategoryIconResolver.ResolveCategoryIcon("Info", components);
        Assert.Equal(Icon.News, result);
    }

    [Fact]
    public void ResolveCategoryIcon_Edit_ResolvesCorrectly()
    {
        var components = new[]
        {
            new DesktopComponentDefinition("test1", "Test", "Edit", "Board", 2, 2, false, true)
        };
        var result = ComponentCategoryIconResolver.ResolveCategoryIcon("Board", components);
        Assert.Equal(Icon.Edit, result);
    }

    [Fact]
    public void ResolveCategoryIcon_InvalidIconKey_FallsBackToApps()
    {
        var components = new[]
        {
            new DesktopComponentDefinition("test1", "Test", "NonExistentIcon", "Other", 2, 2, false, true)
        };
        var result = ComponentCategoryIconResolver.ResolveCategoryIcon("Other", components);
        Assert.Equal(Icon.Apps, result);
    }

    [Fact]
    public void ResolveCategoryIcon_EmptyComponents_FallsBackToApps()
    {
        var result = ComponentCategoryIconResolver.ResolveCategoryIcon("Unknown", []);
        Assert.Equal(Icon.Apps, result);
    }

    [Fact]
    public void ResolveCategoryIcon_Play_ResolvesCorrectly()
    {
        var components = new[]
        {
            new DesktopComponentDefinition("test1", "Test", "Play", "Media", 2, 2, false, true)
        };
        var result = ComponentCategoryIconResolver.ResolveCategoryIcon("Media", components);
        Assert.Equal(Icon.Play, result);
    }

    [Fact]
    public void ResolveCategoryIcon_Calculator_ResolvesCorrectly()
    {
        var components = new[]
        {
            new DesktopComponentDefinition("test1", "Test", "Calculator", "Calculator", 2, 2, false, true)
        };
        var result = ComponentCategoryIconResolver.ResolveCategoryIcon("Calculator", components);
        Assert.Equal(Icon.Calculator, result);
    }

    [Fact]
    public void ResolveCategoryIcon_Folder_ResolvesCorrectly()
    {
        var components = new[]
        {
            new DesktopComponentDefinition("test1", "Test", "Folder", "File", 2, 2, false, true)
        };
        var result = ComponentCategoryIconResolver.ResolveCategoryIcon("File", components);
        Assert.Equal(Icon.Folder, result);
    }

    [Fact]
    public void ResolveCategoryIcon_Date_ResolvesCorrectly()
    {
        var result = ComponentCategoryIconResolver.ResolveCategoryIcon("Date", []);
        Assert.Equal(Icon.Calendar, result);
    }

    [Fact]
    public void ResolveCategoryIcon_Study_ResolvesCorrectly()
    {
        var result = ComponentCategoryIconResolver.ResolveCategoryIcon("Study", []);
        Assert.Equal(Icon.Book, result);
    }
}
