using LanMountainDesktop.Helpers;
using Xunit;

namespace LanMountainDesktop.Tests;

/// <summary>
/// 短文本归一化的口径：9 个组件过去各抄一份，现在只有一份，这份的行为得钉住。
/// </summary>
public sealed class CompactTextTests
{
    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData("\t\n", "")]
    [InlineData("  标题  ", "标题")]
    [InlineData("标题  两字", "标题 两字")]
    [InlineData("标题\n第二行", "标题 第二行")]
    [InlineData("标题\t　 第二行", "标题 第二行")]
    [InlineData("a b c", "a b c")]
    public void Normalize_FoldsAllWhitespaceIntoOneSpace(string? input, string expected)
    {
        Assert.Equal(expected, CompactText.Normalize(input));
    }

    [Fact]
    public void Normalize_KeepsSingleLineTextByteIdentical()
    {
        var oneLine = "东方财富 300059 涨 2.31%";
        Assert.Equal(oneLine, CompactText.Normalize(oneLine));
    }
}
