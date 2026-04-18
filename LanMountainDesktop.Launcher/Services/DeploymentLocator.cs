using System.Globalization;
using System.Text.Json;
using LanMountainDesktop.Launcher.Models;
using LanMountainDesktop.Shared.Contracts.Launcher;

namespace LanMountainDesktop.Launcher.Services;

internal sealed class DeploymentLocator
{
    private readonly string _appRoot;

    public DeploymentLocator(string appRoot)
    {
        _appRoot = appRoot;
    }

    public string GetAppRoot() => _appRoot;

    public string? FindCurrentDeploymentDirectory()
    {
        Console.WriteLine("[DeploymentLocator] Searching for deployment directories (ClassIsland style)...");

        if (!Directory.Exists(_appRoot))
        {
            Console.WriteLine("[DeploymentLocator] App root directory does not exist");
            return null;
        }

        var executable = OperatingSystem.IsWindows() ? "LanMountainDesktop.exe" : "LanMountainDesktop";

        try
        {
            var candidates = Directory.GetDirectories(_appRoot, "app-*", SearchOption.TopDirectoryOnly);
            Console.WriteLine($"[DeploymentLocator] Found {candidates.Length} app-* directories");

            // ClassIsland 风格的查询：先筛选，后排序
            var validInstallations = candidates
                .Where(path =>
                {
                    var hasDestroy = File.Exists(Path.Combine(path, ".destroy"));
                    var hasPartial = File.Exists(Path.Combine(path, ".partial"));
                    var hasExe = File.Exists(Path.Combine(path, executable));
                    var hasCurrent = File.Exists(Path.Combine(path, ".current"));
                    var version = ParseVersionFromDirectory(path);

                    Console.WriteLine($"[DeploymentLocator] Candidate: {Path.GetFileName(path)} | " +
                        $"Version={version} | " +
                        $"Current={hasCurrent} | " +
                        $"Destroy={hasDestroy} | " +
                        $"Partial={hasPartial} | " +
                        $"HasExe={hasExe}");

                    return !hasDestroy && !hasPartial && hasExe;
                })
                .Select(path => new
                {
                    Path = path,
                    Version = ParseVersionFromDirectory(path),
                    HasCurrentMarker = File.Exists(Path.Combine(path, ".current"))
                })
                .OrderBy(x => x.HasCurrentMarker ? 0 : 1)  // .current 标记的排前面
                .ThenByDescending(x => x.Version)  // 然后按版本号降序
                .ToList();

            if (validInstallations.Count == 0)
            {
                Console.WriteLine("[DeploymentLocator] No valid deployment directories found");
                return null;
            }

            var best = validInstallations[0];
            Console.WriteLine($"[DeploymentLocator] Selected: {Path.GetFileName(best.Path)} (current={best.HasCurrentMarker}, version={best.Version})");
            return best.Path;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[DeploymentLocator] Error searching for deployments: {ex}");
            return null;
        }
    }

    public string? ResolveHostExecutablePath()
    {
        // 使用新的灵活定位器
        var options = new HostDiscoveryOptions
        {
            ExecutableName = "LanMountainDesktop",
            PreferDevModeConfig = true,
            RecursiveSearch = false, // 默认不启用递归搜索以提高性能
            AdditionalSearchPaths = new List<string>
            {
                // 可以通过配置文件或环境变量添加更多路径
                "${AppRoot}",
                "${AppRoot}/..",
                "${BaseDirectory}/../..",
            }
        };

        var locator = new FlexibleHostLocator(_appRoot, options);
        var result = locator.ResolveHostExecutablePath();
        
        if (result != null)
        {
            return result;
        }

        // 回退到旧逻辑（作为备选）
        return ResolveHostExecutablePathLegacy();
    }

    /// <summary>
    /// 传统的主程序路径解析（作为备选）
    /// </summary>
    private string? ResolveHostExecutablePathLegacy()
    {
        var executable = OperatingSystem.IsWindows() ? "LanMountainDesktop.exe" : "LanMountainDesktop";
        
        // 1. 首先查找 app-{version} 目录（生产环境）
        var currentDeployment = FindCurrentDeploymentDirectory();
        if (!string.IsNullOrWhiteSpace(currentDeployment))
        {
            var inDeployment = Path.Combine(currentDeployment, executable);
            if (File.Exists(inDeployment))
            {
                return inDeployment;
            }
        }

        // 2. 查找 Launcher 所在目录（开发环境 - 直接运行）
        var inRoot = Path.Combine(_appRoot, executable);
        if (File.Exists(inRoot))
        {
            return inRoot;
        }

        // 3. 查找父目录（开发环境 - 从 Launcher 项目运行）
        var parent = Path.GetFullPath(Path.Combine(_appRoot, ".."));
        var inParent = Path.Combine(parent, executable);
        if (File.Exists(inParent))
        {
            return inParent;
        }

        // 4. 开发模式：如果启用了开发模式，优先使用保存的自定义路径
        if (Views.ErrorWindow.CheckDevModeEnabled())
        {
            // 4.1 首先检查保存的自定义路径
            var savedCustomPath = Views.ErrorWindow.GetSavedCustomHostPath();
            if (!string.IsNullOrWhiteSpace(savedCustomPath) && File.Exists(savedCustomPath))
            {
                return savedCustomPath;
            }

            // 4.2 扫描开发路径
            var devPath = ScanDevelopmentPaths(executable);
            if (!string.IsNullOrWhiteSpace(devPath))
            {
                return devPath;
            }
        }

        // 5. 开发模式：查找主程序项目的输出目录
        var devPaths = GetDevelopmentPaths(executable);
        foreach (var devPath in devPaths)
        {
            if (File.Exists(devPath))
            {
                return devPath;
            }
        }

        return null;
    }

