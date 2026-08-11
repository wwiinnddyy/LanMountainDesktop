using LanMountainDesktop.Services;
using Xunit;

namespace LanMountainDesktop.Tests;

public sealed class LocalizationServiceTests
{
    private readonly LocalizationService _service = new();

    [Theory]
    [InlineData(null, "zh-CN")]
    [InlineData("", "zh-CN")]
    [InlineData("  ", "zh-CN")]
    [InlineData("zh-CN", "zh-CN")]
    [InlineData("zh-cn", "zh-CN")]
    [InlineData("en-US", "en-US")]
    [InlineData("en-us", "en-US")]
    [InlineData("en", "en-US")]
    [InlineData("EN", "en-US")]
    [InlineData("ja-JP", "ja-JP")]
    [InlineData("ja-jp", "ja-JP")]
    [InlineData("ja", "ja-JP")]
    [InlineData("JA", "ja-JP")]
    [InlineData("ko-KR", "ko-KR")]
    [InlineData("ko-kr", "ko-KR")]
    [InlineData("ko", "ko-KR")]
    [InlineData("KO", "ko-KR")]
    [InlineData("fr-FR", "zh-CN")]
    [InlineData("de", "zh-CN")]
    [InlineData("unknown", "zh-CN")]
    public void NormalizeLanguageCode_MapsToSupportedLocales(string? input, string expected)
    {
        Assert.Equal(expected, _service.NormalizeLanguageCode(input));
    }

    [Fact]
    public void GetString_ReturnsFallbackWhenNoLanguageFilesExist()
    {
        var result = _service.GetString("en-US", "nonexistent.key", "Default Value");

        Assert.Equal("Default Value", result);
    }

    [Fact]
    public void GetString_ReturnsFallbackForEmptyKey()
    {
        var result = _service.GetString("en-US", "", "Fallback");

        Assert.Equal("Fallback", result);
    }

    [Fact]
    public void GetString_FallsBackForUnknownLanguage()
    {
        var result = _service.GetString("fr-FR", "some.key", "Fallback");

        Assert.Equal("Fallback", result);
    }

    [Fact]
    public void ClearCache_WithNull_ClearsAllEntries()
    {
        _service.ClearCache(null);
        var result = _service.GetString("en-US", "test.key", "After");
        Assert.Equal("After", result);
    }

    [Fact]
    public void ClearCache_WithSpecificCode_ClearsOnlyThatLanguage()
    {
        _service.ClearCache("en-US");
        var result = _service.GetString("en-US", "test.key", "Cleared");
        Assert.Equal("Cleared", result);
    }

    [Fact]
    public void ClearCache_WithWhitespace_ClearsAllEntries()
    {
        _service.ClearCache("  ");
        var result = _service.GetString("ja-JP", "test.key", "AllCleared");
        Assert.Equal("AllCleared", result);
    }
}
