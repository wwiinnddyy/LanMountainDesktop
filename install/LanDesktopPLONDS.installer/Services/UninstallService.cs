using System.Diagnostics;
using System.Runtime.InteropServices;
using LanMountainDesktop.Shared.Contracts.Deployment;
using LanMountainDesktop.Shared.IO;

namespace LanDesktopPLONDS.Installer.Services;

/// <summary>
/// 完整卸载流程编排。
/// 步骤：进程守护 → 删除快捷方式 → 删除 ARP 注册表键 → 删除安装目录。
/// </summary>
public sealed class UninstallService
{
    private readonly string _installPath;
    private readonly string? _registryBasePath;

    /// <summary>
    /// 初始化卸载服务。
    /// </summary>
    /// <param name="installPath">要卸载的安装根目录。</param>
    /// <param name="registryBasePath">可选：注入的注册表基路径（测试用）。</param>
    public UninstallService(string installPath, string? registryBasePath = null)
    {
        _installPath = InstallerPathGuard.NormalizeInstallPath(installPath);
        _registryBasePath = registryBasePath;
    }

    /// <summary>
    /// 执行卸载操作。
    /// </summary>
    /// <returns>是否成功完成卸载。</returns>
    public bool Execute()
    {
        InstallerElevation.EnsureCanUninstall(_installPath);

        // 1. 检查运行中的进程
        RunningProcessGuard.EnsureNoRunningProcesses(_installPath);

        // 2. 删除快捷方式
        DeleteShortcuts();

        // 3. 删除 ARP 注册表键
        ArpRegistration.Remove(_registryBasePath);

        // 4. 删除安装目录
        RemoveInstallDirectory();

        return true;
    }

    /// <summary>
    /// 删除所有快捷方式（开始菜单、桌面、启动项）。
    /// </summary>
    private void DeleteShortcuts()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var shortcutLocations = new[]
        {
            GetShortcutDirectory(Environment.SpecialFolder.StartMenu),
            GetShortcutDirectory(Environment.SpecialFolder.CommonStartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.Startup),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup)
        };

        foreach (var location in shortcutLocations)
        {
            if (string.IsNullOrWhiteSpace(location) || !Directory.Exists(location))
            {
                continue;
            }

            // 删除 .url 快捷方式
            FileOperationRetryHelper.TryDeleteFile(Path.Combine(location, "LanMountainDesktop.url"), "Uninstaller");

            // 删除 .lnk 快捷方式（如果有的话）
            FileOperationRetryHelper.TryDeleteFile(Path.Combine(location, "LanMountainDesktop.lnk"), "Uninstaller");

            // 也检查 Programs 子目录
            var programsDir = Path.Combine(location, "Programs");
            if (Directory.Exists(programsDir))
            {
                FileOperationRetryHelper.TryDeleteFile(Path.Combine(programsDir, "LanMountainDesktop.url"), "Uninstaller");
                FileOperationRetryHelper.TryDeleteFile(Path.Combine(programsDir, "LanMountainDesktop.lnk"), "Uninstaller");
            }
        }
    }

    /// <summary>
    /// 删除安装目录。
    /// 如果是自身 exe 所在目录，使用 cmd /c 延迟删除。
    /// </summary>
    private void RemoveInstallDirectory()
    {
        if (!Directory.Exists(_installPath))
        {
            return;
        }

        var currentExePath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(currentExePath))
        {
            var normalizedExe = Path.GetFullPath(currentExePath);
            if (InstallerPathGuard.IsSameOrChildPath(_installPath, normalizedExe))
            {
                // 自身 exe 在安装目录内，使用 cmd /c 延迟删除
                SpawnDelayedDelete();
                return;
            }
        }

        // 非自身 exe 所在目录，直接删除
        FileOperationRetryHelper.TryDeleteDirectory(_installPath, true, "Uninstaller");
    }

    /// <summary>
    /// 使用 cmd /c 延迟删除自身 exe 所在目录。
    /// 这样进程退出后，cmd 会等待再删除。
    /// </summary>
    private void SpawnDelayedDelete()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            // 使用 cmd /c 的 rmdir 命令延迟删除
            var argument = $"/c timeout /t 3 /nobreak > nul 2>&1 & rmdir /s /q \"{_installPath}\"";
            var startInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = argument,
                WindowStyle = ProcessWindowStyle.Hidden,
                CreateNoWindow = true,
                UseShellExecute = false
            };
            Process.Start(startInfo);
        }
        catch
        {
            // 启动延迟删除进程失败时忽略
        }
    }

    private static string GetShortcutDirectory(Environment.SpecialFolder folder)
    {
        var path = Environment.GetFolderPath(folder);
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        return Path.Combine(path, "Programs");
    }
}
