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
                .OrderBy(x => x.HasCurrentMarker ? 0 : 1)  // .current 鏍囪鐨勬帓鍓嶉潰
                .ThenByDescending(x => x.Version)  // 鐒跺悗鎸夌増鏈彿闄嶅簭
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
                if (TryNormalizeSavedDebugPath(savedCustomPath, out var fullSavedPath))
                {
                    searchedPaths.Add(fullSavedPath);
                    if (File.Exists(fullSavedPath))
                    {
                        source = "debug_saved_custom_path";
                        return fullSavedPath;
                    }

                    Logger.Warn($"Saved launcher debug host path is invalid; falling back to development paths. Path='{fullSavedPath}'.");
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

    private static bool TryNormalizeSavedDebugPath(string savedPath, out string fullSavedPath)
    {
        try
        {
            fullSavedPath = Path.GetFullPath(savedPath);
            return true;
        }
        catch (Exception ex)
        {
            fullSavedPath = string.Empty;
            Logger.Warn($"Saved launcher debug host path is invalid and cannot be normalized; falling back to development paths. Path='{savedPath}'; Error='{ex.Message}'.");
            return false;
        }
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
        
        // 1. 棣栧厛鏌ユ壘 app-{version} 鐩綍锛堢敓浜х幆澧冿級
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

        // 4. 寮€鍙戞ā寮忥細濡傛灉鍚敤浜嗗紑鍙戞ā寮忥紝浼樺厛浣跨敤淇濆瓨鐨勮嚜瀹氫箟璺緞
        if (Views.ErrorWindow.CheckDevModeEnabled())
        {
            var savedCustomPath = Views.ErrorWindow.GetSavedCustomHostPath();
            if (!string.IsNullOrWhiteSpace(savedCustomPath))
            {
                if (TryNormalizeSavedDebugPath(savedCustomPath, out var fullSavedPath) &&
                    File.Exists(fullSavedPath))
                {
                    return fullSavedPath;
                }
                else if (!string.IsNullOrWhiteSpace(fullSavedPath))
                {
                    Logger.Warn($"Saved launcher debug host path is invalid; falling back to development paths. Path='{fullSavedPath}'.");
                }
            }

            var devPath = ScanDevelopmentPaths(executable);
            if (!string.IsNullOrWhiteSpace(devPath))
            {
                return devPath;
            }
        }

        // 5. 寮€鍙戞ā寮忥細鏌ユ壘涓荤▼搴忛」鐩殑杈撳嚭鐩綍
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
    /// 鎵弿寮€鍙戣矾寰勶紙寮€鍙戞ā寮忥級
    /// </summary>
    private static string? ScanDevelopmentPaths(string executable)
    {
        var solutionRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        
        var possiblePaths = new[]
        {
            // 标准开发路径：解决方案根目录下的 LanMountainDesktop 项目
            Path.Combine(solutionRoot, "LanMountainDesktop", "bin", "Debug", "net10.0", executable),
            Path.Combine(solutionRoot, "LanMountainDesktop", "bin", "Release", "net10.0", executable),
            
            // 向后兼容
            Path.Combine(AppContext.BaseDirectory, "..", "..", "LanMountainDesktop", "bin", "Debug", "net10.0", executable),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "LanMountainDesktop", "bin", "Release", "net10.0", executable),
            
            // dev-test 目录
            Path.Combine(solutionRoot, "dev-test", "app-1.0.0-dev", executable),
        };

        foreach (var path in possiblePaths.Select(Path.GetFullPath).Distinct())
        {
            Logger.Info($"Scanning development path: {path}");
            if (File.Exists(path))
            {
                Logger.Info($"Found host at: {path}");
                return path;
            }
        }

        return null;
     }
    
    /// <summary>
    /// 鑾峰彇寮€鍙戞ā寮忥細鏌ユ壘涓荤▼搴忚経
    /// </summary>
    private static IEnumerable<string> GetDevelopmentPaths(string executable)
    {
        var launcherDir = AppContext.BaseDirectory;
        
        // 计算解决方案根目录：从 LanMountainDesktop.Launcher\bin\Debug\net10.0\ 向上4级
        var solutionRoot = Path.GetFullPath(Path.Combine(launcherDir, "..", "..", "..", ".."));
        
        var possiblePaths = new[]
        {
            // 标准开发路径：解决方案根目录下的 LanMountainDesktop 项目
            Path.Combine(solutionRoot, "LanMountainDesktop", "bin", "Debug", "net10.0", executable),
            Path.Combine(solutionRoot, "LanMountainDesktop", "bin", "Release", "net10.0", executable),
            
            // 向后兼容：如果 Launcher 在特殊目录结构中
            Path.Combine(launcherDir, "..", "..", "LanMountainDesktop", "bin", "Debug", "net10.0", executable),
            Path.Combine(launcherDir, "..", "..", "LanMountainDesktop", "bin", "Release", "net10.0", executable),
            
            // dev-test 目录
            Path.Combine(solutionRoot, "dev-test", "app-1.0.0-dev", executable),
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
    /// 娓呯悊鏃х増鏈儴缃诧紝淇濈暀鏈€杩戠殑N涓増鏈?    /// </summary>
    /// <param name="minVersionsToKeep">鏈€灏戜繚鐣欑増鏈暟锛岄粯璁?涓?/param>
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

            // 纭畾瑕佷繚鐣欑殑鐗堟湰
            var versionsToKeep = new HashSet<string>();

            // 1. 鎬绘槸淇濈暀褰撳墠鐗堟湰
            var currentVersion = validDeployments.FirstOrDefault(d => d.IsCurrent);
            if (currentVersion != null)
            {
                versionsToKeep.Add(currentVersion.Path);
                Console.WriteLine($"[DeploymentLocator] Keep current version: {currentVersion.Path}");
            }

            // 2. 淇濈暀鏈€杩戠殑N涓湁鏁堢増鏈紙涓嶅寘鎷凡鏍囪destroy鐨勶級
            var activeVersions = validDeployments
                .Where(d => !d.IsDestroyed)
                .Take(minVersionsToKeep)
                .ToList();

            foreach (var ver in activeVersions)
            {
                versionsToKeep.Add(ver.Path);
                Console.WriteLine($"[DeploymentLocator] Keep recent version: {ver.Path}");
            }

            // 3. 淇濈暀鏈夊揩鐓х殑鐗堟湰锛堢敤浜庡洖婊氾級
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
                            // 蹇界暐蹇収瑙ｆ瀽閿欒
                        }
                    }
                }
                catch
                {
                    // 蹇界暐蹇収鐩綍璁块棶閿欒
                }
            }

            // 娓呯悊涓嶉渶瑕佺殑鐗堟湰
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
                            // 蹇界暐鍙栨秷鏍囪澶辫触
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
                        // 蹇界暐鏍囪澶辫触
                    }
                }

                // 灏濊瘯鍒犻櫎
                try
                {
                    Directory.Delete(deployment.Path, recursive: true);
                    Console.WriteLine($"[DeploymentLocator] Deleted: {deployment.Path}");
                }
                catch
                {
                    // 蹇界暐鍒犻櫎澶辫触(鍙兘鏂囦欢琚崰鐢?,涓嬫鍚姩鍐嶈瘯
                    Console.WriteLine($"[DeploymentLocator] Failed to delete (will retry later): {deployment.Path}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[DeploymentLocator] Cleanup failed: {ex.Message}");
            // 蹇界暐娓呯悊澶辫触
        }
    }

    /// <summary>
    /// 浠呮竻鐞嗗凡鏍囪涓?destroy鐨勯儴缃诧紙鍏煎鏃ф柟娉曪級
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
    /// 浠庨儴缃茬洰褰曡鍙栫増鏈俊鎭?    /// </summary>
    public AppVersionInfo GetVersionInfo()
    {
        var executableName = OperatingSystem.IsWindows() ? "LanMountainDesktop.exe" : "LanMountainDesktop";
        var resolved = AppVersionProvider.ResolveFromPackageRoot(_appRoot, executableName);
        return string.IsNullOrWhiteSpace(resolved.Version)
            ? new AppVersionInfo
            {
                Version = GetCurrentVersion(),
                Codename = "Administrate"
            }
            : resolved;
    }
}
