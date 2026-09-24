using System.IO;

namespace LanMountainDesktop.Shared.Contracts.Deployment;

/// <summary>
/// "开始往一个部署目录里写"这一步只认这一处：重置目录，并当场落下 <see cref="DeploymentLayout.PartialMarkerFileName"/>。
///
/// 为什么要收成一家：读侧三处（Core 的 <c>AppVersionProvider</c>、宿主的 <c>AppDeploymentLocator</c>、
/// PLONDS 安装期的候选枚举）都是同一个判据——"带 <c>.partial</c> 的部署目录等于不存在"。
/// 这条不变量只要有一个写侧忘了落标记就整个失效：半写完的部署会被启动器当成一个可用版本挑中，
/// 症状是"更新后启动即崩"或"起在一个缺文件的老目录上"，而写侧自己一路顺利，没有任何报错。
/// 收口前宿主的 <c>PlondsPreparedPackageInstaller</c>（2 个调用点）与安装器的 <c>FilesPackageInstaller</c>
/// （1 个调用点）各抄了一份逐字相同的 9 行（2026-09-24 由普查尺子量出）。
/// </summary>
public static class DeploymentStaging
{
    /// <summary>
    /// 把目标部署目录清空（含旧内容，目录不存在则跳过）后重建，并写入未完成的 <c>.partial</c> 标记。
    /// 摘标记那一半各部署路径自己负责（宿主与安装器写法不同，还没并），但落标记只有这里。
    /// </summary>
    public static void Prepare(string targetDeploymentDirectory)
    {
        if (Directory.Exists(targetDeploymentDirectory))
        {
            Directory.Delete(targetDeploymentDirectory, recursive: true);
        }

        Directory.CreateDirectory(targetDeploymentDirectory);
        File.WriteAllText(
            Path.Combine(targetDeploymentDirectory, DeploymentLayout.PartialMarkerFileName),
            string.Empty);
    }

    /// <summary>
    /// 只落"未完成"标记、不动目录里已有的内容（断点续写那类路径用这条）。
    /// 摘标记仍然是各部署路径自己的事（宿主、启动器、安装器三处写法各不相同，还没并）。
    /// </summary>
    public static void MarkPartial(string deploymentDirectory) =>
        File.WriteAllText(
            Path.Combine(deploymentDirectory, DeploymentLayout.PartialMarkerFileName),
            string.Empty);

    /// <summary>这个部署目录是不是"还没写完"（读侧一律据此跳过）。</summary>
    public static bool IsPartial(string deploymentDirectory) =>
        File.Exists(Path.Combine(deploymentDirectory, DeploymentLayout.PartialMarkerFileName));
}
