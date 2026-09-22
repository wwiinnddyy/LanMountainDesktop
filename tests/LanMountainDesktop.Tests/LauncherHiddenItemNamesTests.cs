using LanMountainDesktop.Services;

using Xunit;

namespace LanMountainDesktop.Tests;

/// <summary>
/// 启动台隐藏项兜底显示名的家。设置页与桌面叠加层此前各抄一份逐字相同的 9 行：
/// 只改一份的结果是同一个隐藏项在两处显示成两个名字（都不报错，只是看起来像两个条目）。
/// </summary>
public sealed class LauncherHiddenItemNamesTests
{
    [Theory]
    [InlineData(@"C:\Start Menu\Games.lnk", "Games")]
    [InlineData("/usr/share/applications/foo.desktop", "foo")]
    [InlineData("Calendar", "Calendar")]
    [InlineData(null, "Unknown")]
    [InlineData("", "Unknown")]
    [InlineData("   ", "Unknown")]
    [InlineData("/", "/")]                 // 末段取不出名字：退回原键，别显示成空标签
    [InlineData(@"x\y\  ", @"x\y\  ")]     // 同上：尾部空白不算名字
    public void FallbackDisplayName_FollowsTheSingleRule(string? key, string expected) =>
        Assert.Equal(expected, LauncherHiddenItemNames.FallbackDisplayName(key));
}
