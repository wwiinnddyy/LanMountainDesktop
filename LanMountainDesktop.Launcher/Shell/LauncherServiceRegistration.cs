using Microsoft.Extensions.DependencyInjection;

namespace LanMountainDesktop.Launcher.Shell;

internal static class LauncherServiceRegistration
{
    private static ServiceProvider? _provider;

    public static IServiceProvider Provider =>
        _provider ?? throw new InvalidOperationException("Launcher services are not initialized.");

    public static void Initialize(CommandContext context)
    {
        if (_provider is not null)
        {
            return;
        }

        var appRoot = Commands.ResolveAppRoot(context);
        var services = new ServiceCollection();
        services.AddSingleton(context);
        services.AddSingleton(new DeploymentLocator(appRoot));
        services.AddSingleton(sp => new OobeStateService(appRoot));
        services.AddSingleton(sp => new DataLocationResolver(appRoot));
        services.AddSingleton(sp => UpdateEngineFactory.Create(sp.GetRequiredService<DeploymentLocator>()));
        services.AddSingleton<HostLaunchService>();
        services.AddSingleton<StartupAttemptRegistry>();
        services.AddSingleton<ILaunchPhase, CleanupDeploymentsPhase>();
        services.AddSingleton<ILaunchPhase, ExistingHostProbePhase>();
        services.AddSingleton<ILaunchPhase, ApplyPendingUpdatePhase>();
        services.AddSingleton<ILaunchPhase, OobeGatePhase>();
        services.AddSingleton<ILaunchPhase, LaunchHostPhase>();
        services.AddSingleton<ILaunchPhase, MonitorStartupPhase>();
        services.AddSingleton(sp => new LaunchPipeline(sp.GetServices<ILaunchPhase>()));

        _provider = services.BuildServiceProvider();
    }

    public static LauncherOrchestrator CreateOrchestrator(
        CommandContext context,
        StartupAttemptRegistry startupAttemptRegistry,
        LauncherCoordinatorIpcServer coordinatorServer)
    {
        Initialize(context);
        var services = Provider;
        return new LauncherOrchestrator(
            context,
            services.GetRequiredService<DeploymentLocator>(),
            services.GetRequiredService<OobeStateService>(),
            services.GetRequiredService<IUpdateEngine>(),
            startupAttemptRegistry,
            coordinatorServer,
            services.GetRequiredService<LaunchPipeline>());
    }
}
