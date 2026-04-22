using LanMountainDesktop.Shared.IPC;
using LanMountainDesktop.Shared.IPC.Abstractions.Services;

namespace LanMountainDesktop.Services.ExternalIpc;

internal sealed class PublicAppInfoService : IPublicAppInfoService
{
    private readonly string _version;
    private readonly string _codename;
    private readonly DateTimeOffset _startedAt;

    public PublicAppInfoService(string version, string codename, DateTimeOffset startedAt)
    {
        _version = version;
        _codename = codename;
        _startedAt = startedAt;
    }

    public PublicAppInfoSnapshot GetAppInfo()
    {
        return new PublicAppInfoSnapshot(
            "LanMountainDesktop",
            _version,
            _codename,
            IpcConstants.DefaultPipeName,
            Environment.ProcessId,
            _startedAt);
    }
}
