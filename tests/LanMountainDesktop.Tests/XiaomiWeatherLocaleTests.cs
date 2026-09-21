using LanMountainDesktop.Services;
using Xunit;

namespace LanMountainDesktop.Tests;

/// <summary>
/// 天气接口 locale 参数是供应商自己的拼法，也是设置页与桌面组件共用的那份真源。
/// </summary>
public sealed class XiaomiWeatherLocaleTests
{
    [Theory]
    [InlineData("en-US", "en_us")]
    [InlineData("en-us", "en_us")]
    [InlineData("EN-US", "en_us")]
    [InlineData("zh-CN", "zh_cn")]
    [InlineData("ja-JP", "zh_cn")]
    [InlineData("ko-KR", "zh_cn")]
    [InlineData("en", "zh_cn")]
    [InlineData(null, "zh_cn")]
    public void ForLanguageCode_MapsOntoTheProviderVocabulary(string? languageCode, string expected)
    {
        Assert.Equal(expected, XiaomiWeatherLocales.ForLanguageCode(languageCode));
    }
}
