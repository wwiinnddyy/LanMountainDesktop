using System.Diagnostics;
using System.Text.Json;

namespace LanMountainDesktop.Launcher.Services;

/// <summary>
/// 灵活的主程序定位器
/// </summary>
internal sealed class FlexibleHostLocator
{
    private readonly HostDiscoveryOptions _options;
    private readonly string _appRoot;

    public FlexibleHostLocator(string appRoot, HostDiscoveryOptions? options = null)
    {
        _appRoot = appRoot;
        _options = options ?? new HostDiscoveryOptions();
    }

    /// <summary>
    /// 解析主程序可执行文件路径
    /// </summary>
    public string? ResolveHostExecutablePath()
    {
        var executable = GetExecutableName();
        var searchContext = new SearchContext
        {
            ExecutableName = executable,
            AppRoot = _appRoot,
            Options = _options
        };

        // ========== 第一阶段：标准路径查找（快速路径）==========
        
        // 1. 检查环境变量指定的路径（最高优先级 - 用于调试和特殊场景）
        var envPath = GetPathFromEnvironment();
        if (!string.IsNullOrWhiteSpace(envPath))
        {
            var validated = ValidateAndReturn(envPath, "environment variable");
            if (validated != null) return validated;
        }

        // 2. 搜索部署目录（app-*）- 生产环境标准路径
        var deploymentPath = SearchDeploymentDirectories(searchContext);
        if (!string.IsNullOrWhiteSpace(deploymentPath))
        {
            return deploymentPath;
        }

        // 3. 检查 Launcher 同级目录（便携模式）
        var portablePath = SearchPortableLocation(searchContext);
        if (!string.IsNullOrWhiteSpace(portablePath))
        {
            return portablePath;
        }

        // ========== 第二阶段：灵活查找（标准路径找不到时）==========
        
        // 4. 检查配置文件中的路径 - 用户自定义配置
        var configPath = GetPathFromConfigFile();
        if (!string.IsNullOrWhiteSpace(configPath))
        {
            var validated = ValidateAndReturn(configPath, "config file");
            if (validated != null) return validated;
        }

        // 5. 搜索附近目录（向上、向下各一层）
        var nearbyPath = SearchNearbyDirectories(searchContext);
        if (!string.IsNullOrWhiteSpace(nearbyPath))
        {
            return nearbyPath;
        }

        // 6. 开发模式：检查保存的自定义路径
        if (_options.PreferDevModeConfig && Views.ErrorWindow.CheckDevModeEnabled())
        {
            var savedPath = Views.ErrorWindow.GetSavedCustomHostPath();
            if (!string.IsNullOrWhiteSpace(savedPath))
            {
                var validated = ValidateAndReturn(savedPath, "saved dev mode path");
                if (validated != null) return validated;
            }
        }

        // 7. 搜索标准开发路径
        var devPath = SearchDevelopmentPaths(searchContext);
        if (!string.IsNullOrWhiteSpace(devPath))
        {
            return devPath;
        }

        // 8. 搜索额外的配置路径
        var additionalPath = SearchAdditionalPaths(searchContext);
        if (!string.IsNullOrWhiteSpace(additionalPath))
        {
            return additionalPath;
        }

        // 9. 递归搜索（如果启用）
        if (_options.RecursiveSearch)
        {
            var recursivePath = SearchRecursively(searchContext);
            if (!string.IsNullOrWhiteSpace(recursivePath))
            {
                return recursivePath;
            }
        }

        return null;
    }

    /// <summary>
    /// 从环境变量获取路径
    /// </summary>
    private string? GetPathFromEnvironment()
    {
        if (string.IsNullOrWhiteSpace(_options.CustomPathEnvVar))
        {
            return null;
        }

        var path = Environment.GetEnvironmentVariable(_options.CustomPathEnvVar);
        return path;
    }

