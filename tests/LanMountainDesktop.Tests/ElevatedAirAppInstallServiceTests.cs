using System.IO;
using LanMountainDesktop.Services;
using Xunit;

namespace LanMountainDesktop.Tests;

/// <summary>
/// 提升安装进程的启动工作目录解析。
/// 原先这个文件里的三条测试都只是在断言 Path.GetDirectoryName 自己的返回值，
/// 从不触碰生产代码（其中两条期望值还写错了：只有文件名的路径返回空串，不是 null），
/// 因此既没有判别力也永远修不好。现在全部改为调 ElevatedAirAppInstallService 的真实实现。
/// </summary>
public sealed class ElevatedAirAppInstallServiceTests
{
    [Theory]
    [InlineData("installer.exe")]
    [InlineData("LanMountainDesktop.Launcher.exe")]
    public void FileNameOnlyPath_FallsBackToBaseDirectory(string launcherFileName)
    {
        var workingDirectory = ElevatedAirAppInstallService.ResolveLauncherWorkingDirectory(launcherFileName);

        Assert.Equal(AppContext.BaseDirectory, workingDirectory);
        Assert.True(Directory.Exists(workingDirectory));
    }

    [Fact]
    public void NullOrEmptyLauncherPath_FallsBackToBaseDirectory()
    {
        Assert.Equal(AppContext.BaseDirectory, ElevatedAirAppInstallService.ResolveLauncherWorkingDirectory(string.Empty));
    }

    [Fact]
    public void RootedLauncherPath_UsesItsOwnDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "LanMountainDesktop.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var launcherPath = Path.Combine(directory, "LanMountainDesktop.Launcher.exe");

            Assert.Equal(directory, ElevatedAirAppInstallService.ResolveLauncherWorkingDirectory(launcherPath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void LauncherFileDirectlyUnderFileSystemRoot_KeepsTheRootAsWorkingDirectory()
    {
        var root = Path.GetPathRoot(Path.GetTempPath());
        Assert.False(string.IsNullOrEmpty(root));

        var launcherPath = Path.Combine(root!, "installer.exe");
        var workingDirectory = ElevatedAirAppInstallService.ResolveLauncherWorkingDirectory(launcherPath);

        Assert.Equal(root, workingDirectory);
        Assert.True(Directory.Exists(workingDirectory));
    }
}
