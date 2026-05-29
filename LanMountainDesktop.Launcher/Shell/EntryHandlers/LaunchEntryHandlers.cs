using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using LanMountainDesktop.Launcher.Models;
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

internal static class AirAppBrokerEntryHandler
{
    public static async Task RunAsync(IClassicDesktopStyleApplicationLifetime desktop, CommandContext context)
    {
        var appRoot = Commands.ResolveAppRoot(context);
        var requesterPid = context.GetIntOption("requester-pid", 0);
        var dataLocationResolver = new DataLocationResolver(appRoot);
        Logger.Info($"Air APP broker starting. AppRoot='{appRoot}'; RequesterPid={requesterPid}.");

        using var airAppIpcHost = new LauncherAirAppLifecycleIpcHost(
            new LauncherAirAppLifecycleService(
                new AirAppProcessStarter(
                    new AirAppHostLocator(),
                    () => appRoot,
                    () => null,
                    () => dataLocationResolver.ResolveDataRoot())));
        airAppIpcHost.Start();

        while (ShouldKeepAlive(requesterPid, airAppIpcHost.LifecycleService))
        {
            await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        }

        Logger.Info("Air APP broker exiting.");
        await Dispatcher.UIThread.InvokeAsync(() => desktop.Shutdown(0), DispatcherPriority.Background);
    }

    internal static bool ShouldKeepAirAppBrokerAlive(int requesterPid, LauncherAirAppLifecycleService lifecycleService)
    {
        if (requesterPid <= 0)
        {
            return lifecycleService.HasLiveAirApps();
        }

        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(requesterPid);
            return !process.HasExited || lifecycleService.HasLiveAirApps();
        }
        catch
        {
            return lifecycleService.HasLiveAirApps();
        }
    }

    private static bool ShouldKeepAlive(int requesterPid, LauncherAirAppLifecycleService lifecycleService) =>
        ShouldKeepAirAppBrokerAlive(requesterPid, lifecycleService);
}
