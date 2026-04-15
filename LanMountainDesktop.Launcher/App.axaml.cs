using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using LanMountainDesktop.Launcher.Services;

namespace LanMountainDesktop.Launcher;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var context = LauncherRuntimeContext.Current;
            var appRoot = Commands.ResolveAppRoot(context);
            var deploymentLocator = new DeploymentLocator(appRoot);
            
            // TODO: 从配置读取 GitHub 仓库信息
            var updateCheckService = new UpdateCheckService("ClassIsland", "LanMountainDesktop");
            
            var coordinator = new LauncherFlowCoordinator(
                context,
                deploymentLocator,
                new OobeStateService(appRoot),
                new UpdateEngineService(deploymentLocator),
                updateCheckService,
                new PluginInstallerService());

            _ = RunCoordinatorAsync(desktop, coordinator);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static async Task RunCoordinatorAsync(
        IClassicDesktopStyleApplicationLifetime desktop,
        LauncherFlowCoordinator coordinator)
    {
        var result = await coordinator.RunAsync().ConfigureAwait(false);
        await Commands.WriteResultIfNeededAsync(LauncherRuntimeContext.Current.GetOption("result"), result).ConfigureAwait(false);
        Environment.ExitCode = result.Success ? 0 : 1;
        await Dispatcher.UIThread.InvokeAsync(() => desktop.Shutdown(Environment.ExitCode), DispatcherPriority.Background);
    }
}
