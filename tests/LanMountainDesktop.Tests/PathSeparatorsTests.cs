using LanMountainDesktop.Shared.IO;
using Xunit;

namespace LanMountainDesktop.Tests;

/// <summary>
/// 目录末尾分隔符的补齐口径（收口前在 Core、宿主、启动器三个二进制里各抄了一份，共 4 份）。
/// 第二条钉的是那份漂移：<c>AirAppLoader</c> 的版本会顺手 <c>Path.GetFullPath</c>，
/// 绝对化是调用点的语义，不该塞进这个 helper。
/// </summary>
public sealed class PathSeparatorsTests
{
    [Theory]
    [InlineData(@"C:\apps", @"C:\apps" + "\\")]
    [InlineData(@"C:\apps\", @"C:\apps" + "\\")]
    [InlineData("", "\\")]
    public void EnsuresExactlyOneTrailingSeparator(string input, string expected)
    {
        Assert.Equal(expected.Replace('\\', Path.DirectorySeparatorChar), PathSeparators.EnsureTrailingSeparator(input));
    }

    [Fact]
    public void DoesNotAbsolutizeThePath()
    {
        var relative = Path.Combine("airapp", "runtime");

        var result = PathSeparators.EnsureTrailingSeparator(relative);

        Assert.Equal(relative + Path.DirectorySeparatorChar, result);
        Assert.False(Path.IsPathRooted(result));
    }

    [Fact]
    public void IsIdempotent()
    {
        var once = PathSeparators.EnsureTrailingSeparator("runtime");

        Assert.Equal(once, PathSeparators.EnsureTrailingSeparator(once));
    }
}
