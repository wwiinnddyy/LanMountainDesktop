using dotnetCampus.Ipc.CompilerServices.Attributes;

namespace LanMountainDesktop.Shared.IPC.Abstractions.Services;

[IpcPublic(IgnoresIpcException = true)]
public interface IPublicAppInfoService
{
    PublicAppInfoSnapshot GetAppInfo();
}
