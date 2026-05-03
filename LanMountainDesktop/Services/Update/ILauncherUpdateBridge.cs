using LanMountainDesktop.Shared.Contracts.Update;

namespace LanMountainDesktop.Services.Update;

public interface ILauncherUpdateBridge
{
    Task<LaunchResult> LaunchInstallerAsync(InstallRequest request, CancellationToken ct);
    IObservable<InstallProgressReport> ProgressStream { get; }
    Task<bool> SupportsIpcAsync();
}
