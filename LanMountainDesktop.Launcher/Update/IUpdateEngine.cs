using LanMountainDesktop.Launcher.Models;

namespace LanMountainDesktop.Launcher.Update;

internal interface IUpdateEngine
{
    LauncherResult CheckPendingUpdate();

    Task<LauncherResult> DownloadAsync(string manifestUrl, string signatureUrl, string archiveUrl, CancellationToken cancellationToken);

    Task<LauncherResult> ApplyPendingUpdateAsync();

    LauncherResult RollbackLatest();

    void CleanupDestroyedDeployments();

    void CleanupIncomingArtifacts();
}
