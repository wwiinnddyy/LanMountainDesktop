using System.Diagnostics;
using System.Runtime.InteropServices;
using LanMountainDesktop.Shared.Contracts.Deployment;

namespace LanDesktopPLONDS.Installer.Services;

/// <summary>
/// 完整卸载流程编排。
/// 步骤：进程守护 → 删除快捷方式 → 删除 ARP 注册表键 → 删除安装目录。
/// </summary>
public sealed class UninstallService
{
    private readonly string _installPath;
    private readonly bool _silent;
    private readonly string? _registryBasePath;

    /// <summary>
    /// 初始化卸载服务。
    /// </summary>
    /// <param name="installPath">要卸载的安装根目录。</param>
    /// <param name="silent">静默模式：不显示确认窗口。</param>
    /// <param name="registryBasePath">可选：注入的注册表基路径（测试用）。</param>
    public UninstallService(string installPath, bool silent = false, string? registryBasePath = null)
    {
        _installPath = InstallerPathGuard.NormalizeInstallPath(installPath);
        _silent = silent;
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
            TryDeleteFile(Path.Combine(location, "LanMountainDesktop.url"));

            // 删除 .lnk 快捷方式（如果有的话）
            TryDeleteFile(Path.Combine(location, "LanMountainDesktop.lnk"));

            // 也检查 Programs 子目录
            var programsDir = Path.Combine(location, "Programs");
            if (Directory.Exists(programsDir))
            {
                TryDeleteFile(Path.Combine(programsDir, "LanMountainDesktop.url"));
                TryDeleteFile(Path.Combine(programsDir, "LanMountainDesktop.lnk"));
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
        TryDeleteDirectory(_installPath);
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

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // 快捷方式删除失败时忽略
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // 目录删除失败时忽略
        }
    }
}
