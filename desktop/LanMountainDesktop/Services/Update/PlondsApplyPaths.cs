using LanMountainDesktop.Shared.Contracts.Update;

namespace LanMountainDesktop.Services.Update;

internal sealed class PlondsApplyPaths
{
    public PlondsApplyPaths(string launcherRoot)
    {
        LauncherRoot = launcherRoot;
        IncomingRoot = UpdatePaths.GetIncomingDirectory(launcherRoot);
        SnapshotsRoot = UpdatePaths.GetSnapshotsDirectory(launcherRoot);
    }

    public string LauncherRoot { get; }
    public string IncomingRoot { get; }
    public string SnapshotsRoot { get; }
    public string InstallCheckpointPath => UpdatePaths.GetInstallCheckpointPath(LauncherRoot);

    public string ApplyLockPath => UpdatePaths.GetApplyInProgressLockPath(LauncherRoot);
    public string DeploymentLockPath => UpdatePaths.GetDeploymentLockPath(LauncherRoot);
    public string DownloadMarkerPath => UpdatePaths.GetDownloadMarkerPath(LauncherRoot);

    public string FileMapPath => Path.Combine(IncomingRoot, UpdatePaths.GetLegacyFileMapName());
    public string SignaturePath => Path.Combine(IncomingRoot, UpdatePaths.GetLegacySignatureName());
    public string ArchivePath => Path.Combine(IncomingRoot, UpdatePaths.GetLegacyArchiveName());

    public string PlondsFileMapPath => UpdatePaths.GetPlondsFileMapPath(LauncherRoot);
    public string PlondsSignaturePath => UpdatePaths.GetPlondsSignaturePath(LauncherRoot);
    public string PlondsUpdateMetadataPath => Path.Combine(IncomingRoot, UpdatePaths.GetPlondsUpdateMetadataName());
    public string PlondsObjectsRoot => Path.Combine(IncomingRoot, UpdatePaths.ObjectsDirectoryName);

    public string PublicKeyPath => Path.Combine(
        UpdatePaths.GetLauncherDataRoot(LauncherRoot),
        UpdatePaths.UpdateDirectoryName,
        UpdatePaths.GetPublicKeyFileName());

    public bool HasPlondsPayload => File.Exists(PlondsFileMapPath) && File.Exists(PlondsSignaturePath);

    public string GetSnapshotPath(string snapshotId) => Path.Combine(SnapshotsRoot, $"{snapshotId}.json");
}
