using dotnetCampus.Ipc.CompilerServices.Attributes;

namespace LanMountainDesktop.Shared.IPC.Abstractions.Services;

[IpcPublic(IgnoresIpcException = true)]
public interface IPublicShellControlService
{
    Task<bool> ActivateMainWindowAsync();

    Task<bool> OpenSettingsAsync(string? pageTag = null);

    Task<bool> RestartAsync();

    Task<bool> ExitAsync();
}
