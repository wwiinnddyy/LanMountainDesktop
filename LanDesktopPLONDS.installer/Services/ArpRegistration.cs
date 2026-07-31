using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using LanMountainDesktop.Shared.Contracts.Deployment;

namespace LanDesktopPLONDS.Installer.Services;

/// <summary>
/// Windows ARP（添加/删除程序）注册表注册与移除。
/// 设计为可测试：注册表基路径可注入。
/// </summary>
public static class ArpRegistration
{
    private const string UninstallKeyName = "LanMountainDesktop";
    private const string DisplayName = "阑山桌面";
    private const string Publisher = "LanMountain";

    /// <summary>
    /// 注册 ARP 条目到 Windows 注册表。
    /// </summary>
    /// <param name="launcherRoot">安装根目录。</param>
    /// <param name="version">应用版本号。</param>
    /// <param name="registryBasePath">可选：注入的注册表基路径（测试用）。</param>
    public static void Register(string launcherRoot, string version, string? registryBasePath = null)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            var subKeyPath = GetUninstallSubKeyPath(registryBasePath);
            using var key = Microsoft.Win32.Registry.LocalMachine.CreateSubKey(subKeyPath);
            WriteRegistryValues(key, launcherRoot, version);
        }
        catch
        {
            // HKLM 写入失败时回退到 HKCU
            try
            {
                var subKeyPath = GetUninstallSubKeyPath(registryBasePath);
                using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(subKeyPath);
                WriteRegistryValues(key, launcherRoot, version);
            }
            catch
            {
                // ARP 注册是尽力而为
            }
        }
    }

    /// <summary>
    /// 移除 ARP 条目。
    /// </summary>
    /// <param name="registryBasePath">可选：注入的注册表基路径（测试用）。</param>
    public static void Remove(string? registryBasePath = null)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        // 尝试 HKLM 和 HKCU 都删除
        TryRemoveKey(Microsoft.Win32.Registry.LocalMachine, registryBasePath);
        TryRemoveKey(Microsoft.Win32.Registry.CurrentUser, registryBasePath);
    }

    /// <summary>
    /// 获取卸载子键路径。
    /// </summary>
    public static string GetUninstallSubKeyPath(string? registryBasePath = null)
    {
        var basePart = registryBasePath ?? @"Software\Microsoft\Windows\CurrentVersion\Uninstall";
        return $@"{basePart}\{UninstallKeyName}";
    }

    private static void WriteRegistryValues(
        Microsoft.Win32.RegistryKey key,
        string launcherRoot,
        string version)
    {
        var launcherExeName = DeploymentLayout.GetLauncherExecutableName();
        var displayIconPath = Path.Combine(launcherRoot, launcherExeName);
        var uninstallExePath = Path.Combine(launcherRoot, DeploymentLayout.LauncherStateDirectoryName, "uninstall.exe");
        var uninstallArgs = $"--uninstall \"{launcherRoot}\"";

        key.SetValue("DisplayName", DisplayName);
        key.SetValue("DisplayVersion", version);
        key.SetValue("Publisher", Publisher);
        key.SetValue("InstallLocation", launcherRoot);
        key.SetValue("DisplayIcon", displayIconPath);
        key.SetValue("UninstallString", $"\"{uninstallExePath}\" {uninstallArgs}");
        key.SetValue("NoModify", 1, Microsoft.Win32.RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, Microsoft.Win32.RegistryValueKind.DWord);

        // 估算安装大小（KB）
        try
        {
            var estimatedKb = EstimateInstallSizeKb(launcherRoot);
            key.SetValue("EstimatedSize", estimatedKb, Microsoft.Win32.RegistryValueKind.DWord);
        }
        catch
        {
            // 大小估算失败不影响注册
        }
    }

    private static void TryRemoveKey(Microsoft.Win32.RegistryKey rootKey, string? registryBasePath)
    {
        try
        {
            var subKeyPath = GetUninstallSubKeyPath(registryBasePath);
            rootKey.DeleteSubKeyTree(subKeyPath, throwOnMissingSubKey: false);
        }
        catch
        {
            // 删除失败时忽略
        }
    }

    private static int EstimateInstallSizeKb(string launcherRoot)
    {
        if (!Directory.Exists(launcherRoot))
        {
            return 0;
        }

        var totalBytes = Directory
            .EnumerateFiles(launcherRoot, "*", SearchOption.AllDirectories)
            .Sum(path =>
            {
                try
                {
                    return new FileInfo(path).Length;
                }
                catch
                {
                    return 0L;
                }
            });

        return (int)(totalBytes / 1024);
    }
}
