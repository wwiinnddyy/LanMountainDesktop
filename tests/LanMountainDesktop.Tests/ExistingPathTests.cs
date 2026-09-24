using System;
using System.IO;

using LanMountainDesktop.Shared.IO;

using Xunit;

namespace LanMountainDesktop.Tests;

/// <summary>
/// "路径存在才给全路径，否则 null"这条判据的家唯一的证据（Core 与宿主共用，10 个调用点）。
/// 两份逐字相同的实现漂开的后果不是崩，而是同一个安装被两个进程判成不同结论——
/// 比如启动器认为部署目录有效，宿主重启时却认不出它。
/// </summary>
public sealed class ExistingPathTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "existing-path-" + Guid.NewGuid().ToString("N"));

    public ExistingPathTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void ExistingDirectory_ReturnsAComparableFullPath()
    {
        var full = Path.GetFullPath(_root);

        Assert.Equal(full, ExistingPath.DirectoryOrNull(full));
        // `.`/相对形状会被 GetFullPath 归一，所以不同写法算得出同一个可比较的串——<b>但只在同一个盘内成立</b>。
        // 2026-09-25 CI 首次真跑用例时这条红了（Expected 全路径、Actual null）：CI 的工作目录在 D:、
        // 临时目录在 C:，`Path.GetRelativePath` 拼出的 `.\..\Users\...` 经 GetFullPath 会落到
        // D:\Users\runneradmin\... 那一边，那个目录本来就不存在——量的是相对归一，不该量跨盘拼接。
        // 所以相对那一支的探针放在工作目录下，保证相对串必然算回同一个绝对位置。
        var relative = Path.Combine("existing-path-relative", Guid.NewGuid().ToString("N"));
        var relativeFull = Path.GetFullPath(relative);
        Directory.CreateDirectory(relativeFull);
        try
        {
            Assert.Equal(relativeFull, ExistingPath.DirectoryOrNull(
                "." + Path.DirectorySeparatorChar + relative));
        }
        finally
        {
            Directory.Delete(relativeFull);
            Directory.Delete(Path.GetDirectoryName(relativeFull)!);
        }

        // 钉住**现状**（两份被收口的实现都是这个行为，收口没改语义）：尾部带分隔符时原样保留，
        // 于是 "C:\dir\" 与 "C:\dir" 算出来是两个不同的串——拿返回值再做相等比较的调用方要注意这一点
        // （已另立待办，改它等于改 10 个调用点的比较结果）。
        Assert.NotEqual(full, ExistingPath.DirectoryOrNull(full + Path.DirectorySeparatorChar));
    }

    [Fact]
    public void ExistingFile_ReturnsTheFullPath_AndKindsDoNotCross()
    {
        var file = Path.Combine(_root, "version.json");
        File.WriteAllText(file, "{}");

        Assert.Equal(file, ExistingPath.FileOrNull(file));
        Assert.Null(ExistingPath.DirectoryOrNull(file)); // 是文件，不是目录
        Assert.Null(ExistingPath.FileOrNull(_root));     // 是目录，不是文件
    }

    [Fact]
    public void MissingPaths_ReturnNull()
    {
        Assert.Null(ExistingPath.DirectoryOrNull(Path.Combine(_root, "nope")));
        Assert.Null(ExistingPath.FileOrNull(Path.Combine(_root, "nope.json")));
    }

    [Fact]
    public void EmptyAndUnparseable_ReturnNullInsteadOfThrowing()
    {
        // 调用方全是"有就用、没有就走兜底"，这里抛出会把一次重启变成一次崩溃。
        Assert.Null(ExistingPath.DirectoryOrNull(null));
        Assert.Null(ExistingPath.DirectoryOrNull("   "));
        Assert.Null(ExistingPath.FileOrNull(null));
        Assert.Null(ExistingPath.FileOrNull("\0\0\0"));
        Assert.Null(ExistingPath.DirectoryOrNull("\0\0\0"));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
