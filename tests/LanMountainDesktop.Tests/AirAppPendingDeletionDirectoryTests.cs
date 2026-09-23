using System;
using System.IO;

using LanMountainDesktop.AirAppPackaging;

using Xunit;

namespace LanMountainDesktop.Tests;

/// <summary>
/// 安装期待删除包暂存目录的收尾规则，家唯一的证据。
/// 收口前有三份实现，而且**做的不是同一件事**：Core 与启动器只删 <c>*.pending</c> 标记，
/// 宿主还多一段"目录空了就删掉目录"。这条判据现在钉的是超集那版，三份抄本共用一套。
/// </summary>
public sealed class AirAppPendingDeletionDirectoryTests : IDisposable
{
    private readonly string _airAppsDirectory =
        Path.Combine(Path.GetTempPath(), "pending-deletions-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void PathFor_UsesTheSingleDirectoryName()
    {
        Assert.Equal(
            Path.Combine(_airAppsDirectory, AirAppPackagingConstants.PendingDeletionDirectoryName),
            AirAppPendingDeletionDirectory.PathFor(_airAppsDirectory));
    }

    /// <summary>
    /// 删得动的情形：文件当场消失，且<b>不该</b>留下 <c>*.pending</c>。
    /// 钉的是"挪进暂存"只是兜底而不是常规路径——写成"总是挪"的话，装一次就在这个隐藏目录里
    /// 攒一份旧包副本，而 <c>CleanupAfterInstall</c> 下一轮才收，中间那段时间它照样占着磁盘。
    /// </summary>
    [Fact]
    public void RemoveOrMoveToPending_DeletesTheStalePackage_InPlace()
    {
        var dir = Prepare(out _, out _, markerCount: 0, keepOther: false);
        var packagePath = Path.Combine(_airAppsDirectory, "stale" + AirAppPackagingConstants.PackageFileExtension);
        File.WriteAllText(packagePath, "old package");

        AirAppPendingDeletionDirectory.RemoveOrMoveToPending(packagePath, dir, "tests");

        Assert.False(File.Exists(packagePath));
        Assert.Empty(Directory.GetFiles(dir, "*.pending"));
    }

    /// <summary>
    /// **未覆盖的一支**：兜底那条"删不动就挪进来 <c>*.pending</c>"在这里量不到——同一进程用
    /// <c>FileShare.None</c> 占住文件确实能让 <c>File.Delete</c> 抛 <c>IOException</c>，
    /// 但紧接着的 <c>File.Move</c> 会撞上同一个锁（没开 <c>FILE_SHARE_DELETE</c> 就改不了名），
    /// 于是这条夹具测的是"挪也失败"，不是"挪成功"。别把它当成那一支已经钉住了。
    /// </summary>
    [Fact]
    public void RemoveOrMoveToPending_KeepsQuiet_WhenThePackageIsAlreadyGone()
    {
        var dir = Prepare(out _, out _, markerCount: 0, keepOther: false);

        AirAppPendingDeletionDirectory.RemoveOrMoveToPending(
            Path.Combine(_airAppsDirectory, "already-gone" + AirAppPackagingConstants.PackageFileExtension),
            dir,
            "tests");

        Assert.Empty(Directory.GetFiles(dir, "*.pending"));
    }

    [Fact]
    public void Cleanup_DeletesMarkers_AndPrunesTheNowEmptyDirectory()
    {
        var dir = Prepare(out _, out _, markerCount: 2, keepOther: false);

        AirAppPendingDeletionDirectory.CleanupAfterInstall(dir);

        Assert.False(Directory.Exists(dir));
    }

    [Fact]
    public void Cleanup_KeepsTheDirectory_WhileSomethingElseIsStillInIt()
    {
        var dir = Prepare(out _, out var otherPath, markerCount: 1, keepOther: true);

        AirAppPendingDeletionDirectory.CleanupAfterInstall(dir);

        Assert.True(Directory.Exists(dir));
        Assert.False(File.Exists(dir + Path.DirectorySeparatorChar + "a.pending"));
        Assert.True(File.Exists(otherPath)); // 不是 *.pending 的东西一律不碰：那可能是别人还在用的暂存内容
    }

    [Fact]
    public void Cleanup_IsSafe_WhenTheDirectoryWasNeverCreated()
    {
        var dir = Path.Combine(_airAppsDirectory, AirAppPackagingConstants.PendingDeletionDirectoryName);
        Assert.False(Directory.Exists(dir));

        AirAppPendingDeletionDirectory.CleanupAfterInstall(
            AirAppPendingDeletionDirectory.PathFor(_airAppsDirectory));
    }

    private string Prepare(out string markerPaths, out string otherPath, int markerCount, bool keepOther)
    {
        var dir = Path.Combine(_airAppsDirectory, AirAppPackagingConstants.PendingDeletionDirectoryName);
        Directory.CreateDirectory(dir);

        var names = new string[markerCount];
        for (var index = 0; index < markerCount; index++)
        {
            names[index] = Path.Combine(dir, $"{(char)('a' + index)}.pending");
            File.WriteAllText(names[index], "moved");
        }

        markerPaths = string.Join(";", names);
        otherPath = Path.Combine(dir, "in-use.tmp");
        if (keepOther)
        {
            File.WriteAllText(otherPath, "still needed");
        }

        return dir;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_airAppsDirectory, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
