using LanMountainDesktop.Shared.IPC;
using LanMountainDesktop.Shared.IPC.Abstractions.Services;

namespace LanMountainDesktop.Launcher.Services.AirApp;

internal sealed class LauncherAirAppLifecycleIpcHost : IDisposable
{
    private readonly PublicIpcHostService _host;

    public LauncherAirAppLifecycleIpcHost(LauncherAirAppLifecycleService lifecycleService)
    {
        LifecycleService = lifecycleService;
        _host = new PublicIpcHostService(IpcConstants.AirAppLifecyclePipeName);
        _host.RegisterPublicService<IAirAppLifecycleService>(lifecycleService);
    }

    public LauncherAirAppLifecycleService LifecycleService { get; }

    public void Start()
    {
        _host.Start();
        Logger.Info($"Air APP lifecycle IPC started. Pipe='{IpcConstants.AirAppLifecyclePipeName}'.");
    }

    public void Dispose()
    {
        _host.Dispose();
    }
}
