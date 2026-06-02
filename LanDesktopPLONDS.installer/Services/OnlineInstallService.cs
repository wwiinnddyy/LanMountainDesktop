using LanDesktopPLONDS.Installer.Models;
using LanMountainDesktop.Shared.Contracts.Privacy;

namespace LanDesktopPLONDS.Installer.Services;

internal sealed class OnlineInstallService(
    InstallerPlondsClient plondsClient,
    FilesPackageInstaller packageInstaller,
    IPrivacyDeviceIdentityProvider privacyIdentity) : IOnlineInstallService
{
    private InstallerPlondsCandidate? _latestCandidate;

    public static OnlineInstallService CreateDefault(IPrivacyDeviceIdentityProvider privacyIdentity)
    {
        var httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(20)
        };
        var stagingRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LanMountainDesktop",
            "Installer",
            "PLONDS");
        return new OnlineInstallService(
            new InstallerPlondsClient(httpClient, stagingRoot),
            new FilesPackageInstaller(),
            privacyIdentity);
    }

    public async Task<OnlineInstallPackageInfo> CheckLatestAsync(CancellationToken cancellationToken)
    {
        var candidate = await plondsClient.FindLatestAsync(cancellationToken).ConfigureAwait(false);
        _latestCandidate = candidate;
        return new OnlineInstallPackageInfo(
            candidate.Manifest.CurrentVersion,
            candidate.Source.Id,
            candidate.FilesZipUrl,
            InstallerPlondsClient.EstimateInstallBytes(candidate.Manifest));
    }

    public async Task InstallFreshAsync(
        string installPath,
        IProgress<InstallerDeployProgress>? progress,
        CancellationToken cancellationToken)
    {
        await InstallFreshAsync(installPath, OnlineInstallOptions.Default, progress, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task InstallFreshAsync(
        string installPath,
        OnlineInstallOptions options,
        IProgress<InstallerDeployProgress>? progress,
        CancellationToken cancellationToken)
    {
        _ = privacyIdentity.GetOrCreateDeviceId();
        var candidate = _latestCandidate ?? await plondsClient.FindLatestAsync(cancellationToken).ConfigureAwait(false);
        var package = await plondsClient.DownloadAndPrepareFullPackageAsync(candidate, progress, cancellationToken).ConfigureAwait(false);
        await packageInstaller.InstallAsync(package, installPath, options, progress, cancellationToken).ConfigureAwait(false);
    }

    public Task RepairAsync(
        string installPath,
        IProgress<InstallerDeployProgress>? progress,
        CancellationToken cancellationToken)
    {
        _ = installPath;
        _ = progress;
        _ = cancellationToken;
        throw new NotSupportedException("Repair is reserved for a later installer version.");
    }

    public Task UpdateIncrementalAsync(
        string installPath,
        IProgress<InstallerDeployProgress>? progress,
        CancellationToken cancellationToken)
    {
        _ = installPath;
        _ = progress;
        _ = cancellationToken;
        throw new NotSupportedException("Incremental update is reserved for a later installer version.");
    }
}
