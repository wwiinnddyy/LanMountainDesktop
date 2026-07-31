using System;
using System.Globalization;
using System.IO;

namespace LanMountainDesktop.Shared.Contracts.Deployment;

/// <summary>
/// 部署目录布局的单一权威约定。
/// 安装器（LanDesktopPLONDS.installer）与启动器（LanMountainDesktop.Launcher）
/// 必须共同引用本类，禁止在任何一侧硬编码这些标记文件名或目录前缀。
/// </summary>
public static class DeploymentLayout
{
    /// <summary>部署目录前缀，完整形式为 app-{version}-{index}。</summary>
    public const string DeploymentDirectoryPrefix = "app-";

    /// <summary>当前活动部署标记文件。</summary>
    public const string CurrentMarkerFileName = ".current";

    /// <summary>未完成（复制中/失败）部署标记文件。</summary>
    public const string PartialMarkerFileName = ".partial";

    /// <summary>待清理部署标记文件。</summary>
    public const string DestroyMarkerFileName = ".destroy";

    /// <summary>Launcher 状态目录名。</summary>
    public const string LauncherStateDirectoryName = ".Launcher";

    /// <summary>主程序可执行文件名（不含扩展名）。</summary>
    public const string HostExecutableBaseName = "LanMountainDesktop";

    /// <summary>启动器可执行文件名（不含扩展名）。</summary>
    public const string LauncherExecutableBaseName = "LanMountainDesktop.Launcher";

    /// <summary>获取当前平台的主程序可执行文件名。</summary>
    public static string GetHostExecutableName() =>
        OperatingSystem.IsWindows() ? HostExecutableBaseName + ".exe" : HostExecutableBaseName;

    /// <summary>获取当前平台的启动器可执行文件名。</summary>
    public static string GetLauncherExecutableName() =>
        OperatingSystem.IsWindows() ? LauncherExecutableBaseName + ".exe" : LauncherExecutableBaseName;

    /// <summary>
    /// 生成不冲突的部署目录路径（app-{version}-{index}，index 递增直到不存在）。
    /// </summary>
    public static string BuildDeploymentDirectory(string launcherRoot, string version)
    {
        var sanitized = string.IsNullOrWhiteSpace(version) ? "0.0.0" : version.Trim();
        var index = 0;
        while (true)
        {
            var candidate = Path.Combine(
                launcherRoot,
                $"{DeploymentDirectoryPrefix}{sanitized}-{index.ToString(CultureInfo.InvariantCulture)}");
            if (!Directory.Exists(candidate))
            {
                return candidate;
            }

            index++;
        }
    }

    /// <summary>判断相对路径是否为部署标记文件。</summary>
    public static bool IsDeploymentMarker(string relativePath)
    {
        var name = Path.GetFileName(relativePath);
        return name is CurrentMarkerFileName or PartialMarkerFileName or DestroyMarkerFileName;
    }

    /// <summary>判断目录名是否为部署目录（app- 前缀）。</summary>
    public static bool IsDeploymentDirectoryName(string directoryName) =>
        directoryName.StartsWith(DeploymentDirectoryPrefix, StringComparison.OrdinalIgnoreCase);
}
