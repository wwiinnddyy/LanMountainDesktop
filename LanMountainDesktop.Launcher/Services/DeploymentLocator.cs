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

    public HostResolutionResult ResolveHostExecutable(CommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var executable = OperatingSystem.IsWindows() ? "LanMountainDesktop.exe" : "LanMountainDesktop";
        var searchedPaths = new List<string>();
        var explicitAppRoot = context.ExplicitAppRoot;
        var devModeConfigIgnored = !context.IsDebugMode && Views.ErrorWindow.CheckDevModeEnabled();

        string? resolvedPath;
        string? source;

        if (!string.IsNullOrWhiteSpace(explicitAppRoot))
        {
            var explicitRoot = Path.GetFullPath(explicitAppRoot);
            resolvedPath = TryResolveExplicitAppRoot(explicitRoot, executable, searchedPaths, out source);
        }
        else
        {
            resolvedPath = TryResolvePublishedOrPortableHost(executable, searchedPaths, out source);
        }

        if (resolvedPath is null && context.IsDebugMode)
        {
            resolvedPath = TryResolveDebugHost(executable, searchedPaths, out source);
        }

        if (resolvedPath is null)
        {
            resolvedPath = ResolveHostExecutablePathLegacy();
            if (!string.IsNullOrWhiteSpace(resolvedPath))
            {
                searchedPaths.Add(Path.GetFullPath(resolvedPath));
                source = "legacy_fallback";
            }
        }

        return new HostResolutionResult
        {
            Success = !string.IsNullOrWhiteSpace(resolvedPath),
            ResolvedHostPath = resolvedPath,
            ResolutionSource = source,
            AppRoot = _appRoot,
            ExplicitAppRoot = explicitAppRoot,
            DevModeConfigIgnored = devModeConfigIgnored,
            SearchedPaths = searchedPaths
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList()
        };
    }

    public string? ResolveHostExecutablePath()
    {
        return ResolveHostExecutablePathLegacy();
    }

    private string? TryResolveExplicitAppRoot(
        string explicitRoot,
        string executable,
        List<string> searchedPaths,
        out string? source)
    {
        var directPath = Path.Combine(explicitRoot, executable);
        searchedPaths.Add(directPath);
        if (File.Exists(directPath))
        {
            source = "explicit_app_root_direct";
            return directPath;
        }

        var deployment = FindBestDeploymentHost(explicitRoot, executable, searchedPaths);
        if (deployment is not null)
        {
            source = "explicit_app_root_deployment";
            return deployment;
        }

        source = null;
        return null;
    }

    private string? TryResolvePublishedOrPortableHost(
        string executable,
        List<string> searchedPaths,
        out string? source)
    {
        var deployment = FindBestDeploymentHost(_appRoot, executable, searchedPaths);
        if (deployment is not null)
        {
            source = "published_deployment";
            return deployment;
        }

        var portableCandidates = new[]
        {
            Path.Combine(_appRoot, executable),
            Path.Combine(AppContext.BaseDirectory, executable)
        };

        foreach (var candidate in portableCandidates
                     .Select(Path.GetFullPath)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            searchedPaths.Add(candidate);
            if (File.Exists(candidate))
            {
                source = "portable_host";
                return candidate;
            }
        }

        source = null;
        return null;
    }

    private string? TryResolveDebugHost(
        string executable,
        List<string> searchedPaths,
        out string? source)
    {
        if (Views.ErrorWindow.CheckDevModeEnabled())
        {
            var savedCustomPath = Views.ErrorWindow.GetSavedCustomHostPath();
            if (!string.IsNullOrWhiteSpace(savedCustomPath))
            {
                var fullSavedPath = Path.GetFullPath(savedCustomPath);
                searchedPaths.Add(fullSavedPath);
                if (File.Exists(fullSavedPath))
                {
                    source = "debug_saved_custom_path";
                    return fullSavedPath;
                }
            }
        }

        foreach (var devPath in GetDevelopmentPaths(executable))
        {
            var fullPath = Path.GetFullPath(devPath);
            searchedPaths.Add(fullPath);
            if (File.Exists(fullPath))
            {
                source = "debug_build_output";
                return fullPath;
            }
        }

        source = null;
        return null;
    }

    private static string? FindBestDeploymentHost(
        string root,
        string executable,
        List<string> searchedPaths)
    {
        if (!Directory.Exists(root))
        {
            searchedPaths.Add(Path.Combine(root, "app-*", executable));
            return null;
        }

        var appDirs = Directory.GetDirectories(root, "app-*", SearchOption.TopDirectoryOnly)
            .Where(path => !File.Exists(Path.Combine(path, ".destroy")))
            .Where(path => !File.Exists(Path.Combine(path, ".partial")))
            .Select(path => new
            {
                Path = path,
                HostPath = Path.Combine(path, executable),
                HasCurrent = File.Exists(Path.Combine(path, ".current")),
                Version = ParseVersionFromDirectory(path)
            })
            .OrderByDescending(item => item.HasCurrent)
            .ThenByDescending(item => item.Version)
            .ToList();

        foreach (var candidate in appDirs)
        {
            searchedPaths.Add(candidate.HostPath);
            if (File.Exists(candidate.HostPath))
            {
                return candidate.HostPath;
            }
        }

        if (appDirs.Count == 0)
        {
            searchedPaths.Add(Path.Combine(root, "app-*", executable));
        }

        return null;
    }

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

        var inRoot = Path.Combine(_appRoot, executable);
        if (File.Exists(inRoot))
        {
            return inRoot;
        }

        var parent = Path.GetFullPath(Path.Combine(_appRoot, ".."));
        var inParent = Path.Combine(parent, executable);
        if (File.Exists(inParent))
        {
            return inParent;
        }

        // 4. 开发模式：如果启用了开发模式，优先使用保存的自定义路径
        if (Views.ErrorWindow.CheckDevModeEnabled())
        {
            var savedCustomPath = Views.ErrorWindow.GetSavedCustomHostPath();
            if (!string.IsNullOrWhiteSpace(savedCustomPath) && File.Exists(savedCustomPath))
            {
                return savedCustomPath;
            }

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
            // �?Launcher 项目运行
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
    /// 获取开发环境可能的主程序路�?    /// </summary>
    private static IEnumerable<string> GetDevelopmentPaths(string executable)
    {
        var launcherDir = AppContext.BaseDirectory;
        
        var possiblePaths = new[]
        {
            // �?Launcher 项目运行�?.\LanMountainDesktop\bin\Debug\net10.0\LanMountainDesktop.exe
            Path.Combine(launcherDir, "..", "..", "LanMountainDesktop", "bin", "Debug", "net10.0", executable),
            Path.Combine(launcherDir, "..", "..", "LanMountainDesktop", "bin", "Release", "net10.0", executable),
            
            // 从解决方案根目录运行：LanMountainDesktop\bin\Debug\net10.0\LanMountainDesktop.exe
            Path.Combine(launcherDir, "..", "LanMountainDesktop", "bin", "Debug", "net10.0", executable),
            Path.Combine(launcherDir, "..", "LanMountainDesktop", "bin", "Release", "net10.0", executable),
            
            // �?dev-test 目录运行
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
    /// 清理旧版本部署，保留最近的N个版�?    /// </summary>
    /// <param name="minVersionsToKeep">最少保留版本数，默�?�?/param>
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
                    // 忽略删除失败(可能文件被占�?,下次启动再试
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
    /// 仅清理已标记�?destroy的部署（兼容旧方法）
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
    /// 从部署目录读取版本信�?    /// </summary>
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
                }
            }
        }

        return new AppVersionInfo
        {
            Version = GetCurrentVersion(),
            Codename = "Administrate"
        };
}


}
