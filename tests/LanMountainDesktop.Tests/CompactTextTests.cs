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

    /// <summary>
    /// 收口前 <c>Truncate</c> 抄了 4 份，GitHub 更新服务那一份不带省略号——
    /// 裁过的 HTTP 错误体和完整回复长得一样，排查时会误以为拿到了全部响应。这条钉住"裁过必须看得出来"。
    /// </summary>
    [Theory]
    [InlineData(null, 10, "")]
    [InlineData("", 10, "")]
    [InlineData("short", 10, "short")]
    [InlineData("exactly1", 8, "exactly1")]
    [InlineData("0123456789abc", 8, "01234567...")]
    public void Truncate_MarksWhereItCut(string? input, int maxLength, string expected)
    {
        Assert.Equal(expected, CompactText.Truncate(input, maxLength));
    }

    [Fact]
    public void Truncate_KeepsTheHeadOfLongErrorBodies()
    {
        var body = new string('x', 400);

        var truncated = CompactText.Truncate(body, 180);

        Assert.StartsWith(new string('x', 180), truncated, StringComparison.Ordinal);
        Assert.EndsWith("...", truncated, StringComparison.Ordinal);
    }
}
