using LanMountainDesktop.Shared.IO;

using Xunit;

namespace LanMountainDesktop.Tests;

/// <summary>
/// 路径段压形的形状钉。非法字符集本身平台相关（<c>Path.GetInvalidFileNameChars()</c>），
/// 所以只用**两个平台都算非法**的 <c>/</c> 钉形状——用反斜杠会在 Linux 上变成合法字符，
/// 门的红绿不该取决于跑在哪台机器上。
///
/// 三条判据各能被单独注入打到：换成 <c>_</c>（不是删掉：删了 <c>a/b</c> 与 <c>ab</c> 撞成同一段，
/// 磁盘上两个来源盖住彼此）、两端 <c>Trim</c>、压空了才给兜底词。兜底词由调用方给
/// （包目录 <c>unknown</c>、版本段 <c>0.0.0</c>），这一家不替它们统一策略。
/// </summary>
public sealed class PathSegmentSanitizerTests
{
    [Theory]
    [InlineData("a/b", "a_b", "unknown")]
    [InlineData("  padded  ", "padded", "unknown")]
    [InlineData("/", "_", "unknown")]
    [InlineData("  /  ", "_", "unknown")]
    [InlineData("", "unknown", "unknown")]
    [InlineData("   ", "0.0.0", "0.0.0")]
    public void Sanitize_KeepsTheThreeJudgementsSeparateAndVisible(string value, string expected, string fallback)
    {
        Assert.Equal(expected, PathSegmentSanitizer.Sanitize(value, fallback));
    }

    [Fact]
    public void Sanitize_OnlyFallsBackWhenNothingIsLeft()
    {
        // "///" 压完是 "___"：那不是"什么都不剩"，不许落到兜底词。
        Assert.Equal("___", PathSegmentSanitizer.Sanitize("///", "unknown"));
    }

    [Fact]
    public void Sanitize_MapsControlCharsBeforeTrimming_SoTheyNeverReachTheFallback()
    {
        // 制表与换行在 Windows 上也是非法字符：先被换成两个下划线，Trim 就剪不到它们了。
        // 顺序（先换后剪）本身就是判据——倒过来写这两格会红。
        var controlChars = new string((char)9, 1) + (char)10;

        Assert.Equal("__", PathSegmentSanitizer.Sanitize(controlChars, "0.0.0"));
    }
}
