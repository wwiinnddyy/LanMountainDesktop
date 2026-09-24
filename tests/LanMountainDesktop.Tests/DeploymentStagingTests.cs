using System;
using System.IO;
using System.Linq;

using LanMountainDesktop.Shared.Contracts.Deployment;

using Xunit;

namespace LanMountainDesktop.Tests;

/// <summary>
/// "一次部署怎么开始"的判据钉：<see cref="DeploymentStaging.Prepare"/> 必须清空旧内容、
/// 重建目录，并且**当场**落下 <c>.partial</c>；<see cref="DeploymentStaging.MarkPartial"/> 只落标记、不动内容。
///
/// 为什么值得钉：读侧（启动器、宿主、安装器共 8 处）一律把"带 <c>.partial</c> 的部署目录"当成不存在。
/// 写侧漏落标记时没有任何东西会报错——半写完的部署会被挑成可用版本，症状是更新后起不来或起在缺文件的目录上。
/// 收口前宿主与安装器各抄一份逐字相同的 <c>PrepareTargetDirectory</c>（3 个调用点跨两个二进制）。
/// </summary>
public sealed class DeploymentStagingTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "deployment-staging-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Prepare_CreatesTheDirectory_AndMarksItPartial()
    {
        var target = Path.Combine(_root, "app-1.0.0-0");

        DeploymentStaging.Prepare(target);

        Assert.True(Directory.Exists(target));
        Assert.True(DeploymentStaging.IsPartial(target));
        var marker = Path.Combine(target, DeploymentLayout.PartialMarkerFileName);
        Assert.Equal(string.Empty, File.ReadAllText(marker));
    }

    [Fact]
    public void Prepare_DropsEverythingTheOldDeploymentHad()
    {
        var target = Path.Combine(_root, "app-0.9.0-0");
        Directory.CreateDirectory(Path.Combine(target, "sub"));
        File.WriteAllText(Path.Combine(target, "stale.dll"), "old");

        DeploymentStaging.Prepare(target);

        // 旧内容一件都不留，留下的只有那次"未完成"标记本身。
        Assert.False(File.Exists(Path.Combine(target, "stale.dll")));
        Assert.False(Directory.Exists(Path.Combine(target, "sub")));
        Assert.Equal(
            new[] { DeploymentLayout.PartialMarkerFileName },
            Directory.GetFileSystemEntries(target, "*", SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileName)
                .ToArray());
        Assert.True(DeploymentStaging.IsPartial(target));
    }

    [Fact]
    public void MarkPartial_KeepsExistingContent_UnlikePrepare()
    {
        var target = Path.Combine(_root, "app-1.1.0-0");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "payload.bin"), "kept");

        DeploymentStaging.MarkPartial(target);

        Assert.True(DeploymentStaging.IsPartial(target));
        Assert.Equal("kept", File.ReadAllText(Path.Combine(target, "payload.bin")));
    }

    [Fact]
    public void IsPartial_IsFalse_ForAFinishedDeployment()
    {
        var target = Path.Combine(_root, "app-1.2.0-0");
        Directory.CreateDirectory(target);

        Assert.False(DeploymentStaging.IsPartial(target));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }
}
