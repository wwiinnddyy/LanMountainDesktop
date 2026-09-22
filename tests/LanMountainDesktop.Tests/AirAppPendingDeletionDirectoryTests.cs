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
