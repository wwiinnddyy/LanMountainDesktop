using LanMountainDesktop.Shared.Contracts.Deployment;

namespace LanDesktopPLONDS.Installer.Services;

/// <summary>
/// 检测已安装产品的信息：扫描部署目录，读取 .current 标记，
/// 从目录名（app-{version}-{index}）中解析 SemanticVersion。
/// </summary>
internal sealed class InstalledProductInspector
{
    /// <summary>
    /// 检测指定安装根目录下的已安装产品。
    /// </summary>
    /// <param name="launcherRoot">
    /// 启动器根目录（包含 app-* 子目录的父目录）。
    /// </param>
    /// <returns>
    /// 如果找到有效的已安装产品则返回 <see cref="InstalledProductInfo"/>，否则返回 null。
    /// </returns>
    public InstalledProductInfo? Detect(string launcherRoot)
    {
        if (string.IsNullOrWhiteSpace(launcherRoot) || !Directory.Exists(launcherRoot))
        {
            return null;
        }

        // 扫描所有部署目录（app-* 前缀）
        var candidates = Directory.GetDirectories(launcherRoot, DeploymentLayout.DeploymentDirectoryPrefix + "*", SearchOption.TopDirectoryOnly);

        InstalledProductInfo? best = null;

        foreach (var dir in candidates)
        {
            var dirName = Path.GetFileName(dir);

            // 跳过被标记为销毁或部分完成的部署
            if (File.Exists(Path.Combine(dir, DeploymentLayout.DestroyMarkerFileName)) ||
                File.Exists(Path.Combine(dir, DeploymentLayout.PartialMarkerFileName)))
            {
                continue;
            }

            // 从目录名解析版本号：app-{version}-{index} → 提取 version 部分
            var version = ParseVersionFromDirectoryName(dirName);
            if (version is null)
            {
                continue;
            }

            var hasCurrent = File.Exists(Path.Combine(dir, DeploymentLayout.CurrentMarkerFileName));

            // 优先选择 .current 标记的部署；相同标记下选最新版本
            if (best is null ||
                (hasCurrent && !best.HasCurrentMarker) ||
                (hasCurrent == best.HasCurrentMarker && version > best.Version))
            {
                best = new InstalledProductInfo(
                    Version: version,
                    DeploymentPath: dir,
                    HasCurrentMarker: hasCurrent);
            }
        }

        return best;
    }

    /// <summary>
    /// 从部署目录名中解析语义版本号。
    /// 目录名格式：app-{version}-{index}，其中 version 可能包含预发布标签。
    /// </summary>
    internal static SemanticVersion? ParseVersionFromDirectoryName(string directoryName)
    {
        if (string.IsNullOrWhiteSpace(directoryName))
        {
            return null;
        }

        // 去掉 "app-" 前缀
        if (!directoryName.StartsWith(DeploymentLayout.DeploymentDirectoryPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var withoutPrefix = directoryName[DeploymentLayout.DeploymentDirectoryPrefix.Length..];

        // 分割为 [version, index] — index 是最后一个 "-{n}" 段
        // version 本身可能包含预发布标签（如 1.2.3-beta.1），需要正确处理
        var lastDash = withoutPrefix.LastIndexOf('-');
        if (lastDash <= 0)
        {
            // 没有找到 "-" 分隔符，尝试将整个部分作为版本
            return SemanticVersion.TryParse(withoutPrefix, out var sv) ? sv : null;
        }

        var versionPart = withoutPrefix[..lastDash];
        return SemanticVersion.TryParse(versionPart, out var parsed) ? parsed : null;
    }
}

/// <summary>
/// 已安装产品的信息。
/// </summary>
internal sealed record InstalledProductInfo(
    SemanticVersion Version,
    string DeploymentPath,
    bool HasCurrentMarker);
