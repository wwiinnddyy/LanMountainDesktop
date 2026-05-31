using Avalonia.Controls.ApplicationLifetimes;
using LanMountainDesktop.Launcher.Views;

namespace LanMountainDesktop.Launcher.Shell.EntryHandlers;

internal static class LaunchEntryHandler
{
    public static SplashWindow CreateSplashWindow()
    {
        var window = new SplashWindow();
        try
        {
            var appRoot = Commands.ResolveAppRoot(LauncherRuntimeContext.Current);
            var versionInfo = new DeploymentLocator(appRoot).GetVersionInfo();
            window.SetVersionInfo(versionInfo.Version, versionInfo.Codename);
        }
        catch (Exception ex)
        {
            Logger.Warn($"Failed to set splash version info: {ex.Message}");
        }

        return window;
    }

    public static Task RunAsync(
        IClassicDesktopStyleApplicationLifetime desktop,
        CommandContext context,
        SplashWindow splashWindow) =>
        LauncherCompositionRoot.RunOrchestratorWithSplashAsync(desktop, context, splashWindow);
}
