using System.Security.Principal;
using LanMountainDesktop.Launcher.Models;

namespace LanMountainDesktop.Launcher.Services;

internal static class LauncherExecutionContext
{
    public static LauncherExecutionSnapshot Capture()
    {
        var userName = Environment.UserName ?? string.Empty;
        if (!OperatingSystem.IsWindows())
        {
            return new LauncherExecutionSnapshot(false, userName, null);
        }

        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return new LauncherExecutionSnapshot(
                principal.IsInRole(WindowsBuiltInRole.Administrator),
                userName,
                identity.User?.Value);
        }
        catch
        {
            return new LauncherExecutionSnapshot(false, userName, null);
        }
    }
}
