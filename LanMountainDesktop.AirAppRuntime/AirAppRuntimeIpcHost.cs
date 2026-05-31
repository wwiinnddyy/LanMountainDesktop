using LanMountainDesktop.Shared.IPC;
using LanMountainDesktop.Shared.IPC.Abstractions.Services;

namespace LanMountainDesktop.AirAppRuntime;

internal sealed class AirAppRuntimeIpcHost : IDisposable
{
    private readonly PublicIpcHostService _host;

    public AirAppRuntimeIpcHost(
        AirAppLifecycleService lifecycleService,
        AirAppRuntimeControlService controlService)
    {
        _host = new PublicIpcHostService(IpcConstants.AirAppRuntimePipeName);
        _host.RegisterPublicService<IAirAppLifecycleService>(lifecycleService);
        _host.RegisterPublicService<IAirAppRuntimeControlService>(controlService);
    }

    public void Start()
    {
        _host.Start();
        AirAppRuntimeLogger.Info($"Air APP runtime IPC started. Pipe='{IpcConstants.AirAppRuntimePipeName}'.");
    }

    public void Dispose()
    {
        _host.Dispose();
    }
}
