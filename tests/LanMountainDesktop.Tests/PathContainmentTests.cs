using LanMountainDesktop.Shared.IO;

using Xunit;

namespace LanMountainDesktop.Tests;

/// <summary>
/// "路径在不在那棵树里"这条判据的行为钉。收口前三份抄本（安装器一份、宿主两份，其中一份把判定内联在
/// 抛异常的外壳里、三个条件还换了顺序），压掉排版后逐字相同——这种判据漂开的症状是"包外的路径被当成包内"，
/// 不报错，所以值得钉住而不是只删抄本。
///
/// 每一格都对着一个具体的错法：共享前缀的兄弟目录（<c>C:\pkg</c> 与 <c>C:\pkg-other</c>）是"只比前缀"会错的方向；
/// 末尾分隔符是"不做 TrimEnd 就把自己认成自己的子树"的反面；根目录那一格钉的是"多剥一层就判不出孩子"；
/// 传空串钉的是"两侧都先绝对化"这个口径——它现在会抛，调用方（安装器与解包）得知道自己要接这个异常。
/// 大小写那一格是**如实记下现有政策**，不是认可它：非 Windows 文件系统上它会放宽，已挂 #G1-BI／#G1-BC 等拍板。
/// </summary>
public sealed class PathContainmentTests
{
    [Theory]
    [InlineData(@"C:\pkg", @"C:\pkg", true)]
    [InlineData(@"C:\pkg\", @"C:\pkg", true)]
    // 这一格钉不住"去掉 TrimEnd"那个注入：它的真值由 StartsWith(parent + 分隔符) 那条给，不经过 TrimEnd。
    // 别把它当成"末尾分隔符两个方向都覆盖了"（实测：去掉 TrimEnd 只红上一格与根目录那一格）。
    [InlineData(@"C:\pkg", @"C:\pkg\", true)]
    [InlineData(@"C:\pkg\entries", @"C:\pkg\entries\a.json", true)]
    [InlineData(@"C:\pkg", @"C:/pkg/entries/a.json", true)]
    [InlineData(@"C:\pkg", @"C:\pkg-other", false)]
    [InlineData(@"C:\pkg", @"C:\pkg2\a.json", false)]
    [InlineData(@"C:\pkg\entries", @"C:\pkg", false)]
    [InlineData(@"C:\", @"C:\Windows\explorer.exe", true)]
    [InlineData(@"C:\pkg", @"D:\pkg\a.json", false)]
    public void IsSameOrChild_MatchesTheConsolidatedJudgement(string parent, string child, bool expected)
    {
        Assert.Equal(expected, PathContainment.IsSameOrChild(parent, child));
    }

    [Fact]
    public void IsSameOrChild_ComparesCaseInsensitive_Today()
    {
        // 钉的是现状，不是赞同：Windows 上对，大小写敏感的文件系统上会放宽（文档里那条待拍板就是它）。
        Assert.True(PathContainment.IsSameOrChild(@"C:\PKG", @"C:\pkg\entries\a.json"));
    }

    [Fact]
    public void IsSameOrChild_EmptyParent_ThrowsInsteadOfReturningFalse()
    {
        Assert.Throws<ArgumentException>(() => PathContainment.IsSameOrChild(string.Empty, @"C:\pkg\a.json"));
    }
}
