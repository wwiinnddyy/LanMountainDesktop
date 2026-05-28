using Avalonia.Controls.ApplicationLifetimes;
using LanMountainDesktop.Launcher.Views;

namespace LanMountainDesktop.Launcher.Shell;

/// <summary>
/// Launcher GUI composition root. It only wires services and dispatches to entry coordinators.
/// </summary>
internal static class LauncherCompositionRoot
{
    public static LauncherOrchestrator CreateOrchestrator(
        CommandContext context,
        string appRoot,
        StartupAttemptRegistry startupAttemptRegistry,
        LauncherCoordinatorIpcServer coordinatorServer)
    {
        _ = appRoot;
        return LauncherServiceRegistration.CreateOrchestrator(context, startupAttemptRegistry, coordinatorServer);
    }

    public static Task RunOrchestratorWithSplashAsync(
        IClassicDesktopStyleApplicationLifetime desktop,
        CommandContext context,
        SplashWindow splashWindow) =>
        LauncherGuiCoordinator.RunAsync(desktop, context, splashWindow);
}
