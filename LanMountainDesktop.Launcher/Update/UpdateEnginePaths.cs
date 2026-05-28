using ContractsUpdate = LanMountainDesktop.Shared.Contracts.Update;

namespace LanMountainDesktop.Launcher.Update;

internal sealed class UpdateEnginePaths
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

    public UpdateEnginePaths(string appRoot)
    {
        AppRoot = appRoot;
        var resolver = new DataLocationResolver(appRoot);
        LauncherRoot = resolver.ResolveLauncherDataPath();
        IncomingRoot = Path.Combine(LauncherRoot, UpdateDirectoryName, IncomingDirectoryName);
        SnapshotsRoot = Path.Combine(LauncherRoot, SnapshotsDirectoryName);
        InstallCheckpointPath = ContractsUpdate.UpdatePaths.GetInstallCheckpointPath(appRoot);
    }

    public string AppRoot { get; }

    public string LauncherRoot { get; }

    public string IncomingRoot { get; }

    public string SnapshotsRoot { get; }

    public string InstallCheckpointPath { get; }

    public string ApplyLockPath => ContractsUpdate.UpdatePaths.GetApplyInProgressLockPath(AppRoot);

    public string DeploymentLockPath => ContractsUpdate.UpdatePaths.GetDeploymentLockPath(AppRoot);

    public string DownloadMarkerPath => ContractsUpdate.UpdatePaths.GetDownloadMarkerPath(AppRoot);

    public string FileMapPath => Path.Combine(IncomingRoot, SignedFileMapName);

    public string SignaturePath => Path.Combine(IncomingRoot, SignatureFileName);

    public string ArchivePath => Path.Combine(IncomingRoot, ArchiveFileName);

    public string PlondsFileMapPath => Path.Combine(IncomingRoot, PlondsFileMapName);

    public string PlondsSignaturePath => Path.Combine(IncomingRoot, PlondsSignatureFileName);

    public string PlondsUpdateMetadataPath => Path.Combine(IncomingRoot, PlondsUpdateMetadataName);

    public string PlondsObjectsRoot => Path.Combine(IncomingRoot, PlondsObjectsDirectoryName);

    public string PublicKeyPath => Path.Combine(LauncherRoot, UpdateDirectoryName, PublicKeyFileName);

    public string ExtractRoot => Path.Combine(IncomingRoot, "extracted");

    public bool HasPlondsPayload => File.Exists(PlondsFileMapPath) && File.Exists(PlondsSignaturePath);

    public bool HasLegacyPayload => File.Exists(FileMapPath) && File.Exists(ArchivePath);

    public string GetSnapshotPath(string snapshotId) => Path.Combine(SnapshotsRoot, $"{snapshotId}.json");
}
