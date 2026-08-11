using LanDesktopPLONDS.Installer.Models;

namespace LanDesktopPLONDS.Installer.Services;

public interface IOnlineInstallService
{
    Task<OnlineInstallPackageInfo> CheckLatestAsync(CancellationToken cancellationToken);

    Task InstallFreshAsync(
        string installPath,
        IProgress<InstallerDeployProgress>? progress,
        CancellationToken cancellationToken);

    Task InstallFreshAsync(
        string installPath,
        OnlineInstallOptions options,
        IProgress<InstallerDeployProgress>? progress,
        CancellationToken cancellationToken);

    Task RepairAsync(
        string installPath,
        IProgress<InstallerDeployProgress>? progress,
        CancellationToken cancellationToken);

    Task UpdateIncrementalAsync(
        string installPath,
        IProgress<InstallerDeployProgress>? progress,
        CancellationToken cancellationToken);
}
