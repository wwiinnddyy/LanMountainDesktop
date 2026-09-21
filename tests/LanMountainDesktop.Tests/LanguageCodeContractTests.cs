using LanMountainDesktop.Shared.Contracts.Localization;
using Xunit;

namespace LanMountainDesktop.Tests;

/// <summary>
/// 语言码契约的行为：宿主、启动器与时钟 AirApp 现在共用这一张表，
/// 所以"同一个 settings.json 值被判成不同语言"这类问题只能靠这里钉住。
/// </summary>
public sealed class LanguageCodeContractTests
{
    [Theory]
    [InlineData(null, "zh-CN")]
    [InlineData("", "zh-CN")]
    [InlineData("   ", "zh-CN")]
    [InlineData("zh-CN", "zh-CN")]
    [InlineData("zh", "zh-CN")]
    [InlineData("en", "en-US")]
    [InlineData("en-US", "en-US")]
    [InlineData("EN-us", "en-US")]
    [InlineData(" en-US ", "en-US")]
    [InlineData("ja", "ja-JP")]
    [InlineData("JA-JP", "ja-JP")]
    [InlineData("ko", "ko-KR")]
    [InlineData("ko-kr", "ko-KR")]
    [InlineData("fr-FR", "zh-CN")]
    [InlineData("klingon", "zh-CN")]
    public void Normalize_FoldsOntoTheSupportedSet(string? input, string expected)
    {
        Assert.Equal(expected, LanguageCodes.Normalize(input));
    }

    [Fact]
    public void Normalize_AlwaysReturnsASupportedCode()
    {
        foreach (var input in new[] { "en", "EN_GB", "ja-JP", "zh", "pirate", "", null })
        {
            Assert.Contains(LanguageCodes.Normalize(input), LanguageCodes.Supported);
        }
    }

    [Fact]
    public void Supported_MatchesTheLocaleFilesShippedWithTheHost()
    {
        // 词表文件名是这条契约的另一半：加了语言码却忘了加词表，界面会静默全走英文兜底。
        var localizationDirectory = Path.Combine(RepoRoot, "desktop", "LanMountainDesktop", "Localization");
        foreach (var code in LanguageCodes.Supported)
        {
            Assert.True(
                File.Exists(Path.Combine(localizationDirectory, code + ".json")),
                $"语言码 {code} 在 {code}.json 里没有对应词表");
        }
    }

    [Fact]
    public void Default_IsChinese()
    {
        Assert.Equal(LanguageCodes.Chinese, LanguageCodes.Default);
        Assert.True(LanguageCodes.IsChinese("ZH_cn"));
        Assert.True(LanguageCodes.IsChinese(null));
    }

    [Fact]
    public void IsEnglishCode_ComparesLiterallyAndDoesNotFoldShortCodes()
    {
        Assert.True(LanguageCodes.IsEnglishCode("en-us"));
        Assert.False(
            LanguageCodes.IsEnglishCode("en"),
            "逐字判据故意不折叠简写：天气那三处一直是这个口径，收口时没顺手改成归一化");
    }

    private static string RepoRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "LanMountainDesktop.slnx")))
            {
                directory = directory.Parent;
            }

            return directory?.FullName ?? AppContext.BaseDirectory;
        }
    }
}
