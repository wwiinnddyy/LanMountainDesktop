using System.Runtime.InteropServices;

using LanMountainDesktop.Services;

using Xunit;

namespace LanMountainDesktop.Tests;

/// <summary>
/// 平台标识这套 token 的行为钉。它是**对外契约**而不是内部命名：发布资产名里就写着
/// <c>files-windows-x64.zip</c>，所以"全小写、认不出的架构落到 <c>x64</c>、os 与架构之间那根连字符"
/// 三条都是"清单能不能对上"的前提——漂移了不会报错，只会挑不到安装包（或算出一个没人发布的键）。
///
/// 钉得住什么、钉不住什么都写清：架构那段是真值表（五格，含"没列出的架构走默认档"），
/// 把 <c>Arm64</c> 与 <c>X86</c> 的返回值对调会红两格；平台标识那两格钉的是拼接形状（去掉连字符、
/// 或把 token 写成 <c>amd64</c>／<c>x86_64</c> 都红）。钉不住的是"这台机器到底算成哪个 os"——
/// 那要跨三个系统跑同一个测试才量得到，离线门里只判形状，不宣称已覆盖三平台。
/// </summary>
public sealed class PlatformIdentifiersTests
{
    [Theory]
    [InlineData(Architecture.X64, "x64")]
    [InlineData(Architecture.X86, "x86")]
    [InlineData(Architecture.Arm64, "arm64")]
    [InlineData(Architecture.Arm, "x64")]
    [InlineData((Architecture)99, "x64")]
    public void ArchitectureToken_UsesThePublishedTokens(Architecture architecture, string expected)
    {
        Assert.Equal(expected, PlatformIdentifiers.ArchitectureToken(architecture));
    }

    [Fact]
    public void CurrentPlatformId_IsLowercaseOsDashArchitecture()
    {
        var id = PlatformIdentifiers.CurrentPlatformId;

        Assert.Matches("^(windows|linux|macos|unknown)-(x64|arm64|x86)$", id);
        Assert.DoesNotContain("amd64", id);
        Assert.DoesNotContain("x86_64", id);
    }

    [Fact]
    public void CurrentPlatformId_ArchitecturePartMatchesTheSharedToken()
    {
        var parts = PlatformIdentifiers.CurrentPlatformId.Split('-');

        Assert.Equal(2, parts.Length);
        Assert.Equal(PlatformIdentifiers.CurrentArchitectureToken, parts[1]);
    }
}
