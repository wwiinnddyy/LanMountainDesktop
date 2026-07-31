using System.Security.Principal;

namespace LanDesktopPLONDS.Installer.Services;

internal static class InstallerElevation
{
    public static bool IsRunningElevated()
    {
        if (!OperatingSystem.IsWindows())
        {
            return true;
        }

        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    public static bool RequiresElevation(string installPath)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        var fullPath = Path.GetFullPath(installPath);
        return IsUnderSpecialFolder(fullPath, Environment.SpecialFolder.ProgramFiles)
               || IsUnderSpecialFolder(fullPath, Environment.SpecialFolder.ProgramFilesX86)
               || IsUnderWindowsDirectory(fullPath);
    }

    public static void EnsureCanInstall(string installPath)
    {
        if (RequiresElevation(installPath) && !IsRunningElevated())
        {
            throw new UnauthorizedAccessException(
                "所选安装路径需要管理员权限。请以管理员身份重新运行安装程序，或选择用户可写入的文件夹。");
        }
    }

    /// <summary>
    /// 确保当前进程有权限执行卸载操作（删除安装目录、注册表键、快捷方式）。
    /// </summary>
    public static void EnsureCanUninstall(string installPath)
    {
        if (RequiresElevation(installPath) && !IsRunningElevated())
        {
            throw new UnauthorizedAccessException(
                "卸载操作需要管理员权限。请以管理员身份重新运行安装程序。");
        }
    }

    private static bool IsUnderSpecialFolder(string fullPath, Environment.SpecialFolder folder)
    {
        var root = Environment.GetFolderPath(folder);
        return !string.IsNullOrWhiteSpace(root) && InstallerPathGuard.IsSameOrChildPath(root, fullPath);
    }

    private static bool IsUnderWindowsDirectory(string fullPath)
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        return !string.IsNullOrWhiteSpace(windows) && InstallerPathGuard.IsSameOrChildPath(windows, fullPath);
    }
}
