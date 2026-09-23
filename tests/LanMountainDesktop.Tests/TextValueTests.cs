using LanMountainDesktop.Shared.Text;

using Xunit;

namespace LanMountainDesktop.Tests;

/// <summary>
/// "一串候选里取第一个真有内容的"这条判据。收口前它在两个二进制里各抄一份逐字相同的 8 行，
/// 漂开的症状不是崩，而是同一份清单在启动器与宿主眼里"哪个字段算有值"不一致。
/// </summary>
public sealed class TextValueTests
{
    [Fact]
    public void FirstNonEmpty_ReturnsTheFirstCandidateWithContent()
    {
        Assert.Equal("second", TextValue.FirstNonEmpty(null, "", "  ", "second", "third"));
    }

    /// <summary>整串空格算"没有值"——这条就是当初两份抄本最容易各自漂开的那一格。</summary>
    [Fact]
    public void FirstNonEmpty_TreatsWhitespaceOnlyAsNoContent()
    {
        Assert.Equal("x", TextValue.FirstNonEmpty("   ", "\t", "x"));
        Assert.Null(TextValue.FirstNonEmpty(" ", "\n\t ", null));
    }

    [Fact]
    public void FirstNonEmpty_GivesUpQuietly_WhenNothingIsUsable()
    {
        Assert.Null(TextValue.FirstNonEmpty());
        Assert.Null(TextValue.FirstNonEmpty((string?[])[]));
    }

    /// <summary>返回的是原串，不 Trim：调用方要的是"这个字段的原值"，不是它的整洁版。</summary>
    [Fact]
    public void FirstNonEmpty_KeepsTheChosenValueAsIs()
    {
        Assert.Equal("  padded  ", TextValue.FirstNonEmpty("  padded  "));
    }
}