    /// <summary>
    /// 扫描开发路径（开发模式）
    /// </summary>
    private static string? ScanDevelopmentPaths(string executable)
    {
        var possiblePaths = new[]
        {
            // 从 Launcher 项目运行
            Path.Combine(AppContext.BaseDirectory, "..", "..", "LanMountainDesktop", "bin", "Debug", "net10.0", executable),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "LanMountainDesktop", "bin", "Release", "net10.0", executable),
            
            // 从解决方案根目录运行
            Path.Combine(AppContext.BaseDirectory, "..", "LanMountainDesktop", "bin", "Debug", "net10.0", executable),
            Path.Combine(AppContext.BaseDirectory, "..", "LanMountainDesktop", "bin", "Release", "net10.0", executable),
            
            // dev-test 目录
            Path.Combine(AppContext.BaseDirectory, "..", "dev-test", "app-1.0.0-dev", executable),
        };

        foreach (var path in possiblePaths.Select(Path.GetFullPath).Distinct())
        {
            if (File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }

    /// <summary>
    /// 获取开发环境可能的主程序路径
    /// </summary>
    private static IEnumerable<string> GetDevelopmentPaths(string executable)
    {
        // 获取 Launcher 所在目录
        var launcherDir = AppContext.BaseDirectory;
        
        // 可能的开发目录结构
        var possiblePaths = new[]
        {
            // 从 Launcher 项目运行：..\LanMountainDesktop\bin\Debug\net10.0\LanMountainDesktop.exe
            Path.Combine(launcherDir, "..", "..", "LanMountainDesktop", "bin", "Debug", "net10.0", executable),
            Path.Combine(launcherDir, "..", "..", "LanMountainDesktop", "bin", "Release", "net10.0", executable),
            
            // 从解决方案根目录运行：LanMountainDesktop\bin\Debug\net10.0\LanMountainDesktop.exe
            Path.Combine(launcherDir, "..", "LanMountainDesktop", "bin", "Debug", "net10.0", executable),
            Path.Combine(launcherDir, "..", "LanMountainDesktop", "bin", "Release", "net10.0", executable),
            
            // 从 dev-test 目录运行
            Path.Combine(launcherDir, "..", "dev-test", "app-1.0.0-dev", executable),
        };

        return possiblePaths.Select(Path.GetFullPath).Distinct();
    }

    public string GetCurrentVersion()
    {
        var deployment = FindCurrentDeploymentDirectory();
        if (string.IsNullOrWhiteSpace(deployment))
        {
            return "0.0.0";
        }

        return ParseVersionTextFromDirectory(deployment) ?? "0.0.0";
    }

    public string BuildNextDeploymentDirectory(string targetVersion)
    {
        var sanitized = string.IsNullOrWhiteSpace(targetVersion) ? "0.0.0" : targetVersion.Trim();
        var index = 0;
        while (true)
        {
            var candidate = Path.Combine(_appRoot, $"app-{sanitized}-{index.ToString(CultureInfo.InvariantCulture)}");
            if (!Directory.Exists(candidate))
            {
                return candidate;
            }

            index++;
        }
    }

    /// <summary>
    /// 清理旧版本部署，保留最近的N个版本
    /// </summary>
    /// <param name="minVersionsToKeep">最少保留版本数，默认3个</param>
    public void CleanupOldDeployments(int minVersionsToKeep = 3)
    {
        try
        {
            Console.WriteLine($"[DeploymentLocator] Starting cleanup with retention policy: keep at least {minVersionsToKeep} versions");

            if (!Directory.Exists(_appRoot))
            {
                return;
            }

            var candidates = Directory.GetDirectories(_appRoot, "app-*", SearchOption.TopDirectoryOnly);

            // 过滤掉无效部署目录（排除partial），按版本排序
            var validDeployments = candidates
                .Where(path => !File.Exists(Path.Combine(path, ".partial")))
                .Select(path => new
                {
                    Path = path,
                    Version = ParseVersionFromDirectory(path),
                    IsDestroyed = File.Exists(Path.Combine(path, ".destroy")),
                    IsCurrent = File.Exists(Path.Combine(path, ".current"))
                })
                .OrderByDescending(item => item.Version)
                .ToList();

            Console.WriteLine($"[DeploymentLocator] Found {validDeployments.Count} valid deployments");

            // 确定要保留的版本
            var versionsToKeep = new HashSet<string>();

            // 1. 总是保留当前版本
            var currentVersion = validDeployments.FirstOrDefault(d => d.IsCurrent);
            if (currentVersion != null)
            {
                versionsToKeep.Add(currentVersion.Path);
                Console.WriteLine($"[DeploymentLocator] Keep current version: {currentVersion.Path}");
            }

            // 2. 保留最近的N个有效版本（不包括已标记destroy的）
            var activeVersions = validDeployments
                .Where(d => !d.IsDestroyed)
                .Take(minVersionsToKeep)
                .ToList();

            foreach (var ver in activeVersions)
            {
                versionsToKeep.Add(ver.Path);
                Console.WriteLine($"[DeploymentLocator] Keep recent version: {ver.Path}");
            }

            // 3. 保留有快照的版本（用于回滚）
            var snapshotDir = Path.Combine(_appRoot, ".launcher", "snapshots");
            if (Directory.Exists(snapshotDir))
            {
                try
                {
                    var snapshotFiles = Directory.GetFiles(snapshotDir, "*.json", SearchOption.TopDirectoryOnly);
                    foreach (var snapshotFile in snapshotFiles)
                    {
                        try
                        {
                            var json = File.ReadAllText(snapshotFile);
                            var snapshot = System.Text.Json.JsonSerializer.Deserialize(json, AppJsonContext.Default.SnapshotMetadata);
                            if (snapshot != null && !string.IsNullOrEmpty(snapshot.SourceDirectory))
                            {
                                if (Directory.Exists(snapshot.SourceDirectory))
                                {
                                    versionsToKeep.Add(snapshot.SourceDirectory);
                                    Console.WriteLine($"[DeploymentLocator] Keep version for rollback: {snapshot.SourceDirectory}");
                                }
                            }
                        }
                        catch
                        {
                            // 忽略快照解析错误
                        }
                    }
                }
                catch
                {
                    // 忽略快照目录访问错误
                }
            }

            // 清理不需要的版本
            foreach (var deployment in validDeployments)
            {
                if (versionsToKeep.Contains(deployment.Path))
                {
                    // 保留此版本，如果之前标记了destroy则取消标记
                    if (deployment.IsDestroyed)
                    {
                        try
                        {
                            File.Delete(Path.Combine(deployment.Path, ".destroy"));
                            Console.WriteLine($"[DeploymentLocator] Unmarked for deletion (kept): {deployment.Path}");
                        }
                        catch
                        {
                            // 忽略取消标记失败
                        }
                    }
                    continue;
                }

                // 如果还没标记destroy的，先标记
                if (!deployment.IsDestroyed)
                {
                    try
                    {
                        File.WriteAllText(Path.Combine(deployment.Path, ".destroy"), string.Empty);
                        Console.WriteLine($"[DeploymentLocator] Marked for deletion: {deployment.Path}");
                    }
                    catch
                    {
                        // 忽略标记失败
                    }
                }

                // 尝试删除
                try
                {
                    Directory.Delete(deployment.Path, recursive: true);
                    Console.WriteLine($"[DeploymentLocator] Deleted: {deployment.Path}");
                }
                catch
                {
                    // 忽略删除失败(可能文件被占用),下次启动再试
                    Console.WriteLine($"[DeploymentLocator] Failed to delete (will retry later): {deployment.Path}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[DeploymentLocator] Cleanup failed: {ex.Message}");
            // 忽略清理失败
        }
    }

    /// <summary>
    /// 仅清理已标记为.destroy的部署（兼容旧方法）
    /// </summary>
    [Obsolete("Use CleanupOldDeployments instead")]
    public void CleanupDestroyedDeployments()
    {
        CleanupOldDeployments(3);
    }

    public static Version ParseVersionFromDirectory(string path)
    {
        var text = ParseVersionTextFromDirectory(path);
        return Version.TryParse(text, out var version) ? version : new Version(0, 0, 0);
    }

    private static string? ParseVersionTextFromDirectory(string path)
    {
        var fileName = Path.GetFileName(path);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        var segments = fileName.Split('-');
        if (segments.Length < 2)
        {
            return null;
        }

        return segments[1];
    }

    /// <summary>
    /// 从部署目录读取版本信息
    /// </summary>
    public AppVersionInfo GetVersionInfo()
    {
        var deploymentDir = FindCurrentDeploymentDirectory();
        if (!string.IsNullOrWhiteSpace(deploymentDir))
        {
            var versionFile = Path.Combine(deploymentDir, "version.json");
            if (File.Exists(versionFile))
            {
                try
                {
                    var json = File.ReadAllText(versionFile);
                    var info = JsonSerializer.Deserialize(json, AppJsonContext.Default.AppVersionInfo);
                    if (info is not null)
                    {
                        return info;
                    }
                }
                catch
                {
                    // 忽略读取失败，回退到默认值
                }
            }
        }

        // 回退：从目录名解析版本，使用默认开发代号
        return new AppVersionInfo
        {
            Version = GetCurrentVersion(),
            Codename = "Administrate" // 默认开发代号
        };
    }
}
