using LanMountainDesktop.Shared.Contracts.Update;

namespace LanMountainDesktop.Services.Update;

internal sealed class PlondsApplyPaths
{
    public const string UpdateDirectoryName = "update";
    public const string IncomingDirectoryName = "incoming";
    public const string SnapshotsDirectoryName = "snapshots";
    public const string SignedFileMapName = "files.json";
    public const string SignatureFileName = "files.json.sig";
    public const string ArchiveFileName = "update.zip";
    public const string PlondsFileMapName = "plonds-filemap.json";
    public const string PlondsSignatureFileName = "plonds-filemap.sig";
    public const string PlondsUpdateMetadataName = "plonds-update.json";
    public const string PlondsObjectsDirectoryName = "objects";
    public const string PublicKeyFileName = "public-key.pem";

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

    public string FileMapPath => Path.Combine(IncomingRoot, SignedFileMapName);
    public string SignaturePath => Path.Combine(IncomingRoot, SignatureFileName);
    public string ArchivePath => Path.Combine(IncomingRoot, ArchiveFileName);

    public string PlondsFileMapPath => Path.Combine(IncomingRoot, PlondsFileMapName);
    public string PlondsSignaturePath => Path.Combine(IncomingRoot, PlondsSignatureFileName);
    public string PlondsUpdateMetadataPath => Path.Combine(IncomingRoot, PlondsUpdateMetadataName);
    public string PlondsObjectsRoot => Path.Combine(IncomingRoot, PlondsObjectsDirectoryName);

    public string PublicKeyPath => Path.Combine(LauncherRoot, ".Launcher", UpdateDirectoryName, PublicKeyFileName);

    public bool HasPlondsPayload => File.Exists(PlondsFileMapPath) && File.Exists(PlondsSignaturePath);

    public string GetSnapshotPath(string snapshotId) => Path.Combine(SnapshotsRoot, $"{snapshotId}.json");
}
