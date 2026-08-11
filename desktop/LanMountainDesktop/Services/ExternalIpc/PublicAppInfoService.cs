using LanMountainDesktop.Shared.IPC;
using LanMountainDesktop.Shared.IPC.Abstractions.Services;
using LanMountainDesktop.Shared.Contracts.Launcher;

namespace LanMountainDesktop.Services.ExternalIpc;

internal sealed class PublicAppInfoService : IPublicAppInfoService
{
    private readonly DateTimeOffset _startedAt;

    public PublicAppInfoService(DateTimeOffset startedAt)
    {
        _startedAt = startedAt;
    }

    public PublicAppInfoSnapshot GetAppInfo()
    {
        var versionInfo = AppVersionProvider.ResolveForCurrentProcess();
        return new PublicAppInfoSnapshot(
            "LanMountainDesktop",
            versionInfo.Version,
            versionInfo.Codename,
            IpcConstants.DefaultPipeName,
            Environment.ProcessId,
            _startedAt);
    }
}
