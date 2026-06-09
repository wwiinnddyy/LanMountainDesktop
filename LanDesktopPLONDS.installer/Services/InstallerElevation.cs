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
                "The selected installation path requires administrator permission. Restart the installer as administrator or choose a user-writable folder.");
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
