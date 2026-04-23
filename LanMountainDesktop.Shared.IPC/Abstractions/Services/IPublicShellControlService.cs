using dotnetCampus.Ipc.CompilerServices.Attributes;

namespace LanMountainDesktop.Shared.IPC.Abstractions.Services;

[IpcPublic(IgnoresIpcException = true)]
public interface IPublicShellControlService
{
    Task<PublicShellStatus> GetShellStatusAsync();

    Task<bool> ActivateMainWindowAsync();

    Task<PublicShellActivationResult> ActivateMainWindowWithStatusAsync();

    Task<PublicTrayStatus> EnsureTrayReadyAsync();

    Task<PublicTaskbarStatus> EnsureTaskbarEntryAsync();

    Task<bool> OpenSettingsAsync(string? pageTag = null);

    Task<bool> RestartAsync();

    Task<bool> ExitAsync();
}
