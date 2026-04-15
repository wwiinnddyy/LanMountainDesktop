using System.Globalization;

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
        var executable = OperatingSystem.IsWindows() ? "LanMountainDesktop.exe" : "LanMountainDesktop";
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
        return File.Exists(inParent) ? inParent : null;
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
}
