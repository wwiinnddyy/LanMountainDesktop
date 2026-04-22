using System.Diagnostics;
using Microsoft.Win32;

namespace LanMountainDesktop.Launcher.Services;

/// <summary>
/// 老版本检测器 - 检测 0.8.x 及更早的单应用模式安装
/// </summary>
internal sealed class LegacyVersionDetector
{
    private const string LegacyAppName = "LanMountainDesktop";
    private const string LegacyExeName = "LanMountainDesktop.exe";

    /// <summary>
    /// 检测是否存在老版本安装
    /// </summary>
    public static LegacyVersionInfo? DetectLegacyInstallation()
    {
        // 1. 检查注册表（安装版）
        var registryInfo = DetectFromRegistry();
        if (registryInfo != null)
        {
            return registryInfo;
        }

        // 2. 检查常见安装目录
        var commonPaths = DetectFromCommonPaths();
        if (commonPaths != null)
        {
            return commonPaths;
        }

        // 3. 检查便携版位置
        var portableInfo = DetectPortableInstallation();
        if (portableInfo != null)
        {
            return portableInfo;
        }

        return null;
    }

    /// <summary>
    /// 从注册表检测安装信息
    /// </summary>
    private static LegacyVersionInfo? DetectFromRegistry()
    {
        try
        {
            // 检查 HKLM\Software\Microsoft\Windows\CurrentVersion\Uninstall
            using var key = Registry.LocalMachine.OpenSubKey(
                @$"Software\Microsoft\Windows\CurrentVersion\Uninstall\{LegacyAppName}");
            
            if (key != null)
            {
                var installLocation = key.GetValue("InstallLocation") as string;
                var displayVersion = key.GetValue("DisplayVersion") as string;
                var uninstallString = key.GetValue("UninstallString") as string;

                if (!string.IsNullOrWhiteSpace(installLocation) && 
                    File.Exists(Path.Combine(installLocation, LegacyExeName)))
                {
                    return new LegacyVersionInfo
                    {
                        Version = displayVersion ?? "0.8.x",
                        InstallPath = installLocation,
                        UninstallCommand = uninstallString,
                        InstallType = LegacyInstallType.Registry
                    };
                }
            }

            // 检查 HKCU（用户级安装）
            using var userKey = Registry.CurrentUser.OpenSubKey(
                @$"Software\Microsoft\Windows\CurrentVersion\Uninstall\{LegacyAppName}");
            
            if (userKey != null)
            {
                var installLocation = userKey.GetValue("InstallLocation") as string;
                var displayVersion = userKey.GetValue("DisplayVersion") as string;
                var uninstallString = userKey.GetValue("UninstallString") as string;

                if (!string.IsNullOrWhiteSpace(installLocation) && 
                    File.Exists(Path.Combine(installLocation, LegacyExeName)))
                {
                    return new LegacyVersionInfo
                    {
                        Version = displayVersion ?? "0.8.x",
                        InstallPath = installLocation,
                        UninstallCommand = uninstallString,
                        InstallType = LegacyInstallType.Registry
                    };
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[LegacyVersionDetector] Registry detection failed: {ex.Message}");
        }

        return null;
    }

    /// <summary>
    /// 从常见安装路径检测
    /// </summary>
    private static LegacyVersionInfo? DetectFromCommonPaths()
    {
        var commonPaths = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), LegacyAppName),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), LegacyAppName),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), LegacyAppName),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), LegacyAppName),
        };

        foreach (var path in commonPaths)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    // 检查是否存在老版本的特征文件（没有 app-* 目录）
                    var exePath = Path.Combine(path, LegacyExeName);
                    var hasAppDirs = Directory.GetDirectories(path, "app-*").Length > 0;

                    if (File.Exists(exePath) && !hasAppDirs)
                    {
                        // 尝试读取版本信息
                        var version = TryGetFileVersion(exePath);
                        
                        return new LegacyVersionInfo
                        {
                            Version = version ?? "0.8.x",
                            InstallPath = path,
                            UninstallCommand = FindUninstaller(path),
                            InstallType = LegacyInstallType.CommonPath
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LegacyVersionDetector] Path detection failed for {path}: {ex.Message}");
            }
        }

        return null;
    }

    /// <summary>
    /// 检测便携版安装
    /// </summary>
    private static LegacyVersionInfo? DetectPortableInstallation()
    {
        try
        {
            // 检查启动器所在目录的父目录（便携版常见布局）
            var launcherDir = AppContext.BaseDirectory;
            var parentDir = Path.GetFullPath(Path.Combine(launcherDir, ".."));

            if (Directory.Exists(parentDir))
            {
                var exePath = Path.Combine(parentDir, LegacyExeName);
                var hasAppDirs = Directory.GetDirectories(parentDir, "app-*").Length > 0;

                // 如果存在 exe 且没有 app-* 目录，可能是老版本
                if (File.Exists(exePath) && !hasAppDirs)
                {
                    var version = TryGetFileVersion(exePath);
                    
                    // 检查是否真的是老版本（通过文件版本或特定标记）
                    if (IsLegacyVersion(version))
                    {
                        return new LegacyVersionInfo
                        {
                            Version = version ?? "0.8.x",
                            InstallPath = parentDir,
                            UninstallCommand = null, // 便携版没有卸载程序
                            InstallType = LegacyInstallType.Portable
                        };
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[LegacyVersionDetector] Portable detection failed: {ex.Message}");
        }

        return null;
    }

    /// <summary>
    /// 查找卸载程序
    /// </summary>
    private static string? FindUninstaller(string installPath)
    {
        try
        {
            // 常见的卸载程序命名
            var uninstallerNames = new[] { "unins000.exe", "uninstall.exe", "Uninstall.exe" };
            
            foreach (var name in uninstallerNames)
            {
                var path = Path.Combine(installPath, name);
                if (File.Exists(path))
                {
                    return path;
                }
            }
        }
        catch { }

        return null;
    }

    /// <summary>
    /// 获取文件版本
    /// </summary>
    private static string? TryGetFileVersion(string filePath)
    {
        try
        {
            var versionInfo = FileVersionInfo.GetVersionInfo(filePath);
            return versionInfo.FileVersion;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 判断是否为老版本（版本号 < 1.0.0）
    /// </summary>
    private static bool IsLegacyVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return true; // 无法确定版本时，保守认为是老版本
        }

        if (Version.TryParse(version.Split(' ')[0], out var v))
        {
            return v.Major < 1;
        }

        return true;
    }

    /// <summary>
    /// 打开卸载界面
    /// </summary>
    public static void OpenUninstallInterface(LegacyVersionInfo info)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(info.UninstallCommand))
            {
                // 有卸载命令，直接执行
                var parts = info.UninstallCommand.Split(new[] { ' ' }, 2);
                var fileName = parts[0].Trim('"');
                var arguments = parts.Length > 1 ? parts[1] : "";
                Logger.Info(
                    $"Opening legacy uninstall interface with elevation reason 'legacy_uninstall'. " +
                    $"InstallPath='{info.InstallPath}'; Version='{info.Version}'.");

                Process.Start(new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    UseShellExecute = true,
                    Verb = "runas" // 请求管理员权限
                });
            }
            else
            {
                // 没有卸载命令，打开系统卸载面板
                Process.Start(new ProcessStartInfo
                {
                    FileName = "appwiz.cpl",
                    UseShellExecute = true
                });
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[LegacyVersionDetector] Failed to open uninstall: {ex.Message}");
            
            // 兜底：打开系统卸载面板
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "appwiz.cpl",
                    UseShellExecute = true
                });
            }
            catch { }
        }
    }

    /// <summary>
    /// 在资源管理器中显示老版本位置
    /// </summary>
    public static void ShowInExplorer(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{path}\"",
                UseShellExecute = false
            });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[LegacyVersionDetector] Failed to show in explorer: {ex.Message}");
        }
    }
}

/// <summary>
/// 老版本信息
/// </summary>
public class LegacyVersionInfo
{
    public string Version { get; set; } = "0.8.x";
    public string InstallPath { get; set; } = "";
    public string? UninstallCommand { get; set; }
    public LegacyInstallType InstallType { get; set; }
}

/// <summary>
/// 老版本安装类型
/// </summary>
public enum LegacyInstallType
{
    Registry,    // 注册表安装版
    CommonPath,  // 常见路径安装
    Portable     // 便携版
}
