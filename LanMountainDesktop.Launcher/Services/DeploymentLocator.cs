using System.Globalization;
using System.Text.Json;
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
        var candidates = Directory.Exists(_appRoot)
            ? Directory.GetDirectories(_appRoot, "app-*", SearchOption.TopDirectoryOnly)
            : [];

        // 过滤掉无效的部署目录
        var validCandidates = candidates
            .Where(path => 
                !File.Exists(Path.Combine(path, ".destroy")) &&    // 排除待删除
                !File.Exists(Path.Combine(path, ".partial")))      // 排除未完成
            .ToList();

        // 优先选择带 .current 标记的版本
        var withMarkers = validCandidates
            .Where(path => File.Exists(Path.Combine(path, ".current")))
            .Select(path => new
            {
                Path = path,
                Version = ParseVersionFromDirectory(path)
            })
            .OrderByDescending(item => item.Version)
            .ToList();

        if (withMarkers.Count > 0)
        {
            return withMarkers[0].Path;
        }

        // 如果没有 .current 标记,选择最新版本
        var byVersion = validCandidates
            .Select(path => new
            {
                Path = path,
                Version = ParseVersionFromDirectory(path)
            })
            .OrderByDescending(item => item.Version)
            .ToList();

        return byVersion.Count > 0 ? byVersion[0].Path : null;
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

    public void CleanupDestroyedDeployments()
    {
        try
        {
            var candidates = Directory.Exists(_appRoot)
                ? Directory.GetDirectories(_appRoot, "app-*", SearchOption.TopDirectoryOnly)
                : [];

            var destroyedDirs = candidates
                .Where(path => File.Exists(Path.Combine(path, ".destroy")));

            foreach (var dir in destroyedDirs)
            {
                try
                {
                    Directory.Delete(dir, recursive: true);
                }
                catch
                {
                    // 忽略删除失败(可能文件被占用),下次启动再试
                }
            }
        }
        catch
        {
            // 忽略清理失败
        }
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
                    var info = JsonSerializer.Deserialize<AppVersionInfo>(json);
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
