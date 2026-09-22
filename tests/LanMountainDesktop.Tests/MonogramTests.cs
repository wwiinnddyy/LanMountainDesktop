using LanMountainDesktop.Services;

using Xunit;

namespace LanMountainDesktop.Tests;

/// <summary>
/// 头像占位字规则的家。这些用例钉的是"同一个人/同一个磁贴显示成什么"——
/// 用户头像与启动台磁贴此前各算各的，规则改动只要漏一边就会出现"同一个名字两种缩写"。
/// </summary>
public sealed class MonogramTests
{
    [Theory]
    [InlineData(null, "?")]
    [InlineData("", "?")]
    [InlineData("   ", "?")]
    [InlineData("Lincube", "L")]
    [InlineData("lin cu", "LC")]
    [InlineData("  ada  lovelace  ", "AL")]
    [InlineData("grace hopper burnell", "GH")] // 只取两个字母，第三段被丢掉
    [InlineData("每日一词", "每")]
    [InlineData("wiiin ddy", "WD")]
    public void From_FollowsTheSingleRule(string? text, string expected) =>
        Assert.Equal(expected, Monogram.From(text));
}
