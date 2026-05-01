using System;
using System.IO;
using System.Linq;

namespace LanMountainDesktop.Services;

/// <summary>
/// 统一解析 Launcher 可执行文件路径的工具类。
/// </summary>
/// <remarks>
/// 安装后的目录结构：
/// <code>
/// {AppRoot}/                          ← 应用安装根目录
///   LanMountainDesktop.Launcher.exe   ← Launcher 可执行文件
///   .Launcher/                        ← Launcher 数据目录（日志、状态、配置等）
///   app-{version}/                    ← Host 部署目录
///     LanMountainDesktop.exe
///     ...
/// </code>
/// </remarks>
internal static class LauncherPathResolver
{
    private const string WindowsLauncherExeName = "LanMountainDesktop.Launcher.exe";
    private const string UnixLauncherExeName = "LanMountainDesktop.Launcher";

    private static string LauncherExecutableName =>
        OperatingSystem.IsWindows() ? WindowsLauncherExeName : UnixLauncherExeName;

    /// <summary>
    /// 解析 Launcher 可执行文件的完整路径。如果找不到则返回 null。
    /// </summary>
    public static string? ResolveLauncherExecutablePath()
    {
        var baseDirectory = AppContext.BaseDirectory;

        var candidates = new[]
        {
            // 1. 发布版（安装版）：Host 在 app-* 子目录中，Launcher 在父目录（应用根目录）
            Path.GetFullPath(Path.Combine(baseDirectory, "..", LauncherExecutableName)),

            // 2. 便携版 / 单文件发布：Launcher 与 Host 在同一目录
            Path.Combine(baseDirectory, LauncherExecutableName),

            // 3. 开发环境：Launcher 项目输出目录与 Host 项目输出目录同级
            Path.GetFullPath(Path.Combine(baseDirectory, "..", "..", "..", "LanMountainDesktop.Launcher", "bin", "Debug", "net10.0", LauncherExecutableName)),
            Path.GetFullPath(Path.Combine(baseDirectory, "..", "..", "..", "LanMountainDesktop.Launcher", "bin", "Release", "net10.0", LauncherExecutableName)),
        };

        return candidates
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(File.Exists);
    }

    /// <summary>
    /// 解析 Launcher 数据目录（.Launcher）的路径。
    /// 该目录与 app-* 文件夹同级，位于应用安装根目录下。
    /// </summary>
    public static string ResolveLauncherDataDirectory()
    {
        var baseDirectory = AppContext.BaseDirectory;

        // 优先尝试应用安装根目录（Host 的父目录）
        var appRootCandidate = Path.GetFullPath(Path.Combine(baseDirectory, ".."));
        var launcherDataDir = Path.Combine(appRootCandidate, ".Launcher");

        if (Directory.Exists(launcherDataDir) || CanWriteToDirectory(appRootCandidate))
        {
            return launcherDataDir;
        }

        // 回退到 Host 所在目录（便携模式或开发环境）
        return Path.Combine(baseDirectory, ".Launcher");
    }

    private static bool CanWriteToDirectory(string path)
    {
        try
        {
            var testFile = Path.Combine(path, $".write-test-{Guid.NewGuid():N}.tmp");
            File.WriteAllText(testFile, string.Empty);
            File.Delete(testFile);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