    /// <summary>
    /// 从配置文件获取路径
    /// </summary>
    private string? GetPathFromConfigFile()
    {
        if (string.IsNullOrWhiteSpace(_options.ConfigFileName))
        {
            return null;
        }

        var configPath = Path.Combine(_appRoot, _options.ConfigFileName);
        if (!File.Exists(configPath))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(configPath);
            var config = JsonSerializer.Deserialize<HostDiscoveryConfig>(json);
            if (config?.HostPath != null && File.Exists(config.HostPath))
            {
                return config.HostPath;
            }
        }
        catch
        {
            // 忽略配置文件读取错误
        }

        return null;
    }

    /// <summary>
    /// 搜索部署目录
    /// </summary>
    private string? SearchDeploymentDirectories(SearchContext context)
    {
        if (!Directory.Exists(_appRoot))
        {
            return null;
        }

        try
        {
            // 查找 app-* 目录
            var appDirs = Directory.GetDirectories(_appRoot, "app-*", SearchOption.TopDirectoryOnly)
                .Where(dir => !File.Exists(Path.Combine(dir, ".destroy")))
                .Where(dir => !File.Exists(Path.Combine(dir, ".partial")))
                .ToList();

            // 优先选择带 .current 标记的
            var currentMarked = appDirs
                .Where(dir => File.Exists(Path.Combine(dir, ".current")))
                .Select(dir => Path.Combine(dir, context.ExecutableName))
                .FirstOrDefault(File.Exists);

            if (currentMarked != null)
            {
                return currentMarked;
            }

            // 选择版本号最高的
            var latest = appDirs
                .Select(dir => new
                {
                    Dir = dir,
                    Version = ParseVersionFromDirectoryName(dir)
                })
                .OrderByDescending(x => x.Version)
                .Select(x => Path.Combine(x.Dir, context.ExecutableName))
                .FirstOrDefault(File.Exists);

            return latest;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 搜索便携模式位置（Launcher 同级目录）
    /// </summary>
    private string? SearchPortableLocation(SearchContext context)
    {
        try
        {
            var launcherDir = AppContext.BaseDirectory;
            var portablePath = Path.Combine(launcherDir, context.ExecutableName);
            
            if (File.Exists(portablePath))
            {
                return portablePath;
            }
        }
        catch
        {
            // 忽略错误
        }
        return null;
    }

    /// <summary>
    /// 搜索附近目录（灵活查找，适用于各种部署场景）
    /// </summary>
    private string? SearchNearbyDirectories(SearchContext context)
    {
        try
        {
            var searchDirs = new List<string>();
            
            // Launcher 所在目录
            var launcherDir = AppContext.BaseDirectory;
            searchDirs.Add(launcherDir);
            
            // 上级目录
            var parentDir = Path.GetFullPath(Path.Combine(launcherDir, ".."));
            if (Directory.Exists(parentDir))
            {
                searchDirs.Add(parentDir);
            }
            
            // 上上级目录
            var grandparentDir = Path.GetFullPath(Path.Combine(launcherDir, "..", ".."));
            if (Directory.Exists(grandparentDir))
            {
                searchDirs.Add(grandparentDir);
            }
            
            // AppRoot 及其上级
            if (!string.IsNullOrWhiteSpace(_appRoot) && Directory.Exists(_appRoot))
            {
                searchDirs.Add(_appRoot);
                var appParent = Path.GetFullPath(Path.Combine(_appRoot, ".."));
                if (Directory.Exists(appParent))
                {
                    searchDirs.Add(appParent);
                }
            }
            
            // 去重后搜索
            foreach (var dir in searchDirs.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                // 直接搜索
                var directPath = Path.Combine(dir, context.ExecutableName);
                if (File.Exists(directPath))
                {
                    return directPath;
                }
                
                // 搜索子目录（一层）
                if (Directory.Exists(dir))
                {
                    foreach (var subDir in Directory.GetDirectories(dir))
                    {
                        var subPath = Path.Combine(subDir, context.ExecutableName);
                        if (File.Exists(subPath))
                        {
                            return subPath;
                        }
                    }
                }
            }
        }
        catch
        {
            // 忽略搜索错误
        }
        
        return null;
    }

    /// <summary>
    /// 搜索开发路径
    /// </summary>
    private string? SearchDevelopmentPaths(SearchContext context)
    {
        // 获取 Launcher 所在目录
        var launcherDir = AppContext.BaseDirectory;

        // 动态构建可能的开发路径（支持不同的项目结构）
        var possiblePaths = new List<string>();

        // 从解决方案根目录搜索（支持不同的解决方案结构）
        var solutionRoot = FindSolutionRoot(launcherDir);
        if (!string.IsNullOrWhiteSpace(solutionRoot))
        {
            // 搜索所有可能的 bin 目录
            possiblePaths.AddRange(SearchBinDirectories(solutionRoot, context.ExecutableName));
        }

        // 添加硬编码的备用路径
        possiblePaths.AddRange(new[]
        {
            Path.Combine(launcherDir, "..", "..", "LanMountainDesktop", "bin", "Debug", "net10.0", context.ExecutableName),
            Path.Combine(launcherDir, "..", "..", "LanMountainDesktop", "bin", "Release", "net10.0", context.ExecutableName),
            Path.Combine(launcherDir, "..", "LanMountainDesktop", "bin", "Debug", "net10.0", context.ExecutableName),
            Path.Combine(launcherDir, "..", "LanMountainDesktop", "bin", "Release", "net10.0", context.ExecutableName),
        });

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
    /// 搜索额外的配置路径
    /// </summary>
    private string? SearchAdditionalPaths(SearchContext context)
    {
        if (_options.AdditionalSearchPaths == null || !_options.AdditionalSearchPaths.Any())
        {
            return null;
        }

        foreach (var pattern in _options.AdditionalSearchPaths)
        {
            try
            {
                // 替换变量
                var expandedPattern = ExpandVariables(pattern);

                // 支持通配符
                if (expandedPattern.Contains('*') || expandedPattern.Contains('?'))
                {
                    var dir = Path.GetDirectoryName(expandedPattern) ?? _appRoot;
                    var filePattern = Path.GetFileName(expandedPattern);

                    if (Directory.Exists(dir))
                    {
                        var matches = Directory.GetFiles(dir, filePattern, SearchOption.TopDirectoryOnly);
                        var validMatch = matches.FirstOrDefault(File.Exists);
                        if (validMatch != null)
                        {
                            return validMatch;
                        }
                    }
                }
                else if (File.Exists(expandedPattern))
                {
                    return expandedPattern;
                }
            }
            catch
            {
                // 忽略搜索错误
            }
        }

        return null;
    }

    /// <summary>
    /// 递归搜索
    /// </summary>
    private string? SearchRecursively(SearchContext context)
    {
        try
        {
            var searchDirs = new[] { _appRoot, Path.GetFullPath(Path.Combine(_appRoot, "..")) };

            foreach (var searchDir in searchDirs.Where(Directory.Exists))
            {
                var result = SearchDirectoryRecursively(searchDir, context.ExecutableName, 0);
                if (result != null)
                {
                    return result;
                }
            }
        }
        catch
        {
            // 忽略递归搜索错误
        }

        return null;
    }

    /// <summary>
    /// 递归搜索目录
    /// </summary>
    private string? SearchDirectoryRecursively(string dir, string executableName, int depth)
    {
        if (depth > _options.MaxRecursionDepth)
        {
            return null;
        }

        try
        {
            // 检查当前目录
            var directPath = Path.Combine(dir, executableName);
            if (File.Exists(directPath))
            {
                return directPath;
            }

            // 检查子目录
            foreach (var subDir in Directory.GetDirectories(dir))
            {
                // 跳过某些目录
                var dirName = Path.GetFileName(subDir).ToLowerInvariant();
                if (dirName is ".git" or "node_modules" or ".vs" or "obj" or ".launcher")
                {
                    continue;
                }

                var result = SearchDirectoryRecursively(subDir, executableName, depth + 1);
                if (result != null)
                {
                    return result;
                }
            }
        }
        catch
        {
            // 忽略访问错误
        }

        return null;
    }

    /// <summary>
    /// 查找解决方案根目录
    /// </summary>
    private string? FindSolutionRoot(string startDir)
    {
        var current = new DirectoryInfo(startDir);
        while (current != null)
        {
            // 查找 .sln 文件
            if (current.GetFiles("*.sln").Any())
            {
                return current.FullName;
            }

            // 查找 .git 目录作为备选
            if (current.GetDirectories(".git").Any())
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return null;
    }

    /// <summary>
    /// 搜索 bin 目录
    /// </summary>
    private IEnumerable<string> SearchBinDirectories(string root, string executableName)
    {
        var results = new List<string>();

        try
        {
            // 查找所有 bin 目录
            var binDirs = Directory.GetDirectories(root, "bin", SearchOption.AllDirectories);

            foreach (var binDir in binDirs)
            {
                // 检查 Debug 和 Release 子目录
                var configDirs = new[] { "Debug", "Release" };
                foreach (var config in configDirs)
                {
                    var configPath = Path.Combine(binDir, config);
                    if (Directory.Exists(configPath))
                    {
                        // 检查所有 net* 子目录
                        var frameworkDirs = Directory.GetDirectories(configPath, "net*");
                        foreach (var fwDir in frameworkDirs)
                        {
                            var exePath = Path.Combine(fwDir, executableName);
                            if (File.Exists(exePath))
                            {
                                results.Add(exePath);
                            }
                        }
                    }
                }
            }
        }
        catch
        {
            // 忽略搜索错误
        }

        return results;
    }

    /// <summary>
    /// 验证路径并返回
    /// </summary>
    private string? ValidateAndReturn(string path, string source)
    {
        if (File.Exists(path))
        {
            Debug.WriteLine($"Found host executable from {source}: {path}");
            return path;
        }

        // 尝试添加 .exe（Windows）
        if (OperatingSystem.IsWindows() && !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            var withExe = path + ".exe";
            if (File.Exists(withExe))
            {
                Debug.WriteLine($"Found host executable from {source}: {withExe}");
                return withExe;
            }
        }

        return null;
    }

    /// <summary>
    /// 获取可执行文件名
    /// </summary>
    private string GetExecutableName()
    {
        var name = _options.ExecutableName;
        if (OperatingSystem.IsWindows() && !name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            name += ".exe";
        }
        return name;
    }

    /// <summary>
    /// 展开路径变量
    /// </summary>
    private string ExpandVariables(string path)
    {
        return path
            .Replace("${AppRoot}", _appRoot)
            .Replace("${BaseDirectory}", AppContext.BaseDirectory)
            .Replace("${UserProfile}", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))
            .Replace("${LocalAppData}", Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
    }

    /// <summary>
    /// 从目录名解析版本
    /// </summary>
    private static Version ParseVersionFromDirectoryName(string path)
    {
        var fileName = Path.GetFileName(path);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return new Version(0, 0, 0);
        }

        var segments = fileName.Split('-');
        if (segments.Length < 2)
        {
            return new Version(0, 0, 0);
        }

        return Version.TryParse(segments[1], out var version) ? version : new Version(0, 0, 0);
    }

    /// <summary>
    /// 搜索上下文
    /// </summary>
    private class SearchContext
    {
        public required string ExecutableName { get; set; }
        public required string AppRoot { get; set; }
        public required HostDiscoveryOptions Options { get; set; }
    }

    /// <summary>
    /// 发现配置文件
    /// </summary>
    private class HostDiscoveryConfig
    {
        public string? HostPath { get; set; }
        public List<string>? AdditionalPaths { get; set; }
    }
}
